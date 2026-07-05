namespace MarketLens.Core.Domain;

public sealed record PriceBarValue(
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long? Volume,
    DateTime? IngestedAt = null,
    bool Live = false);

public static class PriceBarIntervals
{
    public static string? Normalize(string? interval)
    {
        if (string.IsNullOrWhiteSpace(interval)) return null;

        return interval.Trim().ToLowerInvariant() switch
        {
            "d" => "1d",
            "w" => "1w",
            "mo" => "1mo",
            "60m" => "1h",
            var value => value,
        };
    }

    public static string SourceInterval(string interval)
        => Normalize(interval) switch
        {
            "3d" => "1d",
            "1w" => "1d",
            var value when !string.IsNullOrWhiteSpace(value) => value,
            _ => "1d",
        };

    public static bool IsDaily(string? interval)
        => Normalize(interval) == "1d";

    public static bool IsDerivedFromDaily(string? interval)
        => Normalize(interval) is "3d" or "1w";

    public static DateTime NormalizeTimestamp(string? interval, DateTime timestamp)
    {
        var utc = timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
        };

        return Normalize(interval) switch
        {
            "1mo" => new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            "1w" => WeekStart(utc),
            "1d" => DateTime.SpecifyKind(utc.Date, DateTimeKind.Utc),
            "1h" => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc),
            "30m" => FloorToMinuteBucket(utc, 30),
            "15m" => FloorToMinuteBucket(utc, 15),
            "5m" => FloorToMinuteBucket(utc, 5),
            "1m" => FloorToMinuteBucket(utc, 1),
            _ => utc,
        };
    }

    public static TimeSpan BucketSpan(string? interval)
        => Normalize(interval) switch
        {
            "1mo" => TimeSpan.FromDays(32),
            "1w" => TimeSpan.FromDays(7),
            "1d" => TimeSpan.FromDays(1),
            "1h" => TimeSpan.FromHours(1),
            "30m" => TimeSpan.FromMinutes(30),
            "15m" => TimeSpan.FromMinutes(15),
            "5m" => TimeSpan.FromMinutes(5),
            "1m" => TimeSpan.FromMinutes(1),
            _ => TimeSpan.Zero,
        };

    public static TimeSpan RefreshThreshold(string? interval)
        => Normalize(interval) switch
        {
            "1d" => TimeSpan.FromHours(12),
            "1h" => TimeSpan.FromMinutes(45),
            "30m" => TimeSpan.FromMinutes(35),
            "15m" => TimeSpan.FromMinutes(20),
            "5m" => TimeSpan.FromMinutes(8),
            "1m" => TimeSpan.FromMinutes(3),
            "1mo" => TimeSpan.FromHours(24),
            _ => TimeSpan.FromHours(12),
        };

    public static TimeSpan EmptyBackoff(string? interval, int emptyResultCount)
    {
        var multiplier = Math.Clamp(emptyResultCount, 1, 6);
        return Normalize(interval) switch
        {
            "1d" => TimeSpan.FromHours(6 * multiplier),
            "1mo" => TimeSpan.FromDays(multiplier),
            _ => TimeSpan.FromMinutes(15 * multiplier),
        };
    }

    public static TimeSpan FailureBackoff(int consecutiveFailures)
    {
        var minutes = Math.Min(12 * 60, Math.Pow(2, Math.Max(consecutiveFailures - 1, 0)) * 15);
        return TimeSpan.FromMinutes(minutes);
    }

    private static DateTime WeekStart(DateTime utc)
    {
        var date = utc.Date;
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return DateTime.SpecifyKind(date.AddDays(-offset), DateTimeKind.Utc);
    }

    private static DateTime FloorToMinuteBucket(DateTime utc, int minutes)
    {
        var minute = utc.Minute - utc.Minute % minutes;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, minute, 0, DateTimeKind.Utc);
    }
}

public static class PriceBarAggregation
{
    public static IReadOnlyList<PriceBarValue> Canonicalize(
        string sourceInterval,
        IEnumerable<PriceBarValue> rows)
        => rows
            .GroupBy(row => PriceBarIntervals.NormalizeTimestamp(sourceInterval, row.Timestamp))
            .Select(group =>
            {
                var selected = group
                    .OrderByDescending(row => row.IngestedAt ?? row.Timestamp)
                    .ThenByDescending(row => row.Timestamp)
                    .First();

                return selected with
                {
                    Timestamp = group.Key,
                    IngestedAt = selected.IngestedAt,
                };
            })
            .OrderBy(row => row.Timestamp)
            .ToList();

    public static IReadOnlyList<PriceBarValue> Build(
        string requestedInterval,
        IEnumerable<PriceBarValue> rows,
        int limit)
    {
        var interval = PriceBarIntervals.Normalize(requestedInterval) ?? "1d";
        var sourceInterval = PriceBarIntervals.SourceInterval(interval);
        var canonical = Canonicalize(sourceInterval, rows);
        var safeLimit = Math.Max(1, limit);

        if (!PriceBarIntervals.IsDerivedFromDaily(interval))
            return canonical.Take(safeLimit).ToList();

        return canonical
            .GroupBy(row => BucketStart(row.Timestamp, interval))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var ordered = group.OrderBy(row => row.Timestamp).ToList();
                var first = ordered[0];
                var last = ordered[^1];
                long? volume = null;

                foreach (var row in ordered)
                {
                    if (row.Volume is long v)
                        volume = (volume ?? 0L) + v;
                }

                return new PriceBarValue(
                    group.Key,
                    first.Open,
                    ordered.Max(row => row.High),
                    ordered.Min(row => row.Low),
                    last.Close,
                    volume,
                    ordered.Max(row => row.IngestedAt),
                    ordered.Any(row => row.Live));
            })
            .Take(safeLimit)
            .ToList();
    }

    private static DateTime BucketStart(DateTime timestamp, string interval)
    {
        var date = PriceBarIntervals.NormalizeTimestamp("1d", timestamp).Date;
        if (interval == "1w")
        {
            var offset = ((int)date.DayOfWeek + 6) % 7;
            return DateTime.SpecifyKind(date.AddDays(-offset), DateTimeKind.Utc);
        }

        var days = (int)(date - DateTime.UnixEpoch.Date).TotalDays;
        var bucketOffset = ((days % 3) + 3) % 3;
        return DateTime.SpecifyKind(date.AddDays(-bucketOffset), DateTimeKind.Utc);
    }
}

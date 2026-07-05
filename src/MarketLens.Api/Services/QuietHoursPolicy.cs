using Microsoft.Extensions.Options;

namespace MarketLens.Api.Services;

public class QuietHoursOptions
{
    public bool Enabled { get; set; } = false;
    public string TimeZone { get; set; } = "America/Chicago";
    public string Start { get; set; } = "08:00";
    public string End { get; set; } = "18:00";
    public int[] Days { get; set; } = new[] { 1, 2, 3, 4, 5 };
}

public interface IQuietHoursPolicy
{
    bool IsQuietNow();
}

public sealed class QuietHoursPolicy(IOptionsMonitor<QuietHoursOptions> options) : IQuietHoursPolicy
{
    public bool IsQuietNow()
    {
        var opts = options.CurrentValue;
        if (!opts.Enabled) return false;

        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(opts.TimeZone); }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var dow = (int)localNow.DayOfWeek;
        if (opts.Days is { Length: > 0 } && !opts.Days.Contains(dow)) return false;

        if (!TimeOnly.TryParse(opts.Start, out var start)) return false;
        if (!TimeOnly.TryParse(opts.End, out var end)) return false;

        var now = TimeOnly.FromDateTime(localNow);
        return start <= end
            ? now >= start && now < end
            : now >= start || now < end;
    }
}

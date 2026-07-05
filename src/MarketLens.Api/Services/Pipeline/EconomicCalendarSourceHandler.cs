using System.Text.Json;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services.Pipeline;

public sealed record EconomicCalendarSourceResult(bool SourceFound, int RecordsFetched, int Upserts);

public sealed class EconomicCalendarSourceHandler(
    IEnumerable<IEconomicCalendarSource> sources,
    MarketLensDbContext db,
    ILogger<EconomicCalendarSourceHandler> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EconomicCalendarSourceResult> ProcessAsync(
        string sourceName,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var payload = ParsePayload(payloadJson);
        var source = sources.FirstOrDefault(s =>
            string.Equals(s.Name, sourceName, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            logger.LogWarning("Economic calendar source {Source} is no longer registered", sourceName);
            return new EconomicCalendarSourceResult(false, 0, 0);
        }

        IReadOnlyList<string> symbols = payload.Symbols.Count > 0
            ? payload.Symbols
            : await LoadTrackedSymbolsAsync(payload.SymbolLookbackDays, cancellationToken);

        var records = await source.FetchAsync(payload.FromUtc, payload.ToUtc, symbols, cancellationToken);
        if (records.Count == 0)
            return new EconomicCalendarSourceResult(true, 0, 0);

        var upserts = await UpsertRecordsAsync(source.Name, records, cancellationToken);
        logger.LogInformation(
            "Refreshed {Count} economic calendar entries from {Source}",
            upserts,
            source.Name);

        return new EconomicCalendarSourceResult(true, records.Count, upserts);
    }

    private async Task<IReadOnlyList<string>> LoadTrackedSymbolsAsync(
        int symbolLookbackDays,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-Math.Max(symbolLookbackDays, 1));

        var trackedSymbols = await db.Articles
            .AsNoTracking()
            .Where(a => a.Symbol != null && a.PublishedAt >= cutoff)
            .Select(a => a.Symbol!)
            .Distinct()
            .ToListAsync(cancellationToken);

        var assetSymbols = await db.ResearchAssets
            .AsNoTracking()
            .Where(a => a.Symbol != null)
            .Select(a => a.Symbol!)
            .Distinct()
            .ToListAsync(cancellationToken);

        return trackedSymbols.Concat(assetSymbols)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<int> UpsertRecordsAsync(
        string sourceName,
        IReadOnlyList<EconomicEventRecord> records,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var sourceIds = records.Select(r => r.SourceId).ToList();
        var existing = await db.EconomicEvents
            .Where(e => e.Source == sourceName && sourceIds.Contains(e.SourceId))
            .ToDictionaryAsync(e => e.SourceId, cancellationToken);

        var upserts = 0;
        foreach (var rec in records)
        {
            if (existing.TryGetValue(rec.SourceId, out var current))
            {
                current.EventType = rec.EventType;
                current.Symbol = rec.Symbol;
                current.Label = rec.Label;
                current.ScheduledAt = rec.ScheduledAt;
                current.IsTimeSpecific = rec.IsTimeSpecific;
                current.Status = rec.Status;
                current.Notes = rec.Notes;
                current.RawPayload = rec.RawJson;
                current.UpdatedAt = now;
            }
            else
            {
                db.EconomicEvents.Add(new EconomicEvent
                {
                    Id = Guid.NewGuid(),
                    Source = rec.Source,
                    SourceId = rec.SourceId,
                    EventType = rec.EventType,
                    Symbol = rec.Symbol,
                    Label = rec.Label,
                    ScheduledAt = rec.ScheduledAt,
                    IsTimeSpecific = rec.IsTimeSpecific,
                    Status = rec.Status,
                    Notes = rec.Notes,
                    RawPayload = rec.RawJson,
                    IngestedAt = now,
                    UpdatedAt = now,
                });
            }

            upserts++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return upserts;
    }

    private static EconomicCalendarPayload ParsePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return EconomicCalendarPayload.Default();

        try
        {
            return JsonSerializer.Deserialize<EconomicCalendarPayload>(payloadJson, JsonOptions)
                ?? EconomicCalendarPayload.Default();
        }
        catch (JsonException)
        {
            return EconomicCalendarPayload.Default();
        }
    }

    private sealed class EconomicCalendarPayload
    {
        public DateTime FromUtc { get; set; } = DateTime.UtcNow.AddDays(-30);
        public DateTime ToUtc { get; set; } = DateTime.UtcNow.AddDays(90);
        public int SymbolLookbackDays { get; set; } = 30;
        public List<string> Symbols { get; set; } = [];

        public static EconomicCalendarPayload Default() => new();
    }
}

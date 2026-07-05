using System.Text.Json;
using MarketLens.Api.Services;
using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services.Pipeline;

public sealed record IdeaMemoWorkResult(bool Processed, bool Generated, bool Current);

public sealed class IdeaMemoWorkHandler(
    MarketLensDbContext db,
    IdeaMemoService memoService,
    ILogger<IdeaMemoWorkHandler> logger)
{
    public async Task<IdeaMemoWorkResult> ProcessAsync(
        string naturalKey,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var payload = ParsePayload(payloadJson);
        if (payload.MemoId is { } memoId)
            return await ProcessPendingMemoAsync(memoId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(payload.Symbol))
            return await ProcessSymbolAsync(payload.Symbol!, payload.WindowDays, cancellationToken);

        if (Guid.TryParse(naturalKey, out var naturalMemoId))
            return await ProcessPendingMemoAsync(naturalMemoId, cancellationToken);

        var parts = naturalKey.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[1], out var windowDays))
            return await ProcessSymbolAsync(parts[0], windowDays, cancellationToken);

        throw new InvalidOperationException($"Unsupported idea memo work item '{naturalKey}'.");
    }

    private async Task<IdeaMemoWorkResult> ProcessPendingMemoAsync(
        Guid memoId,
        CancellationToken cancellationToken)
    {
        var memo = await db.IdeaMemos
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == memoId, cancellationToken);

        if (memo is null)
            return new IdeaMemoWorkResult(Processed: false, Generated: false, Current: false);

        if (memo.Status == IdeaMemoStatuses.Ready)
            return new IdeaMemoWorkResult(Processed: false, Generated: false, Current: true);
        if (memo.Status != IdeaMemoStatuses.Pending)
            return new IdeaMemoWorkResult(Processed: false, Generated: false, Current: false);

        var processed = await memoService.ProcessPendingMemoAsync(memoId, cancellationToken);
        if (processed is null)
            return new IdeaMemoWorkResult(Processed: false, Generated: false, Current: false);

        var generated = processed.Status == IdeaMemoStatuses.Ready;
        logger.LogInformation(
            "Processed pending idea memo {MemoId} for {Symbol} {WindowDays}d with status {Status}",
            memoId,
            processed.Symbol,
            processed.WindowDays,
            processed.Status);

        return new IdeaMemoWorkResult(Processed: true, Generated: generated, Current: generated);
    }

    private async Task<IdeaMemoWorkResult> ProcessSymbolAsync(
        string rawSymbol,
        int rawWindowDays,
        CancellationToken cancellationToken)
    {
        var symbol = rawSymbol.Trim().ToUpperInvariant();
        var windowDays = Math.Clamp(rawWindowDays, 7, 365);
        var dto = await memoService.GetOrQueueAsync(symbol, windowDays, cancellationToken);

        if (dto.IsCurrent || dto.CurrentStatus == IdeaMemoStatuses.Ready)
            return new IdeaMemoWorkResult(Processed: false, Generated: false, Current: true);

        if (dto.CurrentStatus != IdeaMemoStatuses.Pending)
            return new IdeaMemoWorkResult(Processed: false, Generated: false, Current: false);

        var memo = await memoService.GenerateAndStoreAsync(symbol, windowDays, force: false, cancellationToken);
        var generated = memo.Status == IdeaMemoStatuses.Ready;
        logger.LogInformation(
            "Processed idea memo candidate {Symbol} {WindowDays}d with status {Status}",
            symbol,
            windowDays,
            memo.Status);

        return new IdeaMemoWorkResult(Processed: true, Generated: generated, Current: generated);
    }

    private static IdeaMemoPayload ParsePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new IdeaMemoPayload();

        try
        {
            return JsonSerializer.Deserialize<IdeaMemoPayload>(
                payloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new IdeaMemoPayload();
        }
        catch (JsonException)
        {
            return new IdeaMemoPayload();
        }
    }

    private sealed class IdeaMemoPayload
    {
        public Guid? MemoId { get; set; }
        public string? Symbol { get; set; }
        public int WindowDays { get; set; } = 90;
    }
}

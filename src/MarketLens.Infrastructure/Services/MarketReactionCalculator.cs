using MarketLens.Core.Models;

namespace MarketLens.Infrastructure.Services;

public class MarketReactionCalculator
{
    public decimal? ComputeMovePercent(MarketDataQuote? quote)
    {
        if (quote?.LastPrice is null || quote.PreviousClose is null || quote.PreviousClose == 0)
            return null;

        return Math.Round(((quote.LastPrice.Value - quote.PreviousClose.Value) / quote.PreviousClose.Value) * 100m, 4);
    }

    public decimal? ComputeRelativeMovePercent(decimal? symbolMovePercent, decimal? benchmarkMovePercent)
    {
        if (symbolMovePercent is null || benchmarkMovePercent is null)
            return null;

        return Math.Round(symbolMovePercent.Value - benchmarkMovePercent.Value, 4);
    }

    public decimal? ComputeRelativeVolume(long? volume, long? averageVolume)
    {
        if (volume is null || averageVolume is null || averageVolume == 0)
            return null;

        return Math.Round(volume.Value / (decimal)averageVolume.Value, 4);
    }

    public decimal ComputeReactionScore(decimal? relativeMovePercent, decimal? movePercent, decimal? relativeVolume)
    {
        var relativeMoveComponent = Math.Min(Math.Abs(relativeMovePercent ?? 0m) / 6m, 1m) * 0.70m;
        var absoluteMoveComponent = Math.Min(Math.Abs(movePercent ?? 0m) / 8m, 1m) * 0.20m;
        var volumeComponent = relativeVolume is null ? 0m : Math.Min(Math.Max(relativeVolume.Value - 1m, 0m) / 4m, 1m) * 0.10m;

        return Math.Round(relativeMoveComponent + absoluteMoveComponent + volumeComponent, 4);
    }
}

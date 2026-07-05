using System.Reflection;
using MarketLens.Core.Domain;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class PipelineWorkTypesTests
{
    private static readonly string[] ExpectedWorkTypes =
    [
        PipelineWorkTypes.ArticleIngestion,
        PipelineWorkTypes.ArticleBodyEnrichment,
        PipelineWorkTypes.TranscriptIngestion,
        PipelineWorkTypes.FilingChunkExtraction,
        PipelineWorkTypes.Form4Processing,
        PipelineWorkTypes.EventExtraction,
        PipelineWorkTypes.ResearchMatching,
        PipelineWorkTypes.StanceClassification,
        PipelineWorkTypes.IdeaMemo,
        PipelineWorkTypes.EconomicCalendar,
        PipelineWorkTypes.EarningsCalendar,
        PipelineWorkTypes.FundamentalsRefresh,
        PipelineWorkTypes.ThesisPlanRefresh,
        PipelineWorkTypes.ThesisBootstrap,
        PipelineWorkTypes.ResearchSnapshot,
        PipelineWorkTypes.MarketQuote,
        PipelineWorkTypes.PriceBarBackfill,
        PipelineWorkTypes.MarketSnapshot,
    ];

    [Fact]
    public void QueueBackedWorkTypesAreExplicitUniqueAndNormalized()
    {
        var declaredWorkTypes = typeof(PipelineWorkTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(workType => workType, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ExpectedWorkTypes.OrderBy(workType => workType, StringComparer.Ordinal),
            declaredWorkTypes);
        Assert.Equal(declaredWorkTypes.Length, declaredWorkTypes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(declaredWorkTypes, workType =>
        {
            Assert.False(string.IsNullOrWhiteSpace(workType));
            Assert.Equal(workType.Trim(), workType);
            Assert.Equal(workType.ToLowerInvariant(), workType);
            Assert.DoesNotContain(' ', workType);
        });
    }
}

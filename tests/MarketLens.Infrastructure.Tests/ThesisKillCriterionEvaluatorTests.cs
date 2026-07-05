using MarketLens.Core.Domain;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class ThesisKillCriterionEvaluatorTests
{
    [Fact]
    public void Evaluate_LeavesCriterionDormantWhenContradictionsDoNotMatchKeywords()
    {
        var now = new DateTime(2026, 5, 11, 18, 0, 0, DateTimeKind.Utc);
        var result = ThesisKillCriterionEvaluator.Evaluate(
            "Cobre Panama and Grasberg both return to full production",
            "cobre panama full restart,grasberg full capacity",
            [
                new KillEvidenceSignal(
                    "Lithium prices remain volatile after an analyst downgrade.",
                    "trade_press",
                    0.90m,
                    false,
                    now),
            ],
            now);

        Assert.Equal("dormant", result.ThreatLevel);
        Assert.Equal(0, result.ContradictingEvidenceCount);
    }

    [Fact]
    public void Evaluate_EscalatesStrongPrimaryContradictionToCritical()
    {
        var now = new DateTime(2026, 5, 11, 18, 0, 0, DateTimeKind.Utc);
        var result = ThesisKillCriterionEvaluator.Evaluate(
            "Cobre Panama and Grasberg both return to full production",
            "cobre panama full restart,grasberg full capacity",
            [
                new KillEvidenceSignal(
                    "Official update confirms Cobre Panama full restart and Grasberg full capacity.",
                    "primary",
                    0.95m,
                    false,
                    now),
            ],
            now);

        Assert.Equal("critical", result.ThreatLevel);
        Assert.Equal(1, result.ContradictingEvidenceCount);
        Assert.True(result.Score >= 0.90m);
        Assert.Contains("Top hit", result.LastTriggeredReason);
    }

    [Fact]
    public void Evaluate_RejectsLowConfidenceContradictions()
    {
        var now = new DateTime(2026, 5, 11, 18, 0, 0, DateTimeKind.Utc);
        var result = ThesisKillCriterionEvaluator.Evaluate(
            "Nuclear accident anywhere in the world",
            "nuclear accident,meltdown,radiation leak",
            [
                new KillEvidenceSignal(
                    "Rumor mentions a nuclear accident but classifier confidence is low.",
                    "aggregator",
                    0.40m,
                    false,
                    now),
            ],
            now);

        Assert.Equal("dormant", result.ThreatLevel);
        Assert.Equal(0, result.ContradictingEvidenceCount);
    }

    [Fact]
    public void Evaluate_DoesNotUseBroadScenarioWordsWhenMonitoringKeywordsExist()
    {
        var now = new DateTime(2026, 5, 11, 18, 0, 0, DateTimeKind.Utc);
        var result = ThesisKillCriterionEvaluator.Evaluate(
            "Nuclear accident anywhere in the world",
            "nuclear accident,meltdown,radiation leak,reactor failure,nuclear emergency,evacuation zone",
            [
                new KillEvidenceSignal(
                    "The report mentions nuclear sector funding and reactor fuel demand, but no incident.",
                    "primary",
                    0.90m,
                    false,
                    now),
            ],
            now);

        Assert.Equal("dormant", result.ThreatLevel);
        Assert.Equal(0, result.ContradictingEvidenceCount);
    }
}

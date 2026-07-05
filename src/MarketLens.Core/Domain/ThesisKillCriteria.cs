using System.Text.RegularExpressions;

namespace MarketLens.Core.Domain;

public sealed record KillEvidenceSignal(
    string Text,
    string? SourceTier,
    decimal? StanceConfidence,
    bool IsPinned,
    DateTime MatchedAt);

public sealed record KillCriterionEvaluation(
    string ThreatLevel,
    int ContradictingEvidenceCount,
    decimal Score,
    string? LastTriggeredReason);

public static class ThesisKillCriterionEvaluator
{
    public static KillCriterionEvaluation Evaluate(
        string scenario,
        string monitoringKeywords,
        IEnumerable<KillEvidenceSignal> evidence,
        DateTime? now = null)
    {
        var monitoringTerms = ParseTerms(monitoringKeywords)
            .Where(t => t.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var terms = monitoringTerms.Length > 0
            ? monitoringTerms
            : ParseTerms(scenario)
                .Where(t => t.Length >= 4)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var matches = evidence
            .Select(signal => new
            {
                Signal = signal,
                TermMatched = terms.Length == 0 || terms.Any(t => ContainsTerm(signal.Text, t)),
            })
            .Where(x => x.TermMatched)
            .Select(x => new
            {
                x.Signal,
                Score = Score(x.Signal, now ?? DateTime.UtcNow),
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToArray();

        var count = matches.Length;
        var score = Math.Round(matches.Sum(x => x.Score), 4);
        var top = matches.FirstOrDefault();
        var threat = ThreatLevel(count, score, top?.Score ?? 0m);
        var reason = top is null
            ? null
            : BuildReason(top.Signal.Text, count, score);

        return new KillCriterionEvaluation(threat, count, score, reason);
    }

    private static decimal Score(KillEvidenceSignal signal, DateTime now)
    {
        var confidence = signal.StanceConfidence ?? 0.65m;
        if (confidence < 0.65m) return 0m;

        var ageDays = Math.Max(0, (now - signal.MatchedAt).TotalDays);
        var recency = ageDays switch
        {
            <= 7 => 1.00m,
            <= 30 => 0.85m,
            <= 90 => 0.65m,
            _ => 0.45m,
        };

        var pinned = signal.IsPinned ? 1.25m : 1.00m;
        return TierWeight(signal.SourceTier) * confidence * recency * pinned;
    }

    private static string ThreatLevel(int count, decimal score, decimal topScore)
    {
        if (count == 0 || score <= 0m) return "dormant";
        if (score >= 1.80m || topScore >= 0.90m) return "critical";
        if (score >= 0.90m || count >= 2) return "elevated";
        return "watching";
    }

    public static decimal TierWeight(string? tier) => tier switch
    {
        "primary" => 1.00m,
        "wire" => 0.85m,
        "industry_analyst" => 0.75m,
        "trade_press" => 0.55m,
        "aggregator" => 0.30m,
        _ => 0.40m,
    };

    private static IEnumerable<string> ParseTerms(string value)
        => value
            .Split([',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t));

    private static bool ContainsTerm(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(term)) return false;
        var pattern = $@"(?<![A-Za-z0-9]){Regex.Escape(term)}(?![A-Za-z0-9])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }

    private static string BuildReason(string text, int count, decimal score)
    {
        var normalized = Regex.Replace(text, @"\s+", " ").Trim();
        if (normalized.Length > 240)
            normalized = normalized[..237] + "...";
        return $"{count} credible contradicting evidence item(s), score {score:F2}. Top hit: {normalized}";
    }
}

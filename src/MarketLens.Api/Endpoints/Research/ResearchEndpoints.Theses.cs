using System.Text.Json;
using MarketLens.Api.HostedServices;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints.Research;

public static partial class ResearchEndpoints
{
    private static void MapThesisEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/theses", async (
            MarketLensDbContext db,
            string? status,
            string? symbol,
            int? take,
            CancellationToken ct) =>
        {
            var q = db.ResearchTheses.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(t => t.Status == status);
            if (!string.IsNullOrWhiteSpace(symbol))
            {
                var s = symbol.ToUpperInvariant();
                q = q.Where(t => t.ThesisAssets.Any(ta => ta.Asset!.Symbol == s));
            }

            var limit = Math.Clamp(take ?? 50, 1, 200);
            var raw = await q
                .OrderByDescending(t => t.UpdatedAt)
                .Take(limit)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Status,
                    t.ThesisText,
                    t.Summary,
                    t.CreatedAt,
                    t.UpdatedAt,
                    hasEmbedding = t.Embedding != null,
                    planStatus = t.Plan == null ? "pending" : "ready",
                    plan = t.Plan,
                    planGeneratedAt = t.PlanGeneratedAt,
                    assetCount = t.ThesisAssets.Count,
                    ruleCount = t.Rules.Count,
                    evidenceCount = t.Evidence.Count,
                    pendingEvidenceCount = t.Evidence.Count(e => e.ReviewStatus == "pending"),
                    supportedCount = t.Evidence.Count(e => e.Stance == StanceValues.Supports),
                    contradictedCount = t.Evidence.Count(e => e.Stance == StanceValues.Contradicts),
                    lastEvidenceAt = t.Evidence
                        .OrderByDescending(e => e.MatchedAt)
                        .Select(e => (DateTime?)e.MatchedAt)
                        .FirstOrDefault(),
                    symbol = t.ThesisAssets
                        .Where(ta => ta.Asset!.Symbol != null)
                        .OrderBy(ta => ta.Role == "primary" ? 0 : 1)
                        .Select(ta => ta.Asset!.Symbol)
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            var items = raw.Select(t => new
            {
                t.Id, t.Name, t.Status, t.ThesisText, t.Summary,
                t.CreatedAt, t.UpdatedAt, t.hasEmbedding, t.planStatus,
                planAdequacy = PlanAdequacy.From(t.plan),
                t.planGeneratedAt, t.assetCount, t.ruleCount, t.evidenceCount,
                t.pendingEvidenceCount, t.supportedCount, t.contradictedCount,
                t.lastEvidenceAt, t.symbol,
            });

            return Results.Ok(items);
        });

        group.MapGet("/theses/{id:guid}", async (MarketLensDbContext db, Guid id, CancellationToken ct) =>
        {
            var thesis = await db.ResearchTheses
                .AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Status,
                    t.ThesisText,
                    t.Summary,
                    t.CreatedAt,
                    t.UpdatedAt,
                    hasEmbedding = t.Embedding != null,
                    plan = t.Plan,
                    planModel = t.PlanModel,
                    planPromptVersion = t.PlanPromptVersion,
                    planGeneratedAt = t.PlanGeneratedAt,
                    assets = t.ThesisAssets.Select(ta => new
                    {
                        ta.AssetId,
                        ta.Role,
                        ta.Asset!.Kind,
                        ta.Asset.Name,
                        ta.Asset.Symbol,
                        keywords = ta.Asset.Keywords,
                    }),
                    rules = t.Rules.OrderBy(r => r.CreatedAt).Select(r => new
                    {
                        r.Id,
                        r.Name,
                        r.IsEnabled,
                        r.AssetKeywords,
                        r.ConceptKeywords,
                        r.EventTypes,
                        r.SourceNames,
                        r.SourceTiers,
                        r.ExcludeTerms,
                        r.MinArticleSimilarity,
                        r.CreatedAt,
                        r.UpdatedAt,
                    }),
                })
                .FirstOrDefaultAsync(ct);

            if (thesis is null) return Results.NotFound();

            return Results.Ok(new
            {
                thesis.Id, thesis.Name, thesis.Status, thesis.ThesisText, thesis.Summary,
                thesis.CreatedAt, thesis.UpdatedAt, thesis.hasEmbedding,
                thesis.plan,
                planAdequacy = PlanAdequacy.From(thesis.plan),
                thesis.planModel, thesis.planPromptVersion, thesis.planGeneratedAt,
                thesis.assets, thesis.rules,
            });
        });

        group.MapPost("/theses/{id:guid}/scan", async (
            MarketLensDbContext db,
            ResearchMatcher matcher,
            Guid id,
            ScanThesisRequest request,
            CancellationToken ct) =>
        {
            var exists = await db.ResearchTheses.AnyAsync(t => t.Id == id, ct);
            if (!exists) return Results.NotFound();

            var lookbackHours = request.LookbackDays.HasValue
                ? Math.Clamp(request.LookbackDays.Value, 1, 3650) * 24
                : request.LookbackHours;

            var result = await matcher.ScanAsync(new ResearchScanRequest(
                ThesisId: id,
                ActiveOnly: false,
                LookbackHours: lookbackHours,
                BatchSize: request.BatchSize), ct);

            return Results.Ok(result);
        });

        group.MapPost("/theses", async (
            MarketLensDbContext db,
            IEmbeddingClient embedder,
            ILoggerFactory loggerFactory,
            ILocalWorkQueue workQueue,
            CreateThesisRequest request,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ThesisText))
                return Results.BadRequest(new { error = "name and thesisText are required" });

            var now = DateTime.UtcNow;
            var thesis = new ResearchThesis
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Status = NormalizeStatus(request.Status),
                ThesisText = request.ThesisText.Trim(),
                Summary = EmptyToNull(request.Summary),
                CreatedAt = now,
                UpdatedAt = now,
                Embedding = await TryEmbedAsync(embedder, request.ThesisText, loggerFactory, ct),
            };

            db.ResearchTheses.Add(thesis);
            var defaultRule = BuildDefaultRule(thesis.Id, request);
            if (defaultRule is not null)
                db.ThesisRules.Add(defaultRule);
            await db.SaveChangesAsync(ct);

            await AutoBindAssetsAsync(db, thesis.Id, $"{request.Name} {request.ThesisText}", ct);

            await EnqueueBootstrapAsync(workQueue, thesis.Id, ct);

            return Results.Created($"/api/research/theses/{thesis.Id}", new
            {
                thesis.Id,
                thesis.Name,
                thesis.Status,
                thesis.ThesisText,
                thesis.Summary,
                thesis.CreatedAt,
                thesis.UpdatedAt,
                hasEmbedding = thesis.Embedding != null,
                ruleCount = defaultRule is null ? 0 : 1,
                planStatus = "pending",
            });
        });

        group.MapPost("/theses/{id:guid}/bootstrap", async (
            MarketLensDbContext db,
            ThesisBootstrapper bootstrapper,
            Guid id,
            CancellationToken ct) =>
        {
            var exists = await db.ResearchTheses.AnyAsync(t => t.Id == id, ct);
            if (!exists) return Results.NotFound();

            var result = await bootstrapper.BootstrapAsync(id, ct);
            return Results.Ok(result);
        });

        group.MapPost("/theses/{id:guid}/promote", async (
            MarketLensDbContext db,
            ILocalWorkQueue workQueue,
            Guid id,
            CancellationToken ct) =>
        {
            var thesis = await db.ResearchTheses.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (thesis is null) return Results.NotFound();
            if (thesis.Status != ThesisStatuses.Exploration)
                return Results.BadRequest(new { error = "only explorations can be promoted" });

            thesis.Status = ThesisStatuses.Active;
            thesis.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await EnqueueBootstrapAsync(workQueue, thesis.Id, ct);

            return Results.Ok(new
            {
                thesis.Id,
                thesis.Status,
                thesis.UpdatedAt,
            });
        });

        group.MapPatch("/theses/{id:guid}", async (
            MarketLensDbContext db,
            IEmbeddingClient embedder,
            ILoggerFactory loggerFactory,
            Guid id,
            UpdateThesisRequest request,
            CancellationToken ct) =>
        {
            var thesis = await db.ResearchTheses.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (thesis is null) return Results.NotFound();

            var changedText = false;
            if (!string.IsNullOrWhiteSpace(request.Name))
                thesis.Name = request.Name.Trim();
            if (request.Status is not null)
                thesis.Status = NormalizeStatus(request.Status);
            if (request.Summary is not null)
                thesis.Summary = EmptyToNull(request.Summary);
            if (!string.IsNullOrWhiteSpace(request.ThesisText) && request.ThesisText.Trim() != thesis.ThesisText)
            {
                thesis.ThesisText = request.ThesisText.Trim();
                changedText = true;
            }

            if (changedText)
                thesis.Embedding = await TryEmbedAsync(embedder, thesis.ThesisText, loggerFactory, ct);

            if (request.PositionIntent is not null)
            {
                var newIntent = NormalizePositionIntent(request.PositionIntent);
                if (newIntent != thesis.PositionIntent)
                {
                    thesis.PositionIntent = newIntent;
                    thesis.PositionUpdatedAt = DateTime.UtcNow;
                }
            }
            if (request.PositionThesis is not null)
                thesis.PositionThesis = EmptyToNull(request.PositionThesis);

            thesis.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                thesis.Id,
                thesis.Name,
                thesis.Status,
                thesis.ThesisText,
                thesis.Summary,
                thesis.CreatedAt,
                thesis.UpdatedAt,
                thesis.PositionIntent,
                thesis.PositionThesis,
                thesis.PositionUpdatedAt,
                hasEmbedding = thesis.Embedding != null,
            });
        });

        group.MapGet("/theses/{id:guid}/snapshots", async (
            MarketLensDbContext db,
            Guid id,
            int? take,
            CancellationToken ct) =>
        {
            var limit = Math.Clamp(take ?? 90, 1, 365);
            var rows = await db.ResearchSnapshots
                .AsNoTracking()
                .Where(s => s.ThesisId == id)
                .OrderByDescending(s => s.SnapshotAt)
                .Take(limit)
                .Select(s => new
                {
                    s.Id, s.SnapshotAt, s.EvidenceCount, s.LatestEvidenceAt, s.Summary,
                })
                .ToListAsync(ct);

            return Results.Ok(rows.Select(r => new
            {
                r.Id, r.SnapshotAt, r.EvidenceCount, r.LatestEvidenceAt,
                summary = SafeParseJson(r.Summary),
            }));
        });
    }

    private static Task EnqueueBootstrapAsync(ILocalWorkQueue workQueue, Guid thesisId, CancellationToken ct) =>
        workQueue.EnqueueAsync(
            new EnqueueWorkRequest(
                WorkType: PipelineWorkTypes.ThesisBootstrap,
                NaturalKey: thesisId.ToString(),
                PayloadJson: $$"""{"thesisId":"{{thesisId}}"}""",
                Priority: 0),
            ct);

    private static object? SafeParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException) { return json; }
    }

    private static string NormalizePositionIntent(string? intent)
    {
        var value = EmptyToNull(intent)?.ToLowerInvariant();
        return value is "none" or "watching_long" or "watching_short" or "open_long" or "open_short" or "closed"
            ? value
            : "none";
    }

    private static string NormalizeStatus(string? status)
    {
        var value = EmptyToNull(status)?.ToLowerInvariant();
        return value is ThesisStatuses.Draft or ThesisStatuses.Active or ThesisStatuses.Paused
            or ThesisStatuses.Archived or ThesisStatuses.Exploration or ThesisStatuses.Watching
            or ThesisStatuses.Validated or ThesisStatuses.Invalidated
            ? value
            : ThesisStatuses.Active;
    }

    private static ThesisRule? BuildDefaultRule(Guid thesisId, CreateThesisRequest request)
    {
        var assetKeywords = NormalizeValues(request.AssetKeywords);
        var conceptKeywords = NormalizeValues(request.ConceptKeywords);
        var eventTypes = NormalizeValues(request.EventTypes);
        var sourceNames = NormalizeValues(request.SourceNames);
        var sourceTiers = NormalizeValues(request.SourceTiers);
        var excludeTerms = NormalizeValues(request.ExcludeTerms);

        if (assetKeywords.Count == 0 &&
            conceptKeywords.Count == 0 &&
            eventTypes.Count == 0 &&
            sourceNames.Count == 0 &&
            sourceTiers.Count == 0 &&
            excludeTerms.Count == 0)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        return new ThesisRule
        {
            Id = Guid.NewGuid(),
            ThesisId = thesisId,
            Name = "Initial subscription",
            IsEnabled = true,
            AssetKeywords = ToJsonArray(assetKeywords),
            ConceptKeywords = ToJsonArray(conceptKeywords),
            EventTypes = ToJsonArray(eventTypes),
            SourceNames = ToJsonArray(sourceNames),
            SourceTiers = ToJsonArray(sourceTiers),
            ExcludeTerms = ToJsonArray(excludeTerms),
            MinArticleSimilarity = request.MinArticleSimilarity,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static IReadOnlyCollection<string> NormalizeValues(IReadOnlyCollection<string>? values) =>
        values?
            .Select(EmptyToNull)
            .Where(v => v is not null)
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
}

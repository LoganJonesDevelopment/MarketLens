using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints.Research;

public static partial class ResearchEndpoints
{
    private static void MapRuleEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/theses/{id:guid}/rules", async (MarketLensDbContext db, Guid id, CancellationToken ct) =>
        {
            var exists = await db.ResearchTheses.AnyAsync(t => t.Id == id, ct);
            if (!exists) return Results.NotFound();

            var rules = await db.ThesisRules
                .AsNoTracking()
                .Where(r => r.ThesisId == id)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(ct);
            return Results.Ok(rules);
        });

        group.MapPost("/theses/{id:guid}/rules", async (
            MarketLensDbContext db,
            Guid id,
            UpsertRuleRequest request,
            CancellationToken ct) =>
        {
            var thesisExists = await db.ResearchTheses.AnyAsync(t => t.Id == id, ct);
            if (!thesisExists) return Results.NotFound();

            var now = DateTime.UtcNow;
            var rule = new ThesisRule
            {
                Id = Guid.NewGuid(),
                ThesisId = id,
                Name = EmptyToNull(request.Name) ?? "Research rule",
                IsEnabled = request.IsEnabled ?? true,
                AssetKeywords = ToJsonArray(request.AssetKeywords),
                ConceptKeywords = ToJsonArray(request.ConceptKeywords),
                EventTypes = ToJsonArray(request.EventTypes),
                SourceNames = ToJsonArray(request.SourceNames),
                SourceTiers = ToJsonArray(request.SourceTiers),
                ExcludeTerms = ToJsonArray(request.ExcludeTerms),
                MinArticleSimilarity = request.MinArticleSimilarity,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.ThesisRules.Add(rule);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/research/theses/{id}/rules/{rule.Id}", rule);
        });

        group.MapPatch("/theses/{id:guid}/rules/{ruleId:guid}", async (
            MarketLensDbContext db,
            Guid id,
            Guid ruleId,
            UpsertRuleRequest request,
            CancellationToken ct) =>
        {
            var rule = await db.ThesisRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.ThesisId == id, ct);
            if (rule is null) return Results.NotFound();

            if (request.Name is not null)
                rule.Name = EmptyToNull(request.Name) ?? rule.Name;
            if (request.IsEnabled.HasValue)
                rule.IsEnabled = request.IsEnabled.Value;
            if (request.AssetKeywords is not null)
                rule.AssetKeywords = ToJsonArray(request.AssetKeywords);
            if (request.ConceptKeywords is not null)
                rule.ConceptKeywords = ToJsonArray(request.ConceptKeywords);
            if (request.EventTypes is not null)
                rule.EventTypes = ToJsonArray(request.EventTypes);
            if (request.SourceNames is not null)
                rule.SourceNames = ToJsonArray(request.SourceNames);
            if (request.SourceTiers is not null)
                rule.SourceTiers = ToJsonArray(request.SourceTiers);
            if (request.ExcludeTerms is not null)
                rule.ExcludeTerms = ToJsonArray(request.ExcludeTerms);
            if (request.MinArticleSimilarity.HasValue)
                rule.MinArticleSimilarity = request.MinArticleSimilarity;

            rule.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(rule);
        });

        group.MapDelete("/theses/{id:guid}/rules/{ruleId:guid}", async (
            MarketLensDbContext db,
            Guid id,
            Guid ruleId,
            CancellationToken ct) =>
        {
            var rule = await db.ThesisRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.ThesisId == id, ct);
            if (rule is null) return Results.NotFound();

            db.ThesisRules.Remove(rule);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}

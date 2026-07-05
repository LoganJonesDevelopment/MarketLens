using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class KillCriteriaEndpoints
{
    public static void MapKillCriteriaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/research/kill-criteria");

        group.MapGet("/", async (
            MarketLensDbContext db,
            Guid? thesisId,
            CancellationToken ct) =>
        {
            var q = db.ThesisKillCriteria.AsNoTracking().AsQueryable();

            if (thesisId.HasValue)
                q = q.Where(k => k.ThesisId == thesisId.Value);

            var items = await q
                .OrderBy(k => k.ThesisId)
                .ThenByDescending(k => k.ThreatLevel == "critical" ? 0 :
                    k.ThreatLevel == "elevated" ? 1 :
                    k.ThreatLevel == "watching" ? 2 : 3)
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        group.MapPost("/", async (
            MarketLensDbContext db,
            ThesisKillCriterionRequest req,
            CancellationToken ct) =>
        {
            var criterion = new ThesisKillCriterion
            {
                ThesisId = req.ThesisId,
                Scenario = req.Scenario,
                MonitoringKeywords = req.MonitoringKeywords,
                ThreatLevel = req.ThreatLevel ?? "dormant",
                CreatedAt = DateTime.UtcNow,
            };

            db.ThesisKillCriteria.Add(criterion);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/research/kill-criteria/{criterion.Id}", criterion);
        });

        group.MapPut("/{id:int}", async (
            MarketLensDbContext db,
            int id,
            ThesisKillCriterionUpdateRequest req,
            CancellationToken ct) =>
        {
            var criterion = await db.ThesisKillCriteria.FindAsync([id], ct);
            if (criterion is null) return Results.NotFound();

            if (req.Scenario is not null) criterion.Scenario = req.Scenario;
            if (req.MonitoringKeywords is not null) criterion.MonitoringKeywords = req.MonitoringKeywords;
            if (req.ThreatLevel is not null) criterion.ThreatLevel = req.ThreatLevel;
            if (req.ContradictingEvidenceCount.HasValue) criterion.ContradictingEvidenceCount = req.ContradictingEvidenceCount.Value;
            if (req.LastTriggeredReason is not null)
            {
                criterion.LastTriggeredReason = req.LastTriggeredReason;
                criterion.LastEscalatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(criterion);
        });

        group.MapDelete("/{id:int}", async (
            MarketLensDbContext db,
            int id,
            CancellationToken ct) =>
        {
            var criterion = await db.ThesisKillCriteria.FindAsync([id], ct);
            if (criterion is null) return Results.NotFound();

            db.ThesisKillCriteria.Remove(criterion);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}

public record ThesisKillCriterionRequest(
    Guid ThesisId,
    string Scenario,
    string MonitoringKeywords,
    string? ThreatLevel);

public record ThesisKillCriterionUpdateRequest(
    string? Scenario,
    string? MonitoringKeywords,
    string? ThreatLevel,
    int? ContradictingEvidenceCount,
    string? LastTriggeredReason);

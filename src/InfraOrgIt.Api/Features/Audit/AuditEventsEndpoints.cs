using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Audit;

public static class AuditEventsEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/audit-events", async (
            HttpContext http,
            ClaimsPrincipal user,
            InfraOrgItDbContext db,
            CancellationToken ct) =>
        {
            EnvironmentAccessService.RequireSystemAdmin(user);
            var page = PageRequest.From(http);
            var kind = http.Request.Query["kind"].ToString().Trim();
            var outcome = http.Request.Query["outcome"].ToString().Trim();
            var serverId = PageRequest.IntQuery(http, "serverId");

            var query = db.AuditEvents.AsNoTracking();
            if (kind.Length != 0)
                query = query.Where(x => x.Kind == kind);
            if (outcome.Length != 0)
                query = query.Where(x => x.Outcome == outcome);
            if (serverId is not null)
                query = query.Where(x => x.ServerId == serverId);
            if (page.Q is not null)
                query = query.Where(x => x.ActorUsername.Contains(page.Q) ||
                                         x.Action.Contains(page.Q) ||
                                         (x.ClientId != null && x.ClientId.Contains(page.Q)));

            query = query
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.Id);

            var total = await query.CountAsync(ct);
            var items = await query
                .Skip((page.Page - 1) * page.PageSize)
                .Take(page.PageSize)
                .Select(x => new AuditEventDto(
                    x.Id,
                    x.OccurredAt,
                    x.Kind,
                    x.ActorUsername,
                    x.ClientId,
                    x.Action,
                    x.TargetType,
                    x.TargetId,
                    x.ServerId,
                    x.TargetHost,
                    x.TargetPort,
                    x.LocalPort,
                    x.Outcome,
                    x.ErrorCode,
                    x.DurationMs,
                    x.CorrelationId))
                .ToListAsync(ct);

            return Results.Ok(new PagedResult<AuditEventDto>(
                items, total, page.Page, page.PageSize));
        }).RequireAuthorization();

        app.MapGet("/api/audit-events/verify", VerifyAsync)
            .Produces<AuditVerifyResult>()
            // Keep the generated OpenAPI tag stable with the existing operation.
            .WithTags("InfraOrgIt.Api")
            .RequireAuthorization();
    }

    /// <summary>
    /// Recomputes the hash chain from the oldest remaining hashed row. Legacy
    /// rows with a null hash are skipped. The first hashed row is accepted as
    /// the chain start because retention may have deleted earlier rows.
    /// </summary>
    private static async Task<IResult> VerifyAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);

        var checkedRows = 0;
        var legacyRows = 0;
        var firstHashedRow = true;
        string? previousHash = null;

        var rows = db.AuditEvents.AsNoTracking()
            .OrderBy(x => x.Id)
            .AsAsyncEnumerable();

        await foreach (var row in rows.WithCancellation(ct))
        {
            checkedRows++;
            if (row.Hash is null)
            {
                legacyRows++;
                continue;
            }

            if (row.PrevHash is null)
                return Results.Ok(new AuditVerifyResult(
                    false, checkedRows, row.Id,
                    $"PrevHash mancante alla riga {row.Id}."));

            if (!firstHashedRow && row.PrevHash != previousHash)
                return Results.Ok(new AuditVerifyResult(
                    false, checkedRows, row.Id,
                    $"Catena interrotta alla riga {row.Id}."));

            if (!string.Equals(
                    AuditChain.Compute(row, row.PrevHash),
                    row.Hash,
                    StringComparison.Ordinal))
                return Results.Ok(new AuditVerifyResult(
                    false, checkedRows, row.Id,
                    $"Hash non valido alla riga {row.Id}."));

            previousHash = row.Hash;
            firstHashedRow = false;
        }

        return Results.Ok(new AuditVerifyResult(
            true,
            checkedRows,
            null,
            legacyRows > 0
                ? $"Righe legacy senza hash ignorate: {legacyRows}."
                : checkedRows == 0 ? "Nessuna riga di audit." : null));
    }
}

public sealed record AuditVerifyResult(
    bool Valid,
    int Checked,
    int? FirstBrokenId,
    string? Message);

public sealed record AuditEventDto(
    int Id,
    DateTime OccurredAt,
    string Kind,
    string ActorUsername,
    string? ClientId,
    string Action,
    string? TargetType,
    int? TargetId,
    int? ServerId,
    string? TargetHost,
    int? TargetPort,
    int? LocalPort,
    string Outcome,
    string? ErrorCode,
    int DurationMs,
    string? CorrelationId);

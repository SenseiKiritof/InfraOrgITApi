using System.Security.Claims;
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
    }
}

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

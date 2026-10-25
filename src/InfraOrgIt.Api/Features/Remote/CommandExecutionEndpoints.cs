using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Remote;

public static class CommandExecutionEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/command-executions", async (
            HttpContext http,
            ClaimsPrincipal user,
            InfraOrgItDbContext db,
            CancellationToken ct) =>
        {
            EnvironmentAccessService.RequireSystemAdmin(user);
            var page = PageRequest.From(http);
            var serverId = PageRequest.IntQuery(http, "serverId");
            var kind = http.Request.Query["kind"].ToString();

            var query = db.CommandExecutions.AsNoTracking();
            if (serverId is not null)
                query = query.Where(x => x.ServerId == serverId);
            if (!string.IsNullOrWhiteSpace(kind))
                query = query.Where(x => x.Kind == kind);
            if (page.Q is not null)
                query = query.Where(x => x.Username.Contains(page.Q) ||
                                         x.ServerName.Contains(page.Q) ||
                                         x.Label.Contains(page.Q));

            query = page.Sort switch
            {
                "started" => query.OrderBy(x => x.StartedAt),
                "-started" => query.OrderByDescending(x => x.StartedAt),
                _ => query.OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id)
            };

            var total = await query.CountAsync(ct);
            var items = await query
                .Skip((page.Page - 1) * page.PageSize)
                .Take(page.PageSize)
                .Select(x => new CommandExecutionDto(
                    x.Id, x.Username, x.ServerId, x.ServerName, x.Kind, x.Label,
                    x.Script, x.ExitCode, x.Output, x.StartedAt, x.EndedAt, x.IdempotencyKey))
                .ToListAsync(ct);

            return Results.Ok(new PagedResult<CommandExecutionDto>(
                items, total, page.Page, page.PageSize));
        }).RequireAuthorization();
    }
}

public sealed record CommandExecutionDto(
    int Id,
    string Username,
    int ServerId,
    string ServerName,
    string Kind,
    string Label,
    string Script,
    int? ExitCode,
    string Output,
    DateTime StartedAt,
    DateTime? EndedAt,
    string? IdempotencyKey);

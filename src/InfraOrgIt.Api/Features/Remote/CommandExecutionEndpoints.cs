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
                                         x.Kind.Contains(page.Q) ||
                                         x.Label.Contains(page.Q));

            query = page.Sort switch
            {
                "startedAt" => query.OrderBy(x => x.StartedAt).ThenBy(x => x.Id),
                "-startedAt" => query.OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id),
                "username" => query.OrderBy(x => x.Username).ThenBy(x => x.Id),
                "-username" => query.OrderByDescending(x => x.Username).ThenByDescending(x => x.Id),
                "serverName" => query.OrderBy(x => x.ServerName).ThenBy(x => x.Id),
                "-serverName" => query.OrderByDescending(x => x.ServerName).ThenByDescending(x => x.Id),
                "kind" => query.OrderBy(x => x.Kind).ThenBy(x => x.Id),
                "-kind" => query.OrderByDescending(x => x.Kind).ThenByDescending(x => x.Id),
                "label" => query.OrderBy(x => x.Label).ThenBy(x => x.Id),
                "-label" => query.OrderByDescending(x => x.Label).ThenByDescending(x => x.Id),
                "exitCode" => query.OrderBy(x => x.ExitCode).ThenBy(x => x.Id),
                "-exitCode" => query.OrderByDescending(x => x.ExitCode).ThenByDescending(x => x.Id),
                _ => query.OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id)
            };

            var total = await query.CountAsync(ct);
            var items = await query
                .Skip((page.Page - 1) * page.PageSize)
                .Take(page.PageSize)
                .Select(x => new CommandExecutionDto(
                    x.Id, x.Username, x.ServerId, x.ServerName, x.Kind, x.Label,
                    x.ExitCode, x.Output, x.StartedAt, x.EndedAt))
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
    int? ExitCode,
    string Output,
    DateTime StartedAt,
    DateTime? EndedAt);

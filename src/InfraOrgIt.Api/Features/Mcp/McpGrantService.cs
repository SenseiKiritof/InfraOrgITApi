using System.Security.Claims;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Mcp;

/// <summary>
/// Extra permission for MCP action tools. A grant enables a tool for a user or
/// a group and, optionally, only for one environment definition; the normal
/// environment RBAC is still required.
/// </summary>
public sealed class McpGrantService(InfraOrgItDbContext db)
{
    public static readonly string[] Actions = ["reachability.run", "actions.run", "*"];

    public async Task<bool> HasAsync(
        ClaimsPrincipal user,
        string action,
        int providerEnvironmentId,
        CancellationToken ct)
    {
        var username = user.Identity?.Name ?? "";
        var userId = await db.Users
            .Where(x => x.Username == username)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        if (userId is null)
            return false;

        var environmentDefinitionId = await db.ProviderEnvironments
            .Where(x => x.Id == providerEnvironmentId)
            .Select(x => (int?)x.EnvironmentDefinitionId)
            .SingleOrDefaultAsync(ct);
        if (environmentDefinitionId is null)
            return false;

        var groupIds = db.GroupUsers
            .Where(x => x.UsersId == userId)
            .Select(x => x.GroupsId);

        return await db.McpGrants.AnyAsync(x =>
            (x.UserId == userId ||
             (x.GroupId != null && groupIds.Contains(x.GroupId.Value))) &&
            (x.Action == action || x.Action == "*") &&
            (x.EnvironmentDefinitionId == null ||
             x.EnvironmentDefinitionId == environmentDefinitionId), ct);
    }
}

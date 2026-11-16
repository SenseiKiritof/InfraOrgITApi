using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Mcp;

/// <summary>
/// One MCP client (API token or OAuth client) is approved once by its owner
/// with individual action permissions. Action tools then run without a
/// per-request human approval; the normal environment RBAC still applies.
/// </summary>
public sealed class McpAgentService(InfraOrgItDbContext db)
{
    public static readonly string[] ActionPermissions =
        ["reachability.run", "actions.run", "*"];

    public static readonly string[] SelectableActions =
        ["reachability.run", "actions.run"];

    public Task<int?> UserIdAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var username = user.Identity?.Name ?? "";
        return db.Users
            .Where(x => x.Username == username)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>
    /// Resolves the current client and creates a Pending agent on first use.
    /// The client key is the API token id or the OAuth client id, so two
    /// clients of the same user get separate approvals.
    /// </summary>
    public async Task<McpAgent> ResolveAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = await UserIdAsync(user, ct)
                     ?? throw Guard.Forbidden("Utente non provisionato.");
        var (clientKey, name) = await ClientIdentityAsync(user, ct);

        var agent = await db.McpAgents
            .SingleOrDefaultAsync(
                x => x.UserId == userId && x.ClientKey == clientKey, ct);
        if (agent is null)
        {
            agent = new McpAgent
            {
                UserId = userId,
                ClientKey = clientKey,
                Name = name,
                Status = McpAgentStatuses.Pending,
                LastSeenAt = DateTime.UtcNow
            };
            db.McpAgents.Add(agent);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent first call created the same agent; reload it.
                db.Entry(agent).State = EntityState.Detached;
                agent = await db.McpAgents.SingleAsync(
                    x => x.UserId == userId && x.ClientKey == clientKey, ct);
            }
        }
        else if (agent.LastSeenAt is null ||
                 agent.LastSeenAt < DateTime.UtcNow.AddMinutes(-5))
        {
            // Bypass the concurrency token: concurrent tool calls must not
            // fail just to refresh the last-seen timestamp.
            await db.McpAgents
                .Where(x => x.Id == agent.Id)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.LastSeenAt, DateTime.UtcNow), ct);
        }

        return agent;
    }

    private async Task<(string Key, string Name)> ClientIdentityAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var tokenId = user.FindFirstValue("api_token_id");
        if (int.TryParse(tokenId, out var id))
        {
            var name = await db.ApiTokens
                .Where(x => x.Id == id)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(ct);
            return ($"token:{id}", name ?? $"API token #{id}");
        }

        var clientId = user.FindFirstValue("client_id")
                       ?? user.FindFirstValue("azp");
        if (!string.IsNullOrWhiteSpace(clientId))
            return ($"oauth:{clientId}", clientId);

        return ("mcp", "MCP client");
    }

    public Task<bool> HasPermissionAsync(
        int agentId,
        string action,
        int environmentDefinitionId,
        CancellationToken ct) =>
        db.McpAgentPermissions.AnyAsync(x =>
            x.AgentId == agentId &&
            (x.Action == action || x.Action == "*") &&
            (x.EnvironmentDefinitionId == null ||
             x.EnvironmentDefinitionId == environmentDefinitionId), ct);

    /// <summary>
    /// Creates one Pending permission request per agent, action and
    /// environment. A duplicate call returns the existing pending row.
    /// </summary>
    public async Task<McpPermissionRequest> RequestPermissionAsync(
        int agentId,
        string action,
        int? environmentDefinitionId,
        string? reason,
        CancellationToken ct)
    {
        if (!SelectableActions.Contains(action))
            throw Guard.Unprocessable(
                $"Azione ammessa: {string.Join(", ", SelectableActions)}.");
        if (environmentDefinitionId is int environmentId &&
            !await db.EnvironmentDefinitions.AnyAsync(
                x => x.Id == environmentId, ct))
            throw Guard.NotFound("Ambiente", environmentId);

        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? null
            : reason.Trim()[..Math.Min(reason.Trim().Length, 500)];

        var existing = await db.McpPermissionRequests
            .Where(x => x.AgentId == agentId &&
                        x.Status == McpPermissionRequestStatuses.Pending &&
                        x.Action == action &&
                        x.EnvironmentDefinitionId == environmentDefinitionId)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return existing;

        var request = new McpPermissionRequest
        {
            AgentId = agentId,
            Action = action,
            EnvironmentDefinitionId = environmentDefinitionId,
            Reason = normalizedReason,
            Status = McpPermissionRequestStatuses.Pending
        };
        db.McpPermissionRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return request;
    }

    /// <summary>Replaces the whole permission set of an agent.</summary>
    public async Task ReplacePermissionsAsync(
        int agentId,
        IReadOnlyCollection<McpAgentPermissionInput> permissions,
        CancellationToken ct)
    {
        var seen = new HashSet<(string, int?)>();
        foreach (var permission in permissions)
        {
            if (!ActionPermissions.Contains(permission.Action))
                throw Guard.Unprocessable(
                    $"Azione ammessa: {string.Join(", ", ActionPermissions)}.");
            if (permission.EnvironmentDefinitionId is int environmentId &&
                !await db.EnvironmentDefinitions.AnyAsync(
                    x => x.Id == environmentId, ct))
                throw Guard.NotFound("Ambiente", environmentId);
            if (!seen.Add((permission.Action, permission.EnvironmentDefinitionId)))
                throw Guard.Unprocessable("Permesso duplicato.");
        }

        var existing = await db.McpAgentPermissions
            .Where(x => x.AgentId == agentId)
            .ToListAsync(ct);
        db.McpAgentPermissions.RemoveRange(existing);
        db.McpAgentPermissions.AddRange(permissions.Select(permission =>
            new McpAgentPermission
            {
                AgentId = agentId,
                Action = permission.Action,
                EnvironmentDefinitionId = permission.EnvironmentDefinitionId
            }));
        await db.SaveChangesAsync(ct);
    }

    public async Task AddPermissionIfMissingAsync(
        int agentId,
        string action,
        int? environmentDefinitionId,
        CancellationToken ct)
    {
        var exists = await db.McpAgentPermissions.AnyAsync(x =>
            x.AgentId == agentId &&
            x.Action == action &&
            x.EnvironmentDefinitionId == environmentDefinitionId, ct);
        if (exists)
            return;

        db.McpAgentPermissions.Add(new McpAgentPermission
        {
            AgentId = agentId,
            Action = action,
            EnvironmentDefinitionId = environmentDefinitionId
        });
        await db.SaveChangesAsync(ct);
    }
}

public sealed record McpAgentPermissionInput(
    string Action,
    int? EnvironmentDefinitionId = null);

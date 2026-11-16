using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Mcp;

/// <summary>
/// Cookie-only management of MCP agents. The owner approves each client once
/// with individual permissions; SystemAdmin can see and edit every agent.
/// Bearer tokens cannot decide their own permissions.
/// </summary>
public static class McpAgentsEndpoints
{
    public static void Map(WebApplication app)
    {
        var agents = app.MapGroup("/api/mcp/agents")
            .RequireAuthorization("Session");
        agents.MapGet("", ListAsync);
        agents.MapPost("/{id:int}/approve", ApproveAsync);
        agents.MapPost("/{id:int}/deny", DenyAsync);
        agents.MapPut("/{id:int}/permissions", UpdatePermissionsAsync);

        var requests = app.MapGroup("/api/mcp/permission-requests")
            .RequireAuthorization("Session");
        requests.MapGet("", ListRequestsAsync);
        requests.MapPost("/{id:int}/approve", ApproveRequestAsync);
        requests.MapPost("/{id:int}/deny", DenyRequestAsync);
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        McpAgentService agents,
        EnvironmentAccessService access,
        CancellationToken ct)
    {
        var userId = await RequireUserIdAsync(user, agents, ct);
        var isAdmin = await access.IsSystemAdminAsync(user, ct);

        var query = db.McpAgents.AsNoTracking();
        if (!isAdmin)
            query = query.Where(x => x.UserId == userId);

        var items = await query
            .OrderBy(x => x.Status)
            .ThenBy(x => x.Id)
            .Select(x => new McpAgentDto(
                x.Id,
                x.UserId,
                x.User!.Username,
                x.ClientKey,
                x.Name,
                x.Status,
                x.CreatedAt,
                x.ApprovedAt,
                x.ApprovedByUser != null ? x.ApprovedByUser.Username : null,
                x.LastSeenAt,
                x.Permissions
                    .OrderBy(p => p.Action)
                    .ThenBy(p => p.EnvironmentDefinitionId)
                    .Select(p => new McpAgentPermissionDto(
                        p.Id,
                        p.Action,
                        p.EnvironmentDefinitionId,
                        p.EnvironmentDefinition != null
                            ? p.EnvironmentDefinition.Name
                            : null))
                    .ToList(),
                x.PermissionRequests.Count(r =>
                    r.Status == McpPermissionRequestStatuses.Pending)))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> ApproveAsync(
        int id,
        McpAgentDecisionInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        McpAgentService agents,
        EnvironmentAccessService access,
        IAuditService audit,
        CancellationToken ct)
    {
        var agent = await LoadManagedAgentAsync(
            id, user, db, agents, access, ct);
        await agents.ReplacePermissionsAsync(
            agent.Id, input.Permissions ?? [], ct);

        var now = DateTime.UtcNow;
        agent.Status = McpAgentStatuses.Approved;
        agent.ApprovedAt = now;
        agent.ApprovedByUserId = await agents.UserIdAsync(user, ct);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditEntries.Change(
            user,
            "mcp_agent_approve",
            "McpAgent",
            agent.Id,
            detail: new
            {
                agent.Name,
                agent.ClientKey,
                permissions = input.Permissions
            }), ct);
        return Results.Ok(await AgentDtoAsync(db, agent.Id, ct));
    }

    private static async Task<IResult> DenyAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        McpAgentService agents,
        EnvironmentAccessService access,
        IAuditService audit,
        CancellationToken ct)
    {
        var agent = await LoadManagedAgentAsync(
            id, user, db, agents, access, ct);
        agent.Status = McpAgentStatuses.Denied;
        agent.ApprovedAt = null;
        agent.ApprovedByUserId = null;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditEntries.Change(
            user,
            "mcp_agent_deny",
            "McpAgent",
            agent.Id,
            detail: new { agent.Name, agent.ClientKey }), ct);
        return Results.Ok(await AgentDtoAsync(db, agent.Id, ct));
    }

    private static async Task<IResult> UpdatePermissionsAsync(
        int id,
        McpAgentDecisionInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        McpAgentService agents,
        EnvironmentAccessService access,
        IAuditService audit,
        CancellationToken ct)
    {
        var agent = await LoadManagedAgentAsync(
            id, user, db, agents, access, ct);
        await agents.ReplacePermissionsAsync(
            agent.Id, input.Permissions ?? [], ct);

        await audit.RecordAsync(AuditEntries.Change(
            user,
            "mcp_agent_permissions",
            "McpAgent",
            agent.Id,
            detail: new { permissions = input.Permissions }), ct);
        return Results.Ok(await AgentDtoAsync(db, agent.Id, ct));
    }

    private static async Task<IResult> ListRequestsAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        McpAgentService agents,
        EnvironmentAccessService access,
        CancellationToken ct)
    {
        var userId = await RequireUserIdAsync(user, agents, ct);
        var isAdmin = await access.IsSystemAdminAsync(user, ct);

        var query = db.McpPermissionRequests.AsNoTracking();
        if (!isAdmin)
            query = query.Where(x => x.Agent!.UserId == userId);

        var items = await query
            .OrderBy(x => x.Status)
            .ThenByDescending(x => x.Id)
            .Select(x => new McpPermissionRequestDto(
                x.Id,
                x.AgentId,
                x.Agent!.Name,
                x.Agent.User!.Username,
                x.Action,
                x.EnvironmentDefinitionId,
                x.EnvironmentDefinition != null
                    ? x.EnvironmentDefinition.Name
                    : null,
                x.Reason,
                x.Status,
                x.CreatedAt,
                x.DecidedAt,
                x.DecidedByUser != null ? x.DecidedByUser.Username : null))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> ApproveRequestAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        McpAgentService agents,
        EnvironmentAccessService access,
        IAuditService audit,
        CancellationToken ct)
    {
        var (request, userId) = await LoadManagedRequestAsync(
            id, user, db, agents, access, ct);
        var now = DateTime.UtcNow;
        var updated = await db.McpPermissionRequests
            .Where(x => x.Id == id &&
                        x.Status == McpPermissionRequestStatuses.Pending)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status,
                        McpPermissionRequestStatuses.Approved)
                    .SetProperty(x => x.DecidedAt, now)
                    .SetProperty(x => x.DecidedByUserId, userId),
                ct);
        if (updated == 0)
            throw Guard.Conflict("Richiesta non più decidibile.");

        await agents.AddPermissionIfMissingAsync(
            request.AgentId, request.Action,
            request.EnvironmentDefinitionId, ct);
        await audit.RecordAsync(AuditEntries.Change(
            user,
            "mcp_permission_approve",
            "McpPermissionRequest",
            request.Id,
            detail: new { request.AgentId, request.Action }), ct);
        return Results.Ok(await RequestDtoAsync(db, id, ct));
    }

    private static async Task<IResult> DenyRequestAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        McpAgentService agents,
        EnvironmentAccessService access,
        IAuditService audit,
        CancellationToken ct)
    {
        var (request, userId) = await LoadManagedRequestAsync(
            id, user, db, agents, access, ct);
        var now = DateTime.UtcNow;
        var updated = await db.McpPermissionRequests
            .Where(x => x.Id == id &&
                        x.Status == McpPermissionRequestStatuses.Pending)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status,
                        McpPermissionRequestStatuses.Denied)
                    .SetProperty(x => x.DecidedAt, now)
                    .SetProperty(x => x.DecidedByUserId, userId),
                ct);
        if (updated == 0)
            throw Guard.Conflict("Richiesta non più decidibile.");

        await audit.RecordAsync(AuditEntries.Change(
            user,
            "mcp_permission_deny",
            "McpPermissionRequest",
            request.Id,
            detail: new { request.AgentId, request.Action }), ct);
        return Results.Ok(await RequestDtoAsync(db, id, ct));
    }

    private static async Task<McpAgent> LoadManagedAgentAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        McpAgentService agents,
        EnvironmentAccessService access,
        CancellationToken ct)
    {
        var agent = await db.McpAgents.SingleOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw Guard.NotFound("Agente MCP", id);
        var userId = await RequireUserIdAsync(user, agents, ct);
        if (agent.UserId != userId &&
            !await access.IsSystemAdminAsync(user, ct))
            throw Guard.NotFound("Agente MCP", id);
        return agent;
    }

    private static async Task<(McpPermissionRequest Request, int UserId)>
        LoadManagedRequestAsync(
            int id,
            ClaimsPrincipal user,
            InfraOrgItDbContext db,
            McpAgentService agents,
            EnvironmentAccessService access,
            CancellationToken ct)
    {
        var request = await db.McpPermissionRequests
                          .Include(x => x.Agent)
                          .SingleOrDefaultAsync(x => x.Id == id, ct)
                      ?? throw Guard.NotFound("Richiesta permesso MCP", id);
        var userId = await RequireUserIdAsync(user, agents, ct);
        if (request.Agent!.UserId != userId &&
            !await access.IsSystemAdminAsync(user, ct))
            throw Guard.NotFound("Richiesta permesso MCP", id);
        return (request, userId);
    }

    private static async Task<int> RequireUserIdAsync(
        ClaimsPrincipal user,
        McpAgentService agents,
        CancellationToken ct) =>
        await agents.UserIdAsync(user, ct)
        ?? throw Guard.Forbidden("Utente non provisionato.");

    private static async Task<McpAgentDto> AgentDtoAsync(
        InfraOrgItDbContext db,
        int id,
        CancellationToken ct) =>
        await db.McpAgents.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new McpAgentDto(
                x.Id,
                x.UserId,
                x.User!.Username,
                x.ClientKey,
                x.Name,
                x.Status,
                x.CreatedAt,
                x.ApprovedAt,
                x.ApprovedByUser != null ? x.ApprovedByUser.Username : null,
                x.LastSeenAt,
                x.Permissions
                    .OrderBy(p => p.Action)
                    .ThenBy(p => p.EnvironmentDefinitionId)
                    .Select(p => new McpAgentPermissionDto(
                        p.Id,
                        p.Action,
                        p.EnvironmentDefinitionId,
                        p.EnvironmentDefinition != null
                            ? p.EnvironmentDefinition.Name
                            : null))
                    .ToList(),
                x.PermissionRequests.Count(r =>
                    r.Status == McpPermissionRequestStatuses.Pending)))
            .SingleAsync(ct);

    private static async Task<McpPermissionRequestDto> RequestDtoAsync(
        InfraOrgItDbContext db,
        int id,
        CancellationToken ct) =>
        await db.McpPermissionRequests.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new McpPermissionRequestDto(
                x.Id,
                x.AgentId,
                x.Agent!.Name,
                x.Agent.User!.Username,
                x.Action,
                x.EnvironmentDefinitionId,
                x.EnvironmentDefinition != null
                    ? x.EnvironmentDefinition.Name
                    : null,
                x.Reason,
                x.Status,
                x.CreatedAt,
                x.DecidedAt,
                x.DecidedByUser != null ? x.DecidedByUser.Username : null))
            .SingleAsync(ct);
}

public sealed record McpAgentDecisionInput(
    IReadOnlyCollection<McpAgentPermissionInput>? Permissions = null);

public sealed record McpAgentPermissionDto(
    int Id,
    string Action,
    int? EnvironmentDefinitionId,
    string? EnvironmentName);

public sealed record McpAgentDto(
    int Id,
    int UserId,
    string Username,
    string ClientKey,
    string Name,
    string Status,
    DateTime CreatedAt,
    DateTime? ApprovedAt,
    string? ApprovedByUsername,
    DateTime? LastSeenAt,
    List<McpAgentPermissionDto> Permissions,
    int PendingRequests);

public sealed record McpPermissionRequestDto(
    int Id,
    int AgentId,
    string AgentName,
    string Username,
    string Action,
    int? EnvironmentDefinitionId,
    string? EnvironmentName,
    string? Reason,
    string Status,
    DateTime RequestedAt,
    DateTime? DecidedAt,
    string? DecidedByUsername);

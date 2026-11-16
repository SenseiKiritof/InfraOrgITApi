using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace InfraOrgIt.Api.Features.Mcp;

/// <summary>
/// Read-only status and permission requests for the current MCP agent. A tool
/// call never grants permissions: only the owner can approve them from the
/// cookie-authenticated web UI.
/// </summary>
[McpServerToolType]
public static class AgentTools
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "get_mcp_agent")]
    [Description("Return the current MCP agent status, approved permissions and pending permission requests.")]
    public static async Task<string> GetMcpAgent(
        InfraOrgItDbContext db,
        IAuditService audit,
        McpAgentService agents,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var agent = await agents.ResolveAsync(user, ct);
        var permissions = await db.McpAgentPermissions.AsNoTracking()
            .Where(x => x.AgentId == agent.Id)
            .OrderBy(x => x.Action)
            .ThenBy(x => x.EnvironmentDefinitionId)
            .Select(x => new
            {
                x.Id,
                x.Action,
                x.EnvironmentDefinitionId,
                environmentName = x.EnvironmentDefinition != null
                    ? x.EnvironmentDefinition.Name
                    : null
            })
            .ToListAsync(ct);
        var requests = await db.McpPermissionRequests.AsNoTracking()
            .Where(x => x.AgentId == agent.Id &&
                        x.Status == McpPermissionRequestStatuses.Pending)
            .OrderByDescending(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.Action,
                x.EnvironmentDefinitionId,
                x.Reason,
                x.CreatedAt
            })
            .ToListAsync(ct);

        await McpToolAudit.OkAsync(
            audit, user, "get_mcp_agent",
            new { agentId = agent.Id, agent.Status }, ct,
            targetType: "McpAgent", targetId: agent.Id);
        return JsonSerializer.Serialize(new
        {
            id = agent.Id,
            agent.Name,
            agent.ClientKey,
            agent.Status,
            agent.CreatedAt,
            agent.ApprovedAt,
            permissions,
            pendingRequests = requests
        }, JsonOptions);
    }

    [McpServerTool(Name = "request_mcp_permission")]
    [Description("Request one action permission for the current MCP agent. It creates a Pending request; the owner approves or denies it from the web UI.")]
    public static async Task<string> RequestMcpPermission(
        IAuditService audit,
        McpAgentService agents,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Action: reachability.run or actions.run")] string action,
        [Description("Optional environment definition id; omit for every environment")] int? environmentDefinitionId = null,
        [Description("Optional short reason shown to the owner")] string? reason = null)
    {
        if (!McpToolAudit.HasScope(user, "mcp:operate"))
        {
            await McpToolAudit.RecordAsync(
                audit, user, "request_mcp_permission", "Denied",
                new { action, environmentDefinitionId }, "missing_scope", ct);
            throw new McpException("Serve lo scope mcp:operate.");
        }

        var agent = await agents.ResolveAsync(user, ct);
        var request = await agents.RequestPermissionAsync(
            agent.Id, action, environmentDefinitionId, reason, ct);

        await audit.RecordAsync(AuditEntries.Change(
            user,
            "mcp_permission_request",
            "McpPermissionRequest",
            request.Id,
            detail: new
            {
                request.AgentId,
                request.Action,
                request.EnvironmentDefinitionId,
                request.Reason
            }), ct);
        await McpToolAudit.OkAsync(
            audit, user, "request_mcp_permission",
            new { requestId = request.Id, request.Action }, ct,
            targetType: "McpAgent", targetId: agent.Id);

        return JsonSerializer.Serialize(new
        {
            status = request.Status,
            requestId = request.Id,
            agentId = agent.Id,
            request.Action,
            request.EnvironmentDefinitionId,
            message = request.Status == McpPermissionRequestStatuses.Pending
                ? "Richiesta inviata: il proprietario deve approvarla dalla UI web."
                : $"Richiesta già {request.Status}."
        }, JsonOptions);
    }
}

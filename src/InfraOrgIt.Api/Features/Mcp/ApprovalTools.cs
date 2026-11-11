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

/// <summary>Read-only access to the caller's human approvals (PT-18).</summary>
[McpServerToolType]
public static class ApprovalTools
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "get_mcp_approval")]
    [Description("Return status, expiry and target of one of your MCP approvals. It never executes the approved action.")]
    public static async Task<string> GetMcpApproval(
        InfraOrgItDbContext db,
        IAuditService audit,
        McpApprovalService approvals,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Approval id returned by an action tool")] int approvalId)
    {
        var userId = await approvals.UserIdAsync(user, ct);
        var approval = userId is null
            ? null
            : await approvals.FindAsync(userId.Value, approvalId, ct);
        if (approval is null)
        {
            await McpToolAudit.RecordAsync(
                audit, user, "get_mcp_approval", "Error",
                new { approvalId }, "not_found", ct);
            throw new McpException(
                $"Approvazione {approvalId} non trovata per l'utente corrente.");
        }

        var status = approval.Status;
        var now = DateTime.UtcNow;
        if (approval.ExpiresAt <= now &&
            status is McpApprovalStatuses.Pending or McpApprovalStatuses.Approved)
        {
            await db.McpApprovals
                .Where(x => x.Id == approval.Id &&
                            x.UserId == userId &&
                            x.Status == status)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.Status, McpApprovalStatuses.Expired),
                    ct);
            status = McpApprovalStatuses.Expired;
        }

        await McpToolAudit.OkAsync(
            audit, user, "get_mcp_approval",
            new { approvalId, status }, ct, approval.ServerId);
        return JsonSerializer.Serialize(new
        {
            id = approval.Id,
            action = approval.Action,
            serverId = approval.ServerId,
            serverApplicationId = approval.ServerApplicationId,
            applicationActionId = approval.ApplicationActionId,
            status,
            requestedAt = approval.RequestedAt,
            expiresAt = approval.ExpiresAt,
            decidedAt = approval.DecidedAt,
            usedAt = approval.UsedAt,
            parametersJson = approval.ParametersJson
        }, JsonOptions);
    }
}

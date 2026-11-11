using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Mcp;

/// <summary>
/// Human approval channel for MCP action tools (PT-18). Only a cookie session
/// can list and decide approvals: the "Session" policy excludes MCP bearer
/// tokens, and the CSRF middleware protects the two mutations.
/// </summary>
public static class McpApprovalsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/mcp/approvals")
            .RequireAuthorization("Session");

        group.MapGet("", ListAsync);
        group.MapPost("/{id:int}/approve", ApproveAsync);
        group.MapPost("/{id:int}/deny", DenyAsync);
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        McpApprovalService approvals,
        CancellationToken ct,
        string? status = null)
    {
        var userId = await RequireUserIdAsync(user, approvals, ct);
        await approvals.ExpireStaleAsync(userId, DateTime.UtcNow, ct);

        var filter = status?.Trim() ?? "";
        var query = db.McpApprovals.AsNoTracking()
            .Where(x => x.UserId == userId);
        if (filter.Length != 0)
        {
            var normalized = McpApprovalStatuses.All.FirstOrDefault(x =>
                    string.Equals(x, filter, StringComparison.OrdinalIgnoreCase))
                ?? throw Guard.Unprocessable(
                    $"Status ammesso: {string.Join(", ", McpApprovalStatuses.All)}.");
            query = query.Where(x => x.Status == normalized);
        }

        var items = await query
            .OrderByDescending(x => x.Id)
            .Select(x => new McpApprovalDto(
                x.Id,
                x.Action,
                x.ServerId,
                x.Server != null ? x.Server.Name : null,
                x.ServerApplicationId,
                x.ApplicationActionId,
                x.ParametersJson,
                x.Status,
                x.RequestedAt,
                x.ExpiresAt,
                x.DecidedAt,
                x.DecidedByUser != null ? x.DecidedByUser.Username : null,
                x.UsedAt))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static Task<IResult> ApproveAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        McpApprovalService approvals,
        IAuditService audit,
        CancellationToken ct) =>
        DecideAsync(id, McpApprovalStatuses.Approved, "mcp_approval_approve",
            user, db, approvals, audit, ct);

    private static Task<IResult> DenyAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        McpApprovalService approvals,
        IAuditService audit,
        CancellationToken ct) =>
        DecideAsync(id, McpApprovalStatuses.Denied, "mcp_approval_deny",
            user, db, approvals, audit, ct);

    private static async Task<IResult> DecideAsync(
        int id,
        string status,
        string auditAction,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        McpApprovalService approvals,
        IAuditService audit,
        CancellationToken ct)
    {
        var userId = await RequireUserIdAsync(user, approvals, ct);
        var now = DateTime.UtcNow;

        // The conditional UPDATE is the decision lock: a concurrent decision,
        // an expired row or a foreign id cannot be approved twice.
        var updated = await db.McpApprovals
            .Where(x => x.Id == id &&
                        x.UserId == userId &&
                        x.Status == McpApprovalStatuses.Pending &&
                        x.ExpiresAt > now)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, status)
                    .SetProperty(x => x.DecidedAt, now)
                    .SetProperty(x => x.DecidedByUserId, userId),
                ct);
        if (updated == 0)
        {
            var current = await db.McpApprovals.AsNoTracking()
                              .SingleOrDefaultAsync(
                                  x => x.Id == id && x.UserId == userId, ct)
                          ?? throw Guard.NotFound("Approvazione", id);

            if (current.ExpiresAt <= now &&
                current.Status is McpApprovalStatuses.Pending
                    or McpApprovalStatuses.Approved)
            {
                await db.McpApprovals
                    .Where(x => x.Id == id &&
                                x.UserId == userId &&
                                x.Status == current.Status)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(
                            x => x.Status, McpApprovalStatuses.Expired),
                        ct);
                throw Guard.Conflict("Approvazione scaduta.");
            }

            throw Guard.Conflict(
                $"Approvazione non decidibile: stato {current.Status}.");
        }

        var dto = await db.McpApprovals.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new McpApprovalDto(
                x.Id,
                x.Action,
                x.ServerId,
                x.Server != null ? x.Server.Name : null,
                x.ServerApplicationId,
                x.ApplicationActionId,
                x.ParametersJson,
                x.Status,
                x.RequestedAt,
                x.ExpiresAt,
                x.DecidedAt,
                x.DecidedByUser != null ? x.DecidedByUser.Username : null,
                x.UsedAt))
            .SingleAsync(ct);
        await audit.RecordAsync(AuditEntries.Change(
            user,
            auditAction,
            "McpApproval",
            dto.Id,
            dto.ServerId,
            new { dto.Action, status }), ct);

        return Results.Ok(dto);
    }

    private static async Task<int> RequireUserIdAsync(
        ClaimsPrincipal user,
        McpApprovalService approvals,
        CancellationToken ct) =>
        await approvals.UserIdAsync(user, ct)
        ?? throw Guard.Forbidden("Utente non provisionato.");
}

public sealed record McpApprovalDto(
    int Id,
    string Action,
    int ServerId,
    string? ServerName,
    int? ServerApplicationId,
    int? ApplicationActionId,
    string ParametersJson,
    string Status,
    DateTime RequestedAt,
    DateTime ExpiresAt,
    DateTime? DecidedAt,
    string? DecidedByUsername,
    DateTime? UsedAt);

using System.Security.Claims;
using System.Text.Json;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;

namespace InfraOrgIt.Api.Features.Mcp;

/// <summary>
/// Human approval for MCP action tools (PT-18). The model can only request an
/// approval; a cookie-authenticated owner approves or denies it from the web
/// UI. Execution requires an Approved row and consumes it atomically.
/// </summary>
public sealed class McpApprovalService(InfraOrgItDbContext db)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int ParametersLimit = 2048;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializes only the whitelisted target parameters (no free text and no
    /// secrets). It throws instead of truncating so two different parameter
    /// sets can never collapse onto the same approval.
    /// </summary>
    public static string SerializeParameters(object parameters)
    {
        var json = JsonSerializer.Serialize(parameters, JsonOptions);
        if (json.Length > ParametersLimit)
            throw new McpException(
                $"Parametri oltre {ParametersLimit} caratteri: riduci i test id.");
        return json;
    }

    public async Task<int?> UserIdAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var username = user.Identity?.Name ?? "";
        return await db.Users
            .Where(x => x.Username == username)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<McpApproval> RequestAsync(
        int userId,
        string action,
        int serverId,
        int? serverApplicationId,
        int? applicationActionId,
        string parametersJson,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entity = new McpApproval
        {
            UserId = userId,
            Action = action,
            ServerId = serverId,
            ServerApplicationId = serverApplicationId,
            ApplicationActionId = applicationActionId,
            ParametersJson = parametersJson,
            Status = McpApprovalStatuses.Pending,
            RequestedAt = now,
            ExpiresAt = now.Add(Lifetime)
        };
        db.McpApprovals.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public Task<McpApproval?> FindAsync(
        int userId,
        int id,
        CancellationToken ct) =>
        db.McpApprovals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

    public Task<int> ExpireStaleAsync(
        int userId,
        DateTime now,
        CancellationToken ct) =>
        db.McpApprovals
            .Where(x => x.UserId == userId &&
                        (x.Status == McpApprovalStatuses.Pending ||
                         x.Status == McpApprovalStatuses.Approved) &&
                        x.ExpiresAt <= now)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Status, McpApprovalStatuses.Expired),
                ct);

    /// <summary>
    /// Single-use claim: only Approved, unexpired, owner rows are consumed.
    /// The UPDATE is the lock; two concurrent tool calls cannot both get 1.
    /// The approval is consumed even if the action later fails, so a failed
    /// attempt cannot be replayed.
    /// </summary>
    public async Task<bool> TryMarkUsedAsync(
        int userId,
        int id,
        DateTime now,
        CancellationToken ct) =>
        await db.McpApprovals
            .Where(x => x.Id == id &&
                        x.UserId == userId &&
                        x.Status == McpApprovalStatuses.Approved &&
                        x.ExpiresAt > now)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, McpApprovalStatuses.Used)
                    .SetProperty(x => x.UsedAt, now),
                ct) == 1;
}

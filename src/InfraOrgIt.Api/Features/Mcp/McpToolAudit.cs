using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Audit;

namespace InfraOrgIt.Api.Features.Mcp;

internal static class McpToolAudit
{
    public static Task OkAsync(
        IAuditService audit,
        ClaimsPrincipal user,
        string action,
        object detail,
        CancellationToken ct,
        int? serverId = null) =>
        RecordAsync(audit, user, action, "Ok", detail, null, ct, serverId);

    public static Task RecordAsync(
        IAuditService audit,
        ClaimsPrincipal user,
        string action,
        string outcome,
        object? detail,
        string? errorCode,
        CancellationToken ct,
        int? serverId = null)
    {
        var actorId = int.TryParse(
            user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : (int?)null;
        return audit.RecordAsync(new AuditEntry(
            "McpToolCall",
            action,
            outcome,
            ActorUserId: actorId,
            ActorUsername: user.Identity?.Name,
            ClientId: user.FindFirstValue("api_token_id"),
            ServerId: serverId,
            ErrorCode: errorCode,
            Detail: detail), ct);
    }

    public static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}

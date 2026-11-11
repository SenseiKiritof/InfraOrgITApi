namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

// Human approval for an MCP action tool. The model can request an approval
// but only the owner can decide it from the web session (cookie + CSRF).
// Expiry is enforced in the queries; rows are kept for the audit trail.
public sealed class McpApproval
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Action { get; set; } = "";
    public int ServerId { get; set; }
    public int? ServerApplicationId { get; set; }
    public int? ApplicationActionId { get; set; }
    public string ParametersJson { get; set; } = "{}";
    public string Status { get; set; } = McpApprovalStatuses.Pending;
    public DateTime RequestedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public int? DecidedByUserId { get; set; }
    public DateTime? UsedAt { get; set; }

    public User? User { get; set; }
    public User? DecidedByUser { get; set; }
    public Server? Server { get; set; }
    public ServerApplication? ServerApplication { get; set; }
    public ApplicationAction? ApplicationAction { get; set; }
}

public static class McpApprovalStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Denied = "Denied";
    public const string Used = "Used";
    public const string Expired = "Expired";

    public static readonly string[] All =
        [Pending, Approved, Denied, Used, Expired];
}

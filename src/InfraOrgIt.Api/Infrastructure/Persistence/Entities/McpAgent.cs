namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

// One MCP client (API token or OAuth client) approved once by its owner.
// Read tools do not need an agent; action tools require an Approved agent and
// at least one permission for the action and environment.
public sealed class McpAgent : EntityBase
{
    public int UserId { get; set; }
    public string ClientKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = McpAgentStatuses.Pending;
    public DateTime? ApprovedAt { get; set; }
    public int? ApprovedByUserId { get; set; }
    public DateTime? LastSeenAt { get; set; }

    public User? User { get; set; }
    public User? ApprovedByUser { get; set; }
    public List<McpAgentPermission> Permissions { get; set; } = [];
    public List<McpPermissionRequest> PermissionRequests { get; set; } = [];
}

public static class McpAgentStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Denied = "Denied";

    public static readonly string[] All = [Pending, Approved, Denied];
}

// An individual action the agent may run. A null environment means every
// environment; the normal environment RBAC remains mandatory.
public sealed class McpAgentPermission : EntityBase
{
    public string Action { get; set; } = "";
    public int? EnvironmentDefinitionId { get; set; }
    public int AgentId { get; set; }

    public McpAgent? Agent { get; set; }
    public EnvironmentDefinition? EnvironmentDefinition { get; set; }
}

// A permission change requested by the MCP agent. The owner approves or denies
// it from the cookie-authenticated web UI; approving adds an McpAgentPermission.
public sealed class McpPermissionRequest : EntityBase
{
    public int AgentId { get; set; }
    public string Action { get; set; } = "";
    public int? EnvironmentDefinitionId { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = McpPermissionRequestStatuses.Pending;
    public DateTime? DecidedAt { get; set; }
    public int? DecidedByUserId { get; set; }

    public McpAgent? Agent { get; set; }
    public User? DecidedByUser { get; set; }
    public EnvironmentDefinition? EnvironmentDefinition { get; set; }
}

public static class McpPermissionRequestStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Denied = "Denied";

    public static readonly string[] All = [Pending, Approved, Denied];
}

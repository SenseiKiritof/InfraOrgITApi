namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

// Append-only audit row shared by MCP tool calls and tunnel sessions.
// Actor and target ids are snapshots: history survives user/server deletion.
// Hash/PrevHash extend the SHA-256 chain; legacy rows have both null.
public sealed class AuditEvent
{
    public int Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Kind { get; set; } = "";
    public int? ActorUserId { get; set; }
    public string ActorUsername { get; set; } = "";
    public string? ClientId { get; set; }
    public string Action { get; set; } = "";
    public string? TargetType { get; set; }
    public int? TargetId { get; set; }
    public int? ServerId { get; set; }
    public string? TargetHost { get; set; }
    public int? TargetPort { get; set; }
    public int? LocalPort { get; set; }
    public string Outcome { get; set; } = "";
    public string? ErrorCode { get; set; }
    public int DurationMs { get; set; }
    public string? CorrelationId { get; set; }
    public string? DetailJson { get; set; }
    public string? PrevHash { get; set; }
    public string? Hash { get; set; }
}

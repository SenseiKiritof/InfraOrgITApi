namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

// Live projection of the desktop tunnel sessions. AuditEvents remains the
// append-only history for start/stop/error; this table is upserted by
// heartbeats and never duplicates audit rows.
public sealed class TunnelSession
{
    public int Id { get; set; }
    public string SessionKey { get; set; } = "";
    public int UserId { get; set; }
    public string ActorUsername { get; set; } = "";
    public string? ClientId { get; set; }
    public int ServerId { get; set; }
    public int? ServerApplicationId { get; set; }
    public int LocalPort { get; set; }
    public string TargetHost { get; set; } = "";
    public int TargetPort { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string Outcome { get; set; } = "";
    public string? ErrorCode { get; set; }
}

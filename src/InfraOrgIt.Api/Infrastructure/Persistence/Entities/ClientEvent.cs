namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// Event observed by the desktop (local check or offline tunnel) and synced
/// later. The event id plus username makes the batch endpoint idempotent.
/// </summary>
public sealed class ClientEvent : EntityBase
{
    public string EventId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Kind { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public int? ServerId { get; set; }
    public string? TargetHost { get; set; }
    public int? TargetPort { get; set; }
    public int? LocalPort { get; set; }
    public string Outcome { get; set; } = "";
    public string? ErrorCode { get; set; }
    public int DurationMs { get; set; }
    public string? DetailJson { get; set; }
}

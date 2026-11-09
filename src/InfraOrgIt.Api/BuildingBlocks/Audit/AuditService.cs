using System.Text.Json;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.BuildingBlocks.Validation;

namespace InfraOrgIt.Api.BuildingBlocks.Audit;

public sealed record AuditEntry(
    string Kind,
    string Action,
    string Outcome,
    int? ActorUserId = null,
    string? ActorUsername = null,
    string? ClientId = null,
    string? TargetType = null,
    int? TargetId = null,
    int? ServerId = null,
    string? TargetHost = null,
    int? TargetPort = null,
    int? LocalPort = null,
    string? ErrorCode = null,
    int DurationMs = 0,
    string? CorrelationId = null,
    object? Detail = null);

public interface IAuditService
{
    Task RecordAsync(AuditEntry entry, CancellationToken ct = default);
}

/// <summary>
/// Append-only audit writer shared by MCP tool calls and tunnel sessions.
/// Callers pass already-redacted detail; the writer only caps its size.
/// </summary>
public sealed class AuditService(InfraOrgItDbContext db) : IAuditService
{
    private const int DetailLimit = 4096;
    private static readonly string[] Kinds =
        ["McpToolCall", "TunnelStart", "TunnelStop", "TunnelError"];
    private static readonly string[] Outcomes = ["Ok", "Error", "Denied"];
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
    {
        if (!Kinds.Contains(entry.Kind))
            throw Guard.Unprocessable($"Tipo audit non ammesso: {entry.Kind}.");
        if (!Outcomes.Contains(entry.Outcome))
            throw Guard.Unprocessable($"Esito audit non ammesso: {entry.Outcome}.");

        var detail = entry.Detail is null
            ? null
            : JsonSerializer.Serialize(entry.Detail, JsonOptions);
        if (detail is { Length: > DetailLimit })
            detail = detail[..DetailLimit];

        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = DateTime.UtcNow,
            Kind = entry.Kind,
            ActorUserId = entry.ActorUserId,
            ActorUsername = entry.ActorUsername ?? "",
            ClientId = entry.ClientId,
            Action = entry.Action,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            ServerId = entry.ServerId,
            TargetHost = entry.TargetHost,
            TargetPort = entry.TargetPort,
            LocalPort = entry.LocalPort,
            Outcome = entry.Outcome,
            ErrorCode = entry.ErrorCode,
            DurationMs = entry.DurationMs,
            CorrelationId = entry.CorrelationId,
            DetailJson = detail
        });
        await db.SaveChangesAsync(ct);
    }
}

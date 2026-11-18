using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Audit;

/// <summary>
/// Batch intake for events observed by the desktop while the instance was
/// unreachable: local checks and offline tunnel start/stop/error rows. The
/// event id makes retries idempotent; the audit chain records the server
/// receive time and keeps the client time and source in the detail.
/// </summary>
public static class ClientEventsEndpoints
{
    private const int MaxBatch = 200;
    private static readonly string[] Kinds =
    [
        "check.tcp", "check.tls", "check.http",
        "tunnel.start", "tunnel.stop", "tunnel.error"
    ];

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/client-events", RecordAsync)
            .RequireAuthorization("Tunnel")
            .RequireRateLimiting("tunnels")
            .Produces<ClientEventsResult>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> RecordAsync(
        ClientEventsBatch input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        IAuditService audit,
        CancellationToken ct)
    {
        var events = input.Events;
        if (events is null || events.Count == 0)
            throw Guard.Unprocessable("Nessun evento da sincronizzare.");
        if (events.Count > MaxBatch)
            throw Guard.Unprocessable($"Massimo {MaxBatch} eventi per richiesta.");

        var username = user.Identity?.Name ?? "";
        if (string.IsNullOrWhiteSpace(username))
            throw Guard.Forbidden("Utente non provisionato.");

        var now = DateTime.UtcNow;
        var normalized = events
            .Select(item => Normalize(item, now))
            .GroupBy(item => item.EventId)
            .Select(group => group.First())
            .ToList();

        var environmentAccess = await access.GetAsync(user, ct);
        var serverIds = normalized
            .Where(x => x.ServerId is not null)
            .Select(x => x.ServerId!.Value)
            .Distinct()
            .ToList();
        if (serverIds.Count > 0)
        {
            var environments = await db.Servers.AsNoTracking()
                .Where(x => serverIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.ProviderEnvironmentId, ct);
            foreach (var serverId in serverIds)
            {
                if (!environments.TryGetValue(serverId, out var environmentId))
                    throw Guard.NotFound("Server", serverId);
                if (!environmentAccess.OperatorEnvironmentIds.Contains(environmentId))
                    throw Guard.Forbidden(
                        "Serve ServerOperator o ServerAdmin sull'ambiente del server.");
            }
        }

        var ids = normalized.Select(x => x.EventId).ToList();
        var existing = await db.ClientEvents.AsNoTracking()
            .Where(x => x.Username == username && ids.Contains(x.EventId))
            .Select(x => x.EventId)
            .ToListAsync(ct);
        var duplicates = existing.ToHashSet(StringComparer.Ordinal);
        var accepted = new List<string>();

        var (actorId, actorUsername, actorClientId) = AuditEntries.Actor(user);
        var fresh = normalized.Where(x => !duplicates.Contains(x.EventId)).ToList();
        if (fresh.Count > 0)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            foreach (var item in fresh)
            {
                await audit.RecordAsync(new AuditEntry(
                    AuditKind(item.Kind),
                    item.Kind,
                    item.Outcome,
                    ActorUserId: actorId,
                    ActorUsername: actorUsername,
                    ClientId: actorClientId,
                    TargetType: item.ServerId is null ? "Client" : "Server",
                    TargetId: item.ServerId,
                    ServerId: item.ServerId,
                    TargetHost: item.TargetHost,
                    TargetPort: item.TargetPort,
                    LocalPort: item.LocalPort,
                    ErrorCode: item.ErrorCode,
                    DurationMs: item.DurationMs,
                    CorrelationId: item.EventId,
                    Detail: new
                    {
                        source = "Desktop",
                        occurredAt = item.OccurredAt,
                        detail = item.Detail
                    }), ct);

                db.ClientEvents.Add(new ClientEvent
                {
                    EventId = item.EventId,
                    Username = username,
                    Kind = item.Kind,
                    OccurredAt = item.OccurredAt,
                    ReceivedAt = now,
                    ServerId = item.ServerId,
                    TargetHost = item.TargetHost,
                    TargetPort = item.TargetPort,
                    LocalPort = item.LocalPort,
                    Outcome = item.Outcome,
                    ErrorCode = item.ErrorCode,
                    DurationMs = item.DurationMs,
                    DetailJson = item.Detail
                });
                accepted.Add(item.EventId);
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        return Results.Ok(new ClientEventsResult(accepted, duplicates.ToList()));
    }

    private static NormalizedEvent Normalize(ClientEventInput input, DateTime now)
    {
        var eventId = Guard.Text(input.EventId, "EventId", 100);
        var kind = Guard.Text(input.Kind, "Kind", 50).ToLowerInvariant();
        if (!Kinds.Contains(kind))
            throw Guard.Unprocessable(
                $"Kind ammesso: {string.Join(", ", Kinds)}.");
        var outcome = string.IsNullOrWhiteSpace(input.Outcome)
            ? (kind.EndsWith(".error", StringComparison.Ordinal) ? "Error" : "Ok")
            : Guard.Text(input.Outcome, "Outcome", 20);
        if (outcome is not ("Ok" or "Error"))
            throw Guard.Unprocessable("Outcome ammesso: Ok, Error.");

        var occurred = input.OccurredAt?.UtcDateTime ?? now;
        if (occurred > now.AddMinutes(5) || occurred < now.AddDays(-90))
            occurred = now;

        var targetHost = Guard.OptionalText(input.TargetHost, "TargetHost", 255);
        var errorCode = Guard.OptionalText(input.ErrorCode, "ErrorCode", 100);
        var detail = Guard.OptionalText(input.Detail, "Detail", 2000);
        if (input.TargetPort is int targetPort)
            Guard.Port(targetPort, "TargetPort");
        if (input.LocalPort is int localPort)
            Guard.Port(localPort, "LocalPort");
        var duration = Math.Clamp(input.DurationMs ?? 0, 0, 86_400_000);

        return new NormalizedEvent(
            eventId, kind, occurred, outcome, input.ServerId,
            targetHost, input.TargetPort, input.LocalPort,
            duration, errorCode, detail);
    }

    private static string AuditKind(string kind) => kind switch
    {
        "tunnel.start" => "TunnelStart",
        "tunnel.stop" => "TunnelStop",
        "tunnel.error" => "TunnelError",
        _ => "ClientCheck"
    };

    private sealed record NormalizedEvent(
        string EventId,
        string Kind,
        DateTime OccurredAt,
        string Outcome,
        int? ServerId,
        string? TargetHost,
        int? TargetPort,
        int? LocalPort,
        int DurationMs,
        string? ErrorCode,
        string? Detail);
}

public sealed record ClientEventInput(
    string? EventId,
    string? Kind,
    DateTimeOffset? OccurredAt,
    string? Outcome,
    int? ServerId,
    string? TargetHost,
    int? TargetPort,
    int? LocalPort,
    int? DurationMs,
    string? ErrorCode,
    string? Detail);

public sealed record ClientEventsBatch(IReadOnlyList<ClientEventInput>? Events);

public sealed record ClientEventsResult(
    IReadOnlyList<string> Accepted,
    IReadOnlyList<string> Duplicates);

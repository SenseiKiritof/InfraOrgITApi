using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Tunnels;

/// <summary>
/// Allowlist and audit for the desktop tunnel app. The client authenticates
/// with a personal token carrying tunnels:operate; targets come only from the
/// inventory and only for environments where the user is an operator.
/// </summary>
public static class TunnelEndpoints
{
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(2);

    public static void Map(WebApplication app)
    {
        // Heartbeats are cheap, so the tunnel surface gets its own budget
        // instead of the tighter remote-run one used by server actions.
        var group = app.MapGroup("/api/tunnels")
            .RequireRateLimiting("tunnels");

        group.MapGet("/targets", ListTargetsAsync)
            .Produces<TunnelTargetListDto>()
            .RequireAuthorization("Tunnel");
        group.MapPost("/events", RecordEventAsync).RequireAuthorization("Tunnel");
        group.MapPost("/sessions", UpsertSessionAsync)
            .RequireAuthorization("Tunnel")
            .Produces(StatusCodes.Status204NoContent);
        group.MapGet("/sessions", ListSessionsAsync)
            .RequireAuthorization()
            .Produces<TunnelSessionListDto>();
    }

    private static async Task<IResult> ListTargetsAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        CancellationToken ct)
    {
        var environmentAccess = await access.GetAsync(user, ct);
        var operatorIds = environmentAccess.OperatorEnvironmentIds.ToArray();

        var rows = await db.Servers.AsNoTracking()
            .Where(x => operatorIds.Contains(x.ProviderEnvironmentId))
            .Where(x => x.Credential != null)
            .Where(x => x.ServerApplications.Any() || x.TunnelServices.Any(s => s.Enabled))
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.IpAddress,
                x.Hostname,
                x.SshPort,
                x.ProviderEnvironmentId,
                Username = x.Credential!.Username,
                x.JumpServerId,
                EnvironmentLabel = x.ProviderEnvironment!.Provider!.Name + " / " +
                                   x.ProviderEnvironment!.EnvironmentDefinition!.Name,
                Targets = x.ServerApplications
                    .OrderBy(sa => sa.Id)
                    .Select(sa => new
                    {
                        ServerApplicationId = sa.Id,
                        ApplicationId = sa.ApplicationVersion!.ApplicationId,
                        Application = sa.ApplicationVersion!.Application!.Name,
                        Version = sa.ApplicationVersion.VersionNumber,
                        TargetPort = sa.HostPort ?? sa.ExposedPort
                    })
                    .ToList()
            })
            .ToListAsync(ct);

        var serverIds = rows.Select(x => x.Id).ToArray();
        var services = await db.TunnelServices.AsNoTracking()
            .Where(x => x.Enabled && serverIds.Contains(x.ServerId))
            .OrderBy(x => x.Name)
            .Select(x => new TunnelServiceDto(
                x.Id,
                x.ServerId,
                x.Name,
                x.Mode,
                x.TargetHost,
                x.TargetPort,
                x.LocalPort,
                x.RemotePort,
                x.Kind,
                x.OpenPath,
                x.AutoStart,
                x.Enabled))
            .ToListAsync(ct);

        // Jump chain: the rows only carry the nearest hop id, so resolve the
        // remaining links in one query and walk them nearest-first, cap 3.
        var jumpLinks = await db.Servers.AsNoTracking()
            .Select(x => new JumpLink(
                x.Id,
                x.JumpServerId,
                string.IsNullOrWhiteSpace(x.IpAddress) ? x.Hostname : x.IpAddress,
                x.SshPort,
                x.Credential != null ? x.Credential.Username : null))
            .ToDictionaryAsync(x => x.Id, ct);

        var items = rows.Select(x =>
        {
            var jumps = JumpChain(x.JumpServerId, jumpLinks, x.Username);
            var visibleServices = services
                .Where(s => s.ServerId == x.Id)
                .Where(s => s.Mode != nameof(TunnelMode.Remote) ||
                            environmentAccess.AdminEnvironmentIds.Contains(
                                x.ProviderEnvironmentId))
                .ToList();

            return new TunnelTargetDto(
                x.Id,
                x.Name,
                x.EnvironmentLabel,
                string.IsNullOrWhiteSpace(x.IpAddress) ? x.Hostname : x.IpAddress,
                x.SshPort,
                x.Username,
                jumps.FirstOrDefault(),
                jumps,
                x.Targets.Select(t => new TunnelEndpointDto(
                    t.ServerApplicationId,
                    t.ApplicationId,
                    t.Application,
                    t.Version,
                    "127.0.0.1",
                    t.TargetPort,
                    t.TargetPort >= 1024 ? t.TargetPort : null)).ToList(),
                visibleServices);
        }).ToList();

        return Results.Ok(new TunnelTargetListDto(items));
    }

    private static IReadOnlyList<TunnelJumpDto> JumpChain(
        int? jumpServerId,
        IReadOnlyDictionary<int, JumpLink> links,
        string fallbackUsername)
    {
        var jumps = new List<TunnelJumpDto>();
        var seen = new HashSet<int>();
        var current = jumpServerId;

        while (current is int id && jumps.Count < 3 && seen.Add(id))
        {
            if (!links.TryGetValue(id, out var link))
                break;
            jumps.Add(new TunnelJumpDto(
                link.Host,
                link.Port,
                link.Username ?? fallbackUsername));
            current = link.JumpServerId;
        }

        return jumps;
    }

    private sealed record JumpLink(
        int Id,
        int? JumpServerId,
        string Host,
        int Port,
        string? Username);

    private static async Task<IResult> RecordEventAsync(
        TunnelEventInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        IAuditService audit,
        CancellationToken ct)
    {
        var kind = Guard.Text(input.Kind, "Kind", 20).ToLowerInvariant();
        var (auditKind, outcome) = kind switch
        {
            "start" => ("TunnelStart", "Ok"),
            "stop" => ("TunnelStop", "Ok"),
            "error" => ("TunnelError", "Error"),
            _ => throw Guard.Unprocessable(
                "Kind ammesso: start, stop, error.")
        };

        var server = await db.Servers.AsNoTracking()
            .Where(x => x.Id == input.ServerId)
            .Select(x => new { x.Id, x.ProviderEnvironmentId })
            .SingleOrDefaultAsync(ct)
            ?? throw Guard.NotFound("Server", input.ServerId);

        var environmentAccess = await access.GetAsync(user, ct);
        if (!environmentAccess.OperatorEnvironmentIds.Contains(
                server.ProviderEnvironmentId))
            throw Guard.Forbidden(
                "Serve ServerOperator o ServerAdmin sull'ambiente del server.");

        if (input.ServerApplicationId is int targetId &&
            !await db.ServerApplications.AnyAsync(
                x => x.Id == targetId && x.ServerId == input.ServerId, ct))
            throw Guard.NotFound("Deployment", targetId);

        var targetHost = Guard.OptionalText(input.TargetHost, "TargetHost", 255);
        var errorCode = Guard.OptionalText(input.ErrorCode, "ErrorCode", 100);
        var correlationId = Guard.OptionalText(
            input.CorrelationId, "CorrelationId", 100);
        var detail = Guard.OptionalText(input.Detail, "Detail", 2000);
        if (input.TargetPort is int targetPort)
            Guard.Port(targetPort, "TargetPort");
        if (input.LocalPort is int localPort)
            Guard.Port(localPort, "LocalPort");

        var actorId = int.TryParse(
            user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : (int?)null;

        await audit.RecordAsync(new AuditEntry(
            auditKind,
            $"tunnel.{kind}",
            outcome,
            ActorUserId: actorId,
            ActorUsername: user.Identity?.Name,
            ClientId: user.FindFirstValue("api_token_id"),
            TargetType: input.ServerApplicationId is null
                ? "Server"
                : "ServerApplication",
            TargetId: input.ServerApplicationId ?? input.ServerId,
            ServerId: input.ServerId,
            TargetHost: targetHost,
            TargetPort: input.TargetPort,
            LocalPort: input.LocalPort,
            ErrorCode: errorCode,
            CorrelationId: correlationId,
            Detail: detail is null ? null : new { message = detail }), ct);

        return Results.NoContent();
    }

    /// <summary>
    /// Upserts the live projection by (UserId, SessionKey). Heartbeats only
    /// update this row: the audit trail stays on /api/tunnels/events.
    /// </summary>
    private static async Task<IResult> UpsertSessionAsync(
        TunnelSessionInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        CancellationToken ct)
    {
        var sessionKey = Guard.Text(input.SessionKey, "SessionKey", 100);
        var status = (input.Status ?? "").Trim().ToLowerInvariant() switch
        {
            "active" => "Active",
            "stopped" => "Stopped",
            "error" => "Error",
            _ => throw Guard.Unprocessable(
                "Status ammesso: active, stopped, error.")
        };

        var server = await db.Servers.AsNoTracking()
            .Where(x => x.Id == input.ServerId)
            .Select(x => new { x.Id, x.ProviderEnvironmentId })
            .SingleOrDefaultAsync(ct)
            ?? throw Guard.NotFound("Server", input.ServerId);

        var environmentAccess = await access.GetAsync(user, ct);
        if (!environmentAccess.OperatorEnvironmentIds.Contains(
                server.ProviderEnvironmentId))
            throw Guard.Forbidden(
                "Serve ServerOperator o ServerAdmin sull'ambiente del server.");

        TunnelService? service = null;
        if (input.TunnelServiceId is int serviceId)
        {
            service = await db.TunnelServices.AsNoTracking()
                          .SingleOrDefaultAsync(
                              x => x.Id == serviceId && x.ServerId == input.ServerId, ct)
                      ?? throw Guard.NotFound("Servizio tunnel", serviceId);
        }

        int? deploymentTargetPort = null;
        if (input.ServerApplicationId is int applicationId)
        {
            deploymentTargetPort = await db.ServerApplications.AsNoTracking()
                .Where(x => x.Id == applicationId && x.ServerId == input.ServerId)
                .Select(x => (int?)(x.HostPort ?? x.ExposedPort))
                .SingleOrDefaultAsync(ct)
                ?? throw Guard.NotFound("Deployment", applicationId);
        }

        if (service is not null && deploymentTargetPort is not null)
            throw Guard.Unprocessable(
                "Indicare un deployment oppure un servizio tunnel, non entrambi.");

        var mode = Guard.Enum<TunnelMode>(
            string.IsNullOrWhiteSpace(input.Mode) ? nameof(TunnelMode.Local) : input.Mode,
            "Mode");
        (string Host, int? Target, int? Local, int? Remote) ports = mode switch
        {
            nameof(TunnelMode.Local) => (
                Guard.Text(input.TargetHost, "TargetHost", 255),
                RequirePort(input.TargetPort, "TargetPort"),
                RequirePort(input.LocalPort, "LocalPort"),
                RejectRemotePort(input.RemotePort)),
            nameof(TunnelMode.Dynamic) => (
                Guard.OptionalText(input.TargetHost, "TargetHost", 255) ?? "",
                RejectTargetPort(input.TargetPort),
                RequirePort(input.LocalPort, "LocalPort"),
                RejectRemotePort(input.RemotePort)),
            nameof(TunnelMode.Remote) => (
                Guard.Text(input.TargetHost, "TargetHost", 255),
                RequirePort(input.TargetPort, "TargetPort"),
                RejectLocalPort(input.LocalPort),
                RequirePort(input.RemotePort, "RemotePort")),
            _ => throw Guard.Unprocessable(
                "Mode ammesso: Local, Dynamic, Remote.")
        };
        var (targetHost, targetPort, localPort, remotePort) = ports;

        // PT-14: a Remote forward is only offered to ServerAdmin in the
        // target list, so the write path must enforce the same rule.
        if (mode == nameof(TunnelMode.Remote) &&
            !environmentAccess.AdminEnvironmentIds.Contains(
                server.ProviderEnvironmentId))
            throw Guard.Forbidden(
                "Il modo Remote richiede ServerAdmin sull'ambiente del server.");

        EnsureTargetAllowed(
            service, deploymentTargetPort, mode, targetHost, targetPort, remotePort);

        var errorCode = Guard.OptionalText(input.ErrorCode, "ErrorCode", 100);
        var clientId = Guard.OptionalText(
            user.FindFirstValue("api_token_id"), "ClientId", 100);
        var actorUsername = Guard.Text(
            user.Identity?.Name, "ActorUsername", 100);
        var userId = await ResolveUserIdAsync(user, db, ct);

        var now = DateTime.UtcNow;
        var session = await db.TunnelSessions.SingleOrDefaultAsync(
            x => x.UserId == userId && x.SessionKey == sessionKey, ct);
        if (session is null)
        {
            session = new TunnelSession
            {
                UserId = userId,
                SessionKey = sessionKey,
                StartedAt = now
            };
            db.TunnelSessions.Add(session);
        }
        else if (status == "Active" && session.EndedAt is not null)
        {
            // Same key reused after a stop: this is a new session.
            session.StartedAt = now;
        }

        session.ActorUsername = actorUsername;
        session.ClientId = clientId;
        session.ServerId = server.Id;
        session.ServerApplicationId = input.ServerApplicationId;
        session.TunnelServiceId = input.TunnelServiceId;
        session.Mode = mode;
        session.LocalPort = localPort;
        session.TargetHost = targetHost;
        session.TargetPort = targetPort;
        session.RemotePort = remotePort;
        session.LastSeenAt = now;
        session.Outcome = status;
        session.EndedAt = status == "Active" ? null : now;
        session.ErrorCode = status == "Error" ? errorCode : null;

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListSessionsAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - ActiveWindow;
        var query = db.TunnelSessions.AsNoTracking()
            .Where(x => x.Outcome == "Active" && x.LastSeenAt >= cutoff);

        if (!await access.IsSystemAdminAsync(user, ct))
        {
            var operatorIds = (await access.GetAsync(user, ct))
                .OperatorEnvironmentIds.ToArray();
            query = query.Where(x => db.Servers.Any(s =>
                s.Id == x.ServerId &&
                operatorIds.Contains(s.ProviderEnvironmentId)));
        }

        var items = await query
            .OrderByDescending(x => x.LastSeenAt)
            .ThenByDescending(x => x.Id)
            .Take(200)
            .Select(x => new TunnelSessionDto(
                x.Id,
                x.SessionKey,
                x.ActorUsername,
                x.ClientId,
                x.ServerId,
                db.Servers.Where(s => s.Id == x.ServerId)
                    .Select(s => s.Name).FirstOrDefault() ?? "",
                x.ServerApplicationId == null
                    ? null
                    : db.ServerApplications
                        .Where(sa => sa.Id == x.ServerApplicationId)
                        .Select(sa => sa.ApplicationVersion!.Application!.Name)
                        .FirstOrDefault(),
                x.TunnelServiceId,
                x.TunnelServiceId == null
                    ? null
                    : db.TunnelServices
                        .Where(ts => ts.Id == x.TunnelServiceId)
                        .Select(ts => ts.Name)
                        .FirstOrDefault(),
                x.Mode,
                x.LocalPort,
                x.TargetHost,
                x.TargetPort,
                x.RemotePort,
                x.StartedAt,
                x.LastSeenAt,
                x.Outcome))
            .ToListAsync(ct);

        return Results.Ok(new TunnelSessionListDto(items));
    }

    private static int RequirePort(int? value, string field) =>
        Guard.Port(
            value ?? throw Guard.Unprocessable($"Il campo {field} è obbligatorio."),
            field);

    private static int? RejectLocalPort(int? value) =>
        value is null
            ? null
            : throw Guard.Unprocessable(
                "Il campo LocalPort non è ammesso per il modo Remote.");

    private static int? RejectRemotePort(int? value) =>
        value is null
            ? null
            : throw Guard.Unprocessable(
                "Il campo RemotePort è ammesso solo per il modo Remote.");

    private static int? RejectTargetPort(int? value) =>
        value is null
            ? null
            : throw Guard.Unprocessable(
                "Il campo TargetPort non è ammesso per il modo Dynamic.");

    /// <summary>
    /// PT-14: the session projection must describe a target the inventory
    /// exposes, not an arbitrary host chosen by the client.
    /// </summary>
    private static void EnsureTargetAllowed(
        TunnelService? service,
        int? deploymentTargetPort,
        string mode,
        string targetHost,
        int? targetPort,
        int? remotePort)
    {
        if (service is null && deploymentTargetPort is null)
            throw Guard.Unprocessable(
                "Il target deve corrispondere a un deployment del server o a un servizio tunnel abilitato.");

        if (deploymentTargetPort is int port)
        {
            if (mode != nameof(TunnelMode.Local))
                throw Guard.Unprocessable(
                    "Un deployment si apre solo con Mode Local.");
            if (targetHost != "127.0.0.1" || targetPort != port)
                throw Guard.Unprocessable(
                    $"Il target non corrisponde al deployment: atteso 127.0.0.1:{port}.");
            return;
        }

        var allowed = service!;
        if (!allowed.Enabled)
            throw Guard.Unprocessable(
                $"Il servizio tunnel {allowed.Name} è disabilitato.");
        if (!string.Equals(allowed.Mode, mode, StringComparison.OrdinalIgnoreCase))
            throw Guard.Unprocessable(
                $"Il servizio tunnel {allowed.Name} usa il modo {allowed.Mode}.");

        switch (mode)
        {
            case nameof(TunnelMode.Dynamic):
                if (targetHost.Length != 0 || targetPort is not null || remotePort is not null)
                    throw Guard.Unprocessable(
                        "Il modo Dynamic non ammette un target.");
                break;

            case nameof(TunnelMode.Remote):
                RequireServiceTarget(allowed, targetHost, targetPort);
                if (remotePort != allowed.RemotePort)
                    throw Guard.Unprocessable(
                        $"La RemotePort non corrisponde al servizio tunnel {allowed.Name}.");
                break;

            default:
                RequireServiceTarget(allowed, targetHost, targetPort);
                break;
        }
    }

    private static void RequireServiceTarget(
        TunnelService service, string targetHost, int? targetPort)
    {
        if (!string.Equals(service.TargetHost, targetHost, StringComparison.Ordinal) ||
            service.TargetPort != targetPort)
            throw Guard.Unprocessable(
                $"Il target non corrisponde al servizio tunnel {service.Name}: " +
                $"atteso {service.TargetHost}:{service.TargetPort}.");
    }

    private static async Task<int> ResolveUserIdAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        if (int.TryParse(
                user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) &&
            id > 0)
            return id;

        var username = user.Identity?.Name ?? "";
        var userId = await db.Users.AsNoTracking()
            .Where(x => x.Username == username)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        return userId ?? throw Guard.Forbidden("Utente non provisionato.");
    }
}

public sealed record TunnelTargetDto(
    int ServerId,
    string ServerName,
    string EnvironmentLabel,
    string SshHost,
    int SshPort,
    string Username,
    TunnelJumpDto? Jump,
    IReadOnlyList<TunnelJumpDto> Jumps,
    IReadOnlyList<TunnelEndpointDto> Targets,
    IReadOnlyList<TunnelServiceDto> Services);

public sealed record TunnelTargetListDto(
    IReadOnlyList<TunnelTargetDto> Items);

public sealed record TunnelJumpDto(
    string Host,
    int Port,
    string Username);

public sealed record TunnelEndpointDto(
    int ServerApplicationId,
    int ApplicationId,
    string Application,
    string Version,
    string TargetHost,
    int TargetPort,
    int? SuggestedLocalPort);

public sealed record TunnelEventInput(
    string? Kind,
    int ServerId,
    int? ServerApplicationId = null,
    string? TargetHost = null,
    int? TargetPort = null,
    int? LocalPort = null,
    string? ErrorCode = null,
    string? Detail = null,
    string? CorrelationId = null);

public sealed record TunnelSessionInput(
    string? SessionKey,
    string? Status,
    int ServerId,
    string? TargetHost = null,
    int? TargetPort = null,
    int? LocalPort = null,
    int? ServerApplicationId = null,
    string? ErrorCode = null,
    string? Mode = null,
    int? RemotePort = null,
    int? TunnelServiceId = null);

public sealed record TunnelSessionDto(
    int Id,
    string SessionKey,
    string Username,
    string? ClientId,
    int ServerId,
    string ServerName,
    string? Application,
    int? ServiceId,
    string? ServiceName,
    string Mode,
    int? LocalPort,
    string TargetHost,
    int? TargetPort,
    int? RemotePort,
    DateTime StartedAt,
    DateTime LastSeenAt,
    string Status);

public sealed record TunnelSessionListDto(
    IReadOnlyList<TunnelSessionDto> Items);

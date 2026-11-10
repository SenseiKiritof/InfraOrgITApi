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

        group.MapGet("/targets", ListTargetsAsync).RequireAuthorization("Tunnel");
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
            .Where(x => x.ServerApplications.Any())
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.IpAddress,
                x.Hostname,
                x.SshPort,
                EnvironmentLabel = x.ProviderEnvironment!.Provider!.Name + " / " +
                                   x.ProviderEnvironment!.EnvironmentDefinition!.Name,
                Username = x.Credential!.Username,
                JumpHost = x.JumpServer == null
                    ? null
                    : (string.IsNullOrWhiteSpace(x.JumpServer.IpAddress)
                        ? x.JumpServer.Hostname
                        : x.JumpServer.IpAddress),
                JumpPort = x.JumpServer == null ? (int?)null : x.JumpServer.SshPort,
                JumpUsername = x.JumpServer == null
                    ? null
                    : (x.JumpServer.Credential != null
                        ? x.JumpServer.Credential.Username
                        : x.Credential!.Username),
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

        var items = rows.Select(x => new TunnelTargetDto(
            x.Id,
            x.Name,
            x.EnvironmentLabel,
            string.IsNullOrWhiteSpace(x.IpAddress) ? x.Hostname : x.IpAddress,
            x.SshPort,
            x.Username,
            x.JumpHost is null
                ? null
                : new TunnelJumpDto(
                    x.JumpHost,
                    x.JumpPort ?? 22,
                    x.JumpUsername ?? x.Username),
            x.Targets.Select(t => new TunnelEndpointDto(
                t.ServerApplicationId,
                t.ApplicationId,
                t.Application,
                t.Version,
                "127.0.0.1",
                t.TargetPort,
                t.TargetPort >= 1024 ? t.TargetPort : null)).ToList()))
            .ToList();

        return Results.Ok(new { items });
    }

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

        if (input.ServerApplicationId is int deploymentId &&
            !await db.ServerApplications.AnyAsync(
                x => x.Id == deploymentId && x.ServerId == input.ServerId, ct))
            throw Guard.NotFound("Deployment", deploymentId);

        var targetHost = Guard.Text(input.TargetHost, "TargetHost", 255);
        var targetPort = Guard.Port(input.TargetPort, "TargetPort");
        var localPort = Guard.Port(input.LocalPort, "LocalPort");
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
        session.LocalPort = localPort;
        session.TargetHost = targetHost;
        session.TargetPort = targetPort;
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
                x.LocalPort,
                x.TargetHost,
                x.TargetPort,
                x.StartedAt,
                x.LastSeenAt,
                x.Outcome))
            .ToListAsync(ct);

        return Results.Ok(new TunnelSessionListDto(items));
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
    IReadOnlyList<TunnelEndpointDto> Targets);

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
    string? TargetHost,
    int TargetPort,
    int LocalPort,
    int? ServerApplicationId = null,
    string? ErrorCode = null);

public sealed record TunnelSessionDto(
    int Id,
    string SessionKey,
    string Username,
    string? ClientId,
    int ServerId,
    string ServerName,
    string? Application,
    int LocalPort,
    string TargetHost,
    int TargetPort,
    DateTime StartedAt,
    DateTime LastSeenAt,
    string Status);

public sealed record TunnelSessionListDto(
    IReadOnlyList<TunnelSessionDto> Items);

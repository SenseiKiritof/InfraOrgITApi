using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Remote;

/// <summary>
/// Metadata and audit for the desktop-native console/files path. The desktop
/// never receives a credential: it connects with the user's SSH agent, while
/// the API keeps the allowlist, RBAC and the execution/audit projection.
/// </summary>
public static class RemoteClientEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/remote").RequireAuthorization("Tunnel");

        group.MapGet("/targets/{serverId:int}", GetTargetAsync)
            .Produces<RemoteTargetDto>();
        group.MapPost("/console-sessions", StartConsoleAsync)
            .RequireRateLimiting("remote-run")
            .Produces<RemoteConsoleStartDto>();
        group.MapPost("/console-sessions/{id:int}/complete", CompleteConsoleAsync)
            .RequireRateLimiting("remote-run");
        group.MapPost("/file-events", RecordFileAsync)
            .RequireRateLimiting("files");
        group.MapPost("/host-key", RecordHostKeyAsync)
            .RequireRateLimiting("remote-run");
        group.MapPost("/actions/{executionId:int}/complete", CompleteActionAsync)
            .RequireRateLimiting("remote-run");
    }

    private static async Task<IResult> GetTargetAsync(
        int serverId,
        string? scope,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking()
                         .Include(x => x.Credential)
                         .SingleOrDefaultAsync(x => x.Id == serverId, ct)
                     ?? throw Guard.NotFound("Server", serverId);

        if (string.Equals(scope, "actions", StringComparison.OrdinalIgnoreCase))
            await access.RequireOperatorAsync(
                user, [server.ProviderEnvironmentId], ct);
        else if (string.IsNullOrWhiteSpace(scope))
            await access.RequireAdminAsync(
                user, [server.ProviderEnvironmentId], ct);
        else
            throw Guard.Unprocessable("Scope ammesso: actions.");

        var username = server.Credential?.Username ?? "";
        if (string.IsNullOrWhiteSpace(username))
            throw Guard.Unprocessable(
                $"Il server {server.Name} non ha una credenziale SSH.");

        var host = string.IsNullOrWhiteSpace(server.IpAddress)
            ? server.Hostname
            : server.IpAddress;
        var jumps = await JumpChainAsync(
            db, server.JumpServerId, username, ct);

        return Results.Ok(new RemoteTargetDto(
            server.Id,
            server.Name,
            host,
            server.SshPort,
            username,
            jumps,
            server.HostKeyFingerprint,
            server.FileTransferProtocol));
    }

    private static async Task<IResult> StartConsoleAsync(
        RemoteConsoleStartInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking()
                         .SingleOrDefaultAsync(x => x.Id == input.ServerId, ct)
                     ?? throw Guard.NotFound("Server", input.ServerId);
        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);

        var correlationId = Guard.OptionalText(
            input.CorrelationId, "CorrelationId", 100);
        var execution = CommandAudit.Start(
            db,
            user.Identity?.Name ?? "",
            server,
            "Console",
            "Console desktop",
            "",
            DateTime.UtcNow,
            correlationId);
        db.CommandExecutions.Add(execution);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new RemoteConsoleStartDto(execution.Id));
    }

    private static async Task<IResult> CompleteConsoleAsync(
        int id,
        RemoteConsoleCompleteInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        IOptions<ServerAccessOptions> options,
        CancellationToken ct)
    {
        var username = user.Identity?.Name ?? "";
        var execution = await db.CommandExecutions
                            .SingleOrDefaultAsync(
                                x => x.Id == id && x.Username == username, ct)
                        ?? throw Guard.NotFound("Esecuzione", id);
        if (execution.Kind != "Console")
            throw Guard.Unprocessable("L'esecuzione non è una console.");
        if (execution.EndedAt is not null)
            throw Guard.Conflict("L'esecuzione è già conclusa.");

        var output = Guard.OptionalText(input.Output, "Output", 200_000) ?? "";
        CommandAudit.Complete(
            execution,
            new SshCommandResult(input.ExitCode, output, input.TimedOut),
            options.Value);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RecordFileAsync(
        RemoteFileEventInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking()
                         .SingleOrDefaultAsync(x => x.Id == input.ServerId, ct)
                     ?? throw Guard.NotFound("Server", input.ServerId);
        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);

        var operation = Guard.Enum<RemoteFileOperation>(
            input.Operation, "Operation");
        var outcome = string.IsNullOrWhiteSpace(input.Outcome)
            ? "Ok"
            : Guard.Enum<RemoteFileOutcome>(input.Outcome, "Outcome");
        var path = Guard.Text(input.Path, "Path", 1000);
        var targetHost = path.Length > 300 ? path[..300] : path;
        var detail = Guard.OptionalText(input.Error, "Error", 500);

        var (actorUserId, actorUsername, clientId) = AuditEntries.Actor(user);
        await audit.RecordAsync(new AuditEntry(
            "FileTransfer",
            operation,
            outcome,
            ActorUserId: actorUserId,
            ActorUsername: actorUsername,
            ClientId: clientId,
            TargetType: "Server",
            TargetId: server.Id,
            ServerId: server.Id,
            TargetHost: targetHost,
            ErrorCode: outcome == "Error" ? "client_file_error" : null,
            Detail: detail is null ? null : new { path, error = detail }), ct);

        return Results.NoContent();
    }

    private static async Task<IResult> CompleteActionAsync(
        int executionId,
        RemoteActionCompleteInput input,
        ClaimsPrincipal user,
        ApplicationActionRunner runner,
        CancellationToken ct)
    {
        var output = Guard.OptionalText(input.Output, "Output", 200_000) ?? "";
        await runner.CompleteClientAsync(
            user,
            executionId,
            new CommandResultDto(input.ExitCode, output, input.TimedOut),
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RecordHostKeyAsync(
        RemoteHostKeyInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var server = await db.Servers
                         .SingleOrDefaultAsync(x => x.Id == input.ServerId, ct)
                     ?? throw Guard.NotFound("Server", input.ServerId);
        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);

        var fingerprint = Guard.Text(input.Fingerprint, "Fingerprint", 100);
        if (server.HostKeyFingerprint is null)
        {
            server.HostKeyFingerprint = fingerprint;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }
        if (server.HostKeyFingerprint != fingerprint)
            throw Guard.Conflict(
                $"La host key di {server.Name} è cambiata: verificare il server e azzerare quella salvata.");
        return Results.NoContent();
    }

    private static async Task<IReadOnlyList<RemoteJumpDto>> JumpChainAsync(
        InfraOrgItDbContext db,
        int? jumpServerId,
        string fallbackUsername,
        CancellationToken ct)
    {
        var links = await db.Servers.AsNoTracking()
            .Select(x => new JumpLink(
                x.Id,
                x.JumpServerId,
                string.IsNullOrWhiteSpace(x.IpAddress) ? x.Hostname : x.IpAddress,
                x.SshPort,
                x.Credential != null ? x.Credential.Username : null,
                x.HostKeyFingerprint))
            .ToDictionaryAsync(x => x.Id, ct);

        var jumps = new List<RemoteJumpDto>();
        var seen = new HashSet<int>();
        var current = jumpServerId;
        while (current is int id && jumps.Count < 3 && seen.Add(id))
        {
            if (!links.TryGetValue(id, out var link))
                break;
            jumps.Add(new RemoteJumpDto(
                link.Host,
                link.Port,
                link.Username ?? fallbackUsername,
                link.HostKeyFingerprint));
            current = link.JumpServerId;
        }

        return jumps;
    }

    private sealed record JumpLink(
        int Id,
        int? JumpServerId,
        string Host,
        int Port,
        string? Username,
        string? HostKeyFingerprint);
}

public enum RemoteFileOperation
{
    Upload,
    Download,
    Rename,
    Delete,
    CreateDirectory
}

public enum RemoteFileOutcome
{
    Ok,
    Error
}

public sealed record RemoteTargetDto(
    int ServerId,
    string Name,
    string Host,
    int SshPort,
    string Username,
    IReadOnlyList<RemoteJumpDto> Jumps,
    string? HostKeyFingerprint,
    string? FileTransferProtocol);

public sealed record RemoteJumpDto(
    string Host,
    int Port,
    string Username,
    string? HostKeyFingerprint);

public sealed record RemoteConsoleStartInput(int ServerId, string? CorrelationId);

public sealed record RemoteConsoleStartDto(int ExecutionId);

public sealed record RemoteConsoleCompleteInput(
    int? ExitCode,
    string? Output,
    bool TimedOut = false);

public sealed record RemoteHostKeyInput(int ServerId, string? Fingerprint);

public sealed record RemoteActionCompleteInput(
    int? ExitCode,
    string? Output,
    bool TimedOut = false);

public sealed record RemoteFileEventInput(
    int ServerId,
    string? Operation,
    string? Path,
    string? Outcome = null,
    string? Error = null);

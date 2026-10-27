using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Remote;

public static class ConsoleEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/socket/servers/{id:int}/console", OpenAsync)
            .RequireAuthorization()
            .RequireRateLimiting("console");
    }

    private static async Task OpenAsync(
        HttpContext http,
        int id,
        uint? cols,
        uint? rows,
        string? resume,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        ISshConnector ssh,
        ConsoleSessionRegistry sessions,
        IOptions<ServerAccessOptions> accessOptions,
        IOptions<FrontendOptions> frontend,
        InfraOrgItDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (!http.WebSockets.IsWebSocketRequest)
            throw Guard.Unprocessable("Serve una richiesta WebSocket.");

        var origin = http.Request.Headers.Origin.ToString().TrimEnd('/');
        if (string.IsNullOrEmpty(origin) ||
            !frontend.Value.EffectiveOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            throw Guard.Forbidden("Origin non ammessa.");

        var server = await db.Servers
                         .Include(x => x.ProviderEnvironment)
                         .Include(x => x.Credential)
                         .SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Server", id);
        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);

        var username = user.Identity?.Name ?? "";
        var console = string.IsNullOrWhiteSpace(resume)
            ? null
            : sessions.Find(resume, id, username);

        using var socket = await http.WebSockets.AcceptWebSocketAsync();

        if (console is null)
        {
            var execution = CommandAudit.Start(
                db, username, server, "Console", "Console", "", DateTime.UtcNow);
            db.CommandExecutions.Add(execution);
            await db.SaveChangesAsync(ct);

            ISshSession? sshSession = null;
            try
            {
                sshSession = await ssh.ConnectAsync(server, ct);
                var shell = await sshSession.OpenShellAsync(
                    Math.Clamp(cols ?? 80, 10, 500),
                    Math.Clamp(rows ?? 24, 5, 200),
                    ct);

                console = sessions.Create(id, username, sshSession, shell, execution.Id);
                sshSession = null; // la proprietà passa alla console
            }
            catch (Exception exception)
            {
                if (sshSession is not null)
                    await sshSession.DisposeAsync();
                CommandAudit.Complete(
                    execution,
                    new SshCommandResult(null, exception.Message),
                    accessOptions.Value);
                await db.SaveChangesAsync(CancellationToken.None);
                await SendErrorAsync(socket, exception.Message);
                return;
            }
        }

        try
        {
            await console.AttachAsync(
                socket,
                Math.Clamp(cols ?? 80, 10, 500),
                Math.Clamp(rows ?? 24, 5, 200),
                ct);

            var terminate = await console.RunBrowserAsync(socket, ct);
            if (terminate)
                await console.TerminateAsync("chiusa dall'utente");
            else
                await console.DetachAsync(socket);
        }
        catch (OperationCanceledException)
        {
            await console.DetachAsync(socket);
        }
        catch (Exception exception)
        {
            loggerFactory
                .CreateLogger("ConsoleEndpoints")
                .LogWarning(exception, "Console {SessionId} interrotta", console.Id);
            await console.DetachAsync(socket);
        }
    }

    private static async Task SendErrorAsync(WebSocket socket, string error)
    {
        try
        {
            if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
                return;

            await socket.SendAsync(
                Encoding.UTF8.GetBytes($"\r\n[errore] {error}\r\n"),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None);
            await socket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch (Exception exception)
            when (exception is WebSocketException or InvalidOperationException
                      or ObjectDisposedException)
        {
            // browser già chiuso
        }
    }
}

using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        ISshConnector ssh,
        IOptions<ServerAccessOptions> accessOptions,
        IOptions<FrontendOptions> frontend,
        InfraOrgItDbContext db,
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

        using var socket = await http.WebSockets.AcceptWebSocketAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(accessOptions.Value.ConsoleMaxMinutes));

        var startedAt = DateTime.UtcNow;
        var execution = CommandAudit.Start(
            db, user.Identity?.Name ?? "", server, "Console", "Console", "", startedAt);
        db.CommandExecutions.Add(execution);
        await db.SaveChangesAsync(ct);

        string? error = null;
        try
        {
            await using var session = await ssh.ConnectAsync(server, cts.Token);
            await db.SaveChangesAsync(cts.Token);

            await using var shell = await session.OpenShellAsync(
                Math.Clamp(cols ?? 80, 10, 500),
                Math.Clamp(rows ?? 24, 5, 200),
                cts.Token);

            var completed = await Task.WhenAny(
                PumpToBrowserAsync(shell, socket, cts.Token),
                PumpFromBrowserAsync(shell, socket, cts.Token));
            await cts.CancelAsync();
            await completed;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            error = "Sessione terminata per timeout.";
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                if (error is not null)
                    await socket.SendAsync(
                        Encoding.UTF8.GetBytes($"\r\n[errore] {error}\r\n"),
                        WebSocketMessageType.Binary, true, CancellationToken.None);
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or InvalidOperationException)
        {
            // browser già chiuso
        }

        CommandAudit.Complete(
            execution,
            new SshCommandResult(error is null ? 0 : null, error ?? ""),
            accessOptions.Value);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task PumpToBrowserAsync(
        IShellSession shell,
        WebSocket socket,
        CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (true)
        {
            var read = await shell.Stream.ReadAsync(buffer, ct);
            if (read <= 0)
                return;
            if (socket.State != WebSocketState.Open)
                return;
            await socket.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, true, ct);
        }
    }

    private static async Task PumpFromBrowserAsync(
        IShellSession shell,
        WebSocket socket,
        CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var message = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (message.MessageType == WebSocketMessageType.Close)
                return;

            if (message.MessageType == WebSocketMessageType.Binary)
            {
                await shell.Stream.WriteAsync(buffer.AsMemory(0, message.Count), ct);
                await shell.Stream.FlushAsync(ct);
            }
            else if (message.EndOfMessage)
            {
                Resize(shell, buffer.AsSpan(0, message.Count));
            }
        }
    }

    private static void Resize(IShellSession shell, ReadOnlySpan<byte> json)
    {
        try
        {
            var root = JsonDocument.Parse(json.ToArray()).RootElement;
            if (root.GetProperty("type").GetString() == "resize")
                shell.Resize(
                    Math.Clamp(root.GetProperty("cols").GetUInt32(), 10, 500),
                    Math.Clamp(root.GetProperty("rows").GetUInt32(), 5, 200));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
                                              or InvalidOperationException or FormatException)
        {
            // messaggio di controllo malformato: ignorato
        }
    }
}

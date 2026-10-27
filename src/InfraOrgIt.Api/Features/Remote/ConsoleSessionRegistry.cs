using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Remote;

/// Console SSH staccabili: la shell resta viva dopo la chiusura del WebSocket
/// per ConsoleResumeMinutes, così un refresh del browser può riagganciarla.
public sealed class ConsoleSessionRegistry(
    IOptions<ServerAccessOptions> options,
    IServiceScopeFactory scopes,
    ILogger<ConsoleSessionRegistry> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ConsoleSession> sessions =
        new(StringComparer.Ordinal);
    private int stopping;

    public ConsoleSession? Find(string id, int serverId, string username) =>
        sessions.TryGetValue(id, out var session) && session.Matches(serverId, username)
            ? session
            : null;

    public ConsoleSession Create(
        int serverId,
        string username,
        ISshSession ssh,
        IShellSession shell,
        int executionId)
    {
        var session = new ConsoleSession(
            CryptoRandom.Token(),
            serverId,
            username,
            ssh,
            shell,
            executionId,
            options.Value,
            Remove,
            CompleteAuditAsync,
            logger);
        sessions[session.Id] = session;
        session.Start();
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref stopping, 1);
        foreach (var session in sessions.Values)
            await session.TerminateAsync("applicazione arrestata");
    }

    private void Remove(string id) => sessions.TryRemove(id, out _);

    private async Task CompleteAuditAsync(int executionId, string reason)
    {
        if (Volatile.Read(ref stopping) == 1)
            return;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var execution = await db.CommandExecutions
                .SingleOrDefaultAsync(x => x.Id == executionId);
            if (execution is null)
                return;

            CommandAudit.Complete(
                execution,
                new SshCommandResult(0, reason),
                options.Value);
            await db.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Audit console non aggiornato (execution {ExecutionId})",
                executionId);
        }
    }
}

public sealed class ConsoleSession : IAsyncDisposable
{
    private const int MaxReplayBytes = 256 * 1024;

    private readonly object gate = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<byte> replay = [];
    private readonly ServerAccessOptions options;
    private readonly Action<string> remove;
    private readonly Func<int, string, Task> completeAudit;
    private readonly ILogger logger;

    private WebSocket? socket;
    private bool attached;
    private CancellationTokenSource? expiry;
    private int terminated;

    public ConsoleSession(
        string id,
        int serverId,
        string username,
        ISshSession ssh,
        IShellSession shell,
        int executionId,
        ServerAccessOptions options,
        Action<string> remove,
        Func<int, string, Task> completeAudit,
        ILogger logger)
    {
        Id = id;
        ServerId = serverId;
        Username = username;
        Ssh = ssh;
        Shell = shell;
        ExecutionId = executionId;
        this.options = options;
        this.remove = remove;
        this.completeAudit = completeAudit;
        this.logger = logger;
    }

    public string Id { get; }
    public int ServerId { get; }
    public string Username { get; }
    public int ExecutionId { get; }
    public ISshSession Ssh { get; }
    public IShellSession Shell { get; }

    public bool Matches(int serverId, string username) =>
        ServerId == serverId &&
        string.Equals(Username, username, StringComparison.OrdinalIgnoreCase);

    internal void Start()
    {
        _ = Task.Run(PumpAsync);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(options.ConsoleMaxMinutes),
                    lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await TerminateAsync("durata massima della console raggiunta");
        });
    }

    public async Task AttachAsync(
        WebSocket webSocket,
        uint columns,
        uint rows,
        CancellationToken ct)
    {
        byte[] snapshot;
        int mark;
        lock (gate)
        {
            socket = webSocket;
            attached = false;
            expiry?.Cancel();
            expiry?.Dispose();
            expiry = null;
            snapshot = replay.ToArray();
            mark = replay.Count;
        }

        Shell.Resize(columns, rows);

        await sendGate.WaitAsync(ct);
        try
        {
            var helloSocket = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { type = "session", id = Id }));
            await SendRawAsync(webSocket, helloSocket, WebSocketMessageType.Text, ct);

            if (snapshot.Length > 0)
                await SendRawAsync(webSocket, snapshot, WebSocketMessageType.Binary, ct);

            // L'output arrivato durante l'invio del replay non deve andare perso
            // né essere inviato due volte: lo si spedisce qui, poi si aggancia il pump.
            while (true)
            {
                byte[] tail;
                lock (gate)
                {
                    if (!ReferenceEquals(socket, webSocket))
                        return;

                    if (mark > replay.Count)
                        mark = replay.Count;

                    if (replay.Count == mark)
                    {
                        attached = true;
                        break;
                    }

                    tail = replay.Skip(mark).ToArray();
                    mark = replay.Count;
                }

                await SendRawAsync(webSocket, tail, WebSocketMessageType.Binary, ct);
            }
        }
        finally
        {
            sendGate.Release();
        }

        logger.LogInformation(
            "Console {SessionId} agganciata al server {ServerId}",
            Id,
            ServerId);
    }

    public async Task<bool> RunBrowserAsync(WebSocket webSocket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            ValueWebSocketReceiveResult message;
            try
            {
                message = await webSocket.ReceiveAsync(buffer.AsMemory(), ct);
            }
            catch (Exception exception)
                when (exception is WebSocketException or OperationCanceledException
                          or ObjectDisposedException)
            {
                return false;
            }

            if (message.MessageType == WebSocketMessageType.Close)
                return false;

            if (message.MessageType == WebSocketMessageType.Binary)
            {
                try
                {
                    await Shell.Stream.WriteAsync(buffer.AsMemory(0, message.Count), ct);
                    await Shell.Stream.FlushAsync(ct);
                }
                catch (Exception exception)
                    when (exception is IOException or ObjectDisposedException)
                {
                    return false;
                }
            }
            else if (message.EndOfMessage && HandleControl(buffer.AsSpan(0, message.Count)))
            {
                return true;
            }
        }
    }

    public Task DetachAsync(WebSocket webSocket)
    {
        lock (gate)
        {
            if (!ReferenceEquals(socket, webSocket))
                return Task.CompletedTask;
            socket = null;
            attached = false;
        }

        ScheduleExpiry();
        return Task.CompletedTask;
    }

    public async Task TerminateAsync(string reason)
    {
        if (Interlocked.Exchange(ref terminated, 1) == 1)
            return;

        lifetime.Cancel();

        WebSocket? current;
        lock (gate)
        {
            current = socket;
            socket = null;
            attached = false;
            expiry?.Cancel();
            expiry?.Dispose();
            expiry = null;
        }

        try
        {
            if (current is { State: WebSocketState.Open or WebSocketState.CloseReceived })
                await current.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch (Exception exception)
            when (exception is WebSocketException or OperationCanceledException
                      or ObjectDisposedException)
        {
            // browser già chiuso
        }

        await Shell.DisposeAsync();
        await Ssh.DisposeAsync();
        remove(Id);
        await completeAudit(ExecutionId, reason);

        logger.LogInformation("Console {SessionId} terminata: {Reason}", Id, reason);
    }

    public ValueTask DisposeAsync() => new(TerminateAsync("sessione chiusa"));

    private async Task PumpAsync()
    {
        var buffer = new byte[4096];
        var reason = "shell chiusa dal server";

        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var read = await Shell.Stream.ReadAsync(buffer, lifetime.Token);
                if (read <= 0)
                    break;

                WebSocket? current;
                lock (gate)
                {
                    Append(buffer.AsSpan(0, read));
                    current = attached ? socket : null;
                }

                if (current is { State: WebSocketState.Open })
                {
                    var failed = false;
                    await sendGate.WaitAsync(lifetime.Token);
                    try
                    {
                        if (current.State == WebSocketState.Open)
                            await SendRawAsync(
                                current,
                                buffer.AsMemory(0, read),
                                WebSocketMessageType.Binary,
                                lifetime.Token);
                    }
                    catch (Exception exception)
                        when (exception is WebSocketException or IOException
                                  or ObjectDisposedException)
                    {
                        failed = true;
                    }
                    finally
                    {
                        sendGate.Release();
                    }

                    if (failed)
                    {
                        await DetachAsync(current);
                        logger.LogInformation(
                            "Console {SessionId} staccata: invio al browser fallito",
                            Id);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            reason = exception.Message;
        }

        await TerminateAsync(reason);
    }

    private void Append(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
            replay.Add(value);
        if (replay.Count > MaxReplayBytes)
            replay.RemoveRange(0, replay.Count - MaxReplayBytes);
    }

    private void ScheduleExpiry()
    {
        var cts = new CancellationTokenSource();
        lock (gate)
        {
            expiry?.Cancel();
            expiry?.Dispose();
            expiry = cts;
        }

        var minutes = Math.Max(1, options.ConsoleResumeMinutes);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(minutes), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await TerminateAsync("sessione scaduta dopo la disconnessione");
        });
    }

    private static async Task SendRawAsync(
        WebSocket target,
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType type,
        CancellationToken ct) =>
        await target.SendAsync(payload, type, true, ct);

    private bool HandleControl(ReadOnlySpan<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();

            if (type == "resize")
            {
                Shell.Resize(
                    Math.Clamp(root.GetProperty("cols").GetUInt32(), 10, 500),
                    Math.Clamp(root.GetProperty("rows").GetUInt32(), 5, 200));
                return false;
            }

            return type == "terminate";
        }
        catch (Exception exception)
            when (exception is JsonException or KeyNotFoundException
                      or InvalidOperationException or FormatException)
        {
            return false;
        }
    }
}

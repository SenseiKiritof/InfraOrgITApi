using System.Net;
using System.Net.Sockets;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.Infrastructure.Files;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace InfraOrgIt.Api.Infrastructure.Ssh;

public sealed class SshConnector(
    ServerCredentialResolver credentials,
    IOptions<ServerAccessOptions> options,
    InfraOrgItDbContext db,
    ILogger<SshConnector> logger) : ISshConnector
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public Task<ISshSession> ConnectAsync(Server server, CancellationToken ct) =>
        ConnectInternalAsync(server, new HashSet<int>(), ct);

    public Task<IFileTransferSession> OpenSftpAsync(
        Server server,
        ServerCredential credential,
        int port,
        CancellationToken ct) =>
        OpenSftpInternalAsync(server, credential, port, new HashSet<int>(), ct);

    private async Task<IFileTransferSession> OpenSftpInternalAsync(
        Server server,
        ServerCredential credential,
        int port,
        HashSet<int> visited,
        CancellationToken ct)
    {
        var host = HostOf(server);
        if (server.JumpServerId is not int jumpId)
            return new SftpFileSession(
                await ConnectSftpClientAsync(server, credential, host, port, ct));

        var (jumpSession, forwarded) =
            await OpenTunnelAsync(server, host, port, visited, ct);
        try
        {
            var client = await ConnectSftpClientAsync(
                server, credential, "127.0.0.1", (int)forwarded.BoundPort, ct);
            return new SftpFileSession(client, forwarded, jumpSession);
        }
        catch
        {
            forwarded.Dispose();
            await jumpSession.DisposeAsync();
            throw;
        }
    }

    private async Task<ISshSession> ConnectInternalAsync(
        Server server,
        HashSet<int> visited,
        CancellationToken ct)
    {
        var host = HostOf(server);
        if (server.JumpServerId is not int jumpId)
            return new SshSession(
                await ConnectClientAsync(server, host, server.SshPort, ct),
                options.Value.MaxOutputBytes);

        var (jumpSession, forwarded) = await OpenTunnelAsync(
            server, host, server.SshPort, visited, ct);
        try
        {
            var client = await ConnectClientAsync(
                server, "127.0.0.1", (int)forwarded.BoundPort, ct);
            return new SshSession(
                client,
                options.Value.MaxOutputBytes,
                forwarded,
                jumpSession);
        }
        catch
        {
            forwarded.Dispose();
            await jumpSession.DisposeAsync();
            throw;
        }
    }

    private async Task<(SshSession TunnelHost, ForwardedPortLocal Forwarded)> OpenTunnelAsync(
        Server server,
        string host,
        int port,
        HashSet<int> visited,
        CancellationToken ct)
    {
        if (!visited.Add(server.Id))
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "jump_cycle",
                "La catena dei server proxy è ciclica.");

        var jumpId = server.JumpServerId!.Value;
        var jump = await db.Servers.AsNoTracking()
                       .Include(x => x.Credential)
                       .SingleOrDefaultAsync(x => x.Id == jumpId, ct)
                   ?? throw new ApiException(
                       StatusCodes.Status422UnprocessableEntity,
                       "jump_missing",
                       $"Il server proxy {jumpId} non esiste più.");

        var jumpSession = await ConnectInternalAsync(jump, visited, ct);
        if (jumpSession is not SshSession tunnelHost)
        {
            await jumpSession.DisposeAsync();
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "jump_unsupported",
                "Il server proxy non può essere usato come tunnel.");
        }

        var forwarded = new ForwardedPortLocal("127.0.0.1", host, (uint)port);
        try
        {
            tunnelHost.Client.AddForwardedPort(forwarded);
            forwarded.Start();
            return (tunnelHost, forwarded);
        }
        catch
        {
            forwarded.Dispose();
            await jumpSession.DisposeAsync();
            throw;
        }
    }

    private async Task<SshClient> ConnectClientAsync(
        Server server,
        string host,
        int port,
        CancellationToken ct)
    {
        var credential = server.Credential
                         ?? throw new ApiException(
                             StatusCodes.Status422UnprocessableEntity,
                             "credential_missing",
                             $"Il server {server.Name} non ha una credenziale SSH.");

        var client = new SshClient(ConnectionFor(credential, host, port));
        await ConnectAsync(client, server, credential, ct);
        return client;
    }

    private async Task<SftpClient> ConnectSftpClientAsync(
        Server server,
        ServerCredential credential,
        string host,
        int port,
        CancellationToken ct)
    {
        var client = new SftpClient(ConnectionFor(credential, host, port));
        await ConnectAsync(client, server, credential, ct);
        return client;
    }

    private Renci.SshNet.ConnectionInfo ConnectionFor(
        ServerCredential credential,
        string host,
        int port) =>
        new(host, port, credential.Username, credentials.BuildAuthentication(credential))
        {
            Timeout = ConnectTimeout
        };

    private async Task ConnectAsync(
        BaseClient client,
        Server server,
        ServerCredential credential,
        CancellationToken ct)
    {
        string? received = null;
        client.HostKeyReceived += (_, args) =>
        {
            received = args.FingerPrintSHA256;
            args.CanTrust = server.HostKeyFingerprint is null ||
                            server.HostKeyFingerprint == received;
        };

        try
        {
            await client.ConnectAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            client.Dispose();
            if (received is not null &&
                server.HostKeyFingerprint is not null &&
                received != server.HostKeyFingerprint)
            {
                logger.LogWarning(
                    "Host key cambiata per il server {ServerId}: attesa {Expected}, ricevuta {Received}",
                    server.Id, server.HostKeyFingerprint, received);
                throw new ApiException(
                    StatusCodes.Status409Conflict,
                    "host_key_mismatch",
                    $"La host key di {server.Name} è cambiata: verificare il server e azzerare quella salvata.");
            }

            logger.LogWarning(
                exception,
                "Connessione SSH fallita per il server {ServerId} ({Credential})",
                server.Id,
                credential.Name);
            throw new ApiException(
                StatusCodes.Status502BadGateway,
                "ssh_unreachable",
                $"Connessione SSH a {server.Name} fallita: {exception.Message}");
        }

        server.HostKeyFingerprint ??= received;

        // PT-09: callers such as file transfer and tunnel discovery load the
        // server with AsNoTracking, so setting the property alone would lose
        // the first-use key. Persist it explicitly and only when it was empty.
        if (received is not null)
        {
            var stored = await db.Servers
                .Where(x => x.Id == server.Id && x.HostKeyFingerprint == null)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(x => x.HostKeyFingerprint, received), ct);
            if (stored > 0)
                logger.LogInformation(
                    "Host key SSH memorizzata al primo uso per il server {ServerId}",
                    server.Id);
        }
    }

    private static string HostOf(Server server) =>
        string.IsNullOrWhiteSpace(server.IpAddress) ? server.Hostname : server.IpAddress;

    private sealed class SshSession(
        SshClient client,
        int maxOutputBytes,
        ForwardedPortLocal? forwarded = null,
        ISshSession? jumpSession = null) : ISshSession
    {
        public SshClient Client => client;

        public async Task<SshCommandResult> RunAsync(
            string script,
            TimeSpan timeout,
            CancellationToken ct)
        {
            using var command = client.CreateCommand(script);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            try
            {
                await command.ExecuteAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new SshCommandResult(
                    null,
                    Truncate(command.Result + command.Error + $"\n[timeout dopo {timeout}]", maxOutputBytes),
                    TimedOut: true);
            }

            return new SshCommandResult(
                command.ExitStatus,
                Truncate(command.Result + command.Error, maxOutputBytes));
        }

        public Task<IShellSession> OpenShellAsync(uint columns, uint rows, CancellationToken ct)
        {
            var stream = client.CreateShellStream("xterm", columns, rows, 0, 0, 4096);
            return Task.FromResult<IShellSession>(new ShellSession(stream));
        }

        public async Task<Stream> OpenForwardAsync(
            string host,
            int port,
            CancellationToken ct)
        {
            var forwarded = new ForwardedPortLocal("127.0.0.1", host, (uint)port);
            client.AddForwardedPort(forwarded);
            forwarded.Start();
            var tcp = new TcpClient();
            try
            {
                await tcp.ConnectAsync(IPAddress.Loopback, (int)forwarded.BoundPort, ct);
                return new ForwardedStream(tcp, forwarded);
            }
            catch
            {
                tcp.Dispose();
                forwarded.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            forwarded?.Dispose();
            if (jumpSession is not null)
                await jumpSession.DisposeAsync();
        }
    }

    private sealed class ShellSession(ShellStream stream) : IShellSession
    {
        public Stream Stream => stream;

        public void Resize(uint columns, uint rows) =>
            stream.ChangeWindowSize(columns, rows, 0, 0);

        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SftpFileSession(
        SftpClient client,
        ForwardedPortLocal? forwarded = null,
        ISshSession? jumpSession = null) : IFileTransferSession
    {
        // WorkingDirectory è la home restituita dal server dopo il login.
        public string HomePath => RemotePath.Normalize(client.WorkingDirectory);

        public async Task<IReadOnlyList<RemoteFileEntry>> ListAsync(
            string path,
            CancellationToken ct)
        {
            var entries = new List<RemoteFileEntry>();
            await foreach (var entry in client.ListDirectoryAsync(
                               RemotePath.Normalize(path), ct))
            {
                if (entry.Name is "." or "..") continue;
                entries.Add(new RemoteFileEntry(
                    entry.Name,
                    RemotePath.Normalize(entry.FullName),
                    entry.IsDirectory,
                    entry.IsDirectory ? 0 : entry.Length,
                    entry.LastWriteTimeUtc));
            }

            return entries
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public Task DownloadAsync(string path, Stream destination, CancellationToken ct) =>
            client.DownloadFileAsync(RemotePath.Normalize(path), destination, ct);

        public Task UploadAsync(string path, Stream source, CancellationToken ct) =>
            client.UploadFileAsync(source, RemotePath.Normalize(path), true, null, ct);

        public Task CreateDirectoryAsync(string path, CancellationToken ct) =>
            client.CreateDirectoryAsync(RemotePath.Normalize(path), ct);

        public Task RenameAsync(
            string path,
            string newPath,
            bool isDirectory,
            CancellationToken ct) =>
            client.RenameFileAsync(
                RemotePath.Normalize(path), RemotePath.Normalize(newPath), ct);

        public Task DeleteAsync(string path, bool isDirectory, CancellationToken ct) =>
            isDirectory
                ? client.DeleteDirectoryAsync(RemotePath.Normalize(path), ct)
                : client.DeleteFileAsync(RemotePath.Normalize(path), ct);

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            forwarded?.Dispose();
            if (jumpSession is not null)
                return jumpSession.DisposeAsync();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ForwardedStream(
        TcpClient tcp,
        ForwardedPortLocal forwarded) : Stream
    {
        private readonly NetworkStream stream = tcp.GetStream();

        public override bool CanRead => stream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => stream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            stream.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            stream.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) =>
            stream.Write(buffer, offset, count);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            stream.WriteAsync(buffer, cancellationToken);

        public override void Flush() => stream.Flush();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stream.Dispose();
                tcp.Dispose();
                try
                {
                    forwarded.Dispose();
                }
                catch
                {
                    // The client may already be gone; nothing else to release.
                }
            }

            base.Dispose(disposing);
        }
    }

    private static string Truncate(string value, int maxChars) =>
        value.Length <= maxChars ? value : "[output troncato]\n" + value[^maxChars..];
}

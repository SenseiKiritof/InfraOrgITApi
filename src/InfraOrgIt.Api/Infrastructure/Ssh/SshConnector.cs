using System.Security.Cryptography;
using System.Text;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace InfraOrgIt.Api.Infrastructure.Ssh;

public sealed class SshConnector(
    CredentialCipher cipher,
    IOptions<ServerAccessOptions> options,
    InfraOrgItDbContext db,
    ILogger<SshConnector> logger) : ISshConnector
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public Task<ISshSession> ConnectAsync(Server server, CancellationToken ct) =>
        ConnectInternalAsync(server, new HashSet<int>(), ct);

    private async Task<ISshSession> ConnectInternalAsync(
        Server server,
        HashSet<int> visited,
        CancellationToken ct)
    {
        if (!visited.Add(server.Id))
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "jump_cycle",
                "La catena dei server proxy è ciclica.");

        var host = string.IsNullOrWhiteSpace(server.IpAddress)
            ? server.Hostname
            : server.IpAddress;

        if (server.JumpServerId is not int jumpId)
            return new SshSession(
                await ConnectClientAsync(server, host, server.SshPort, ct),
                options.Value.MaxOutputBytes);

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

        var forwarded = new ForwardedPortLocal(
            "127.0.0.1", host, (uint)server.SshPort);
        try
        {
            tunnelHost.Client.AddForwardedPort(forwarded);
            forwarded.Start();

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

        var auth = BuildAuthentication(credential);
        var connection = new Renci.SshNet.ConnectionInfo(host, port, credential.Username, auth)
        {
            Timeout = ConnectTimeout
        };

        var client = new SshClient(connection);
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

            logger.LogWarning(exception, "Connessione SSH fallita per il server {ServerId}", server.Id);
            throw new ApiException(
                StatusCodes.Status502BadGateway,
                "ssh_unreachable",
                $"Connessione SSH a {server.Name} fallita: {exception.Message}");
        }

        server.HostKeyFingerprint ??= received;
        return client;
    }

    private AuthenticationMethod BuildAuthentication(ServerCredential credential)
    {
        string secret;
        try
        {
            secret = cipher.Decrypt(credential.EncryptedSecret, credential.KeyVersion);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "credential_rejected",
                $"La credenziale {credential.Name} non è decifrabile: reinserire il segreto.");
        }

        if (credential.AuthType == "Password")
            return new PasswordAuthenticationMethod(credential.Username, secret);

        string? passphrase = null;
        if (!string.IsNullOrEmpty(credential.EncryptedPassphrase))
        {
            try
            {
                passphrase = cipher.Decrypt(credential.EncryptedPassphrase, credential.KeyVersion);
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                throw new ApiException(
                    StatusCodes.Status409Conflict,
                    "credential_rejected",
                    $"La passphrase della credenziale {credential.Name} non è decifrabile.");
            }
        }

        try
        {
            return new PrivateKeyAuthenticationMethod(
                credential.Username,
                new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(secret)), passphrase));
        }
        catch (Exception exception)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "credential_rejected",
                $"La chiave privata della credenziale {credential.Name} non è valida: {exception.Message}");
        }
    }

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

    private static string Truncate(string value, int maxChars) =>
        value.Length <= maxChars ? value : "[output troncato]\n" + value[^maxChars..];
}

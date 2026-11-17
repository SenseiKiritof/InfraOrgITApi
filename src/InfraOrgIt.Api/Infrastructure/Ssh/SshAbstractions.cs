using InfraOrgIt.Api.Infrastructure.Files;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;

namespace InfraOrgIt.Api.Infrastructure.Ssh;

public sealed record SshCommandResult(int? ExitCode, string Output, bool TimedOut = false);

public interface ISshConnector
{
    Task<ISshSession> ConnectAsync(Server server, CancellationToken ct);

    /// <summary>Apre una sessione SFTP sulla stessa catena di proxy dell'SSH.</summary>
    Task<IFileTransferSession> OpenSftpAsync(
        Server server,
        ServerCredential credential,
        int port,
        CancellationToken ct);
}

public interface ISshSession : IAsyncDisposable
{
    Task<SshCommandResult> RunAsync(string script, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Runs a script through sudo. <paramref name="password"/> is written to
    /// stdin and never appears in the command; null uses sudo -n (NOPASSWD).
    /// </summary>
    Task<SshCommandResult> RunSudoAsync(
        string script,
        string? password,
        TimeSpan timeout,
        CancellationToken ct) =>
        throw new NotSupportedException(
            "Questa sessione SSH non supporta l'esecuzione sudo.");
    Task<IShellSession> OpenShellAsync(uint columns, uint rows, CancellationToken ct);

    /// <summary>
    /// Opens a loopback TCP stream forwarded to <paramref name="host"/>:
    /// <paramref name="port"/> through this SSH session. Used by the server
    /// relay; the caller owns and disposes the returned stream. Test fakes
    /// that only exercise console/actions keep the default failure.
    /// </summary>
    Task<Stream> OpenForwardAsync(string host, int port, CancellationToken ct) =>
        throw new NotSupportedException("Forward SSH non supportato.");
}

public interface IShellSession : IAsyncDisposable
{
    Stream Stream { get; }
    void Resize(uint columns, uint rows);
}

using InfraOrgIt.Api.Infrastructure.Persistence.Entities;

namespace InfraOrgIt.Api.Infrastructure.Ssh;

public sealed record SshCommandResult(int? ExitCode, string Output, bool TimedOut = false);

public interface ISshConnector
{
    Task<ISshSession> ConnectAsync(Server server, CancellationToken ct);
}

public interface ISshSession : IAsyncDisposable
{
    Task<SshCommandResult> RunAsync(string script, TimeSpan timeout, CancellationToken ct);
    Task<IShellSession> OpenShellAsync(uint columns, uint rows, CancellationToken ct);
}

public interface IShellSession : IAsyncDisposable
{
    Stream Stream { get; }
    void Resize(uint columns, uint rows);
}

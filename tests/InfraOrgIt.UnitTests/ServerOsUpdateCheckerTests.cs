using InfraOrgIt.Api.Features.Inventory;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraOrgIt.UnitTests;

public sealed class ServerOsUpdateCheckerTests
{
    private const string Output = """
        NAME="Ubuntu"
        ID=ubuntu
        PRETTY_NAME="Ubuntu 24.04.3 LTS"
        VERSION_ID="24.04"
        VERSION_CODENAME=noble
        __INFRAORGIT_OS__
        New release '26.04 LTS' available.
        """;

    [Fact]
    public async Task Uses_the_catalog_target_and_eol()
    {
        var checker = Checker(
            new OsCatalogResult("ubuntu", "26.04", true,
                new DateOnly(2029, 5, 31), null));
        var result = await checker.CheckAsync(new Server { Id = 1 }, true,
            CancellationToken.None);
        Assert.Equal("Ok", result.Status);
        Assert.Equal("Ubuntu 24.04.3 LTS", result.OsName);
        Assert.Equal("24.04", result.OsVersion);
        Assert.Equal("26.04", result.Target);
        Assert.True(result.TargetLts);
        Assert.Equal(new DateOnly(2029, 5, 31), result.EolDate);
    }

    [Fact]
    public async Task Reports_non_lts_when_lts_is_required()
    {
        var checker = Checker(
            new OsCatalogResult("ubuntu", null, false, null,
                "Disponibile solo 25.10 (non-LTS)"),
            Output.Replace("26.04 LTS", "25.10"));
        var result = await checker.CheckAsync(new Server { Id = 1 }, true,
            CancellationToken.None);
        Assert.Equal("NonLts", result.Status);
        Assert.Null(result.Target);
        Assert.Contains("25.10", result.Detail);
    }

    [Fact]
    public async Task Marks_an_unknown_distro_as_unsupported()
    {
        var checker = Checker(
            new OsCatalogResult("ubuntu", null, false, null, null),
            """
            NAME="Unknown"
            ID=unknown
            VERSION_ID="1"
            """);
        var result = await checker.CheckAsync(new Server { Id = 1 }, true,
            CancellationToken.None);
        Assert.Equal("Unsupported", result.Status);
    }

    private static ServerOsUpdateChecker Checker(
        OsCatalogResult? catalogResult, string output = Output) =>
        new(new FakeSsh(output), new FakeCatalog(catalogResult),
            NullLogger<ServerOsUpdateChecker>.Instance);

    private sealed class FakeCatalog(OsCatalogResult? result) : IEndOfLifeCatalog
    {
        public Task<OsCatalogResult?> LookupAsync(
            OsReleaseInfo info, bool ltsOnly, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class FakeSsh(string output) : ISshConnector
    {
        public Task<ISshSession> ConnectAsync(Server server, CancellationToken ct) =>
            Task.FromResult<ISshSession>(new Session(output));

        public Task<InfraOrgIt.Api.Infrastructure.Files.IFileTransferSession> OpenSftpAsync(
            Server server, ServerCredential credential, int port, CancellationToken ct) =>
            throw new NotSupportedException();

        private sealed class Session(string output) : ISshSession
        {
            public Task<SshCommandResult> RunAsync(
                string script, TimeSpan timeout, CancellationToken ct) =>
                Task.FromResult(new SshCommandResult(0, output));

            public Task<SshCommandResult> RunSudoAsync(
                string script, string? password, TimeSpan timeout, CancellationToken ct) =>
                Task.FromResult(new SshCommandResult(0, output));

            public Task<IShellSession> OpenShellAsync(
                uint columns, uint rows, CancellationToken ct) =>
                throw new NotSupportedException();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

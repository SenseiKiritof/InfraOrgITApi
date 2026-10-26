using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.LegacyImport;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MySql;

namespace InfraOrgIt.IntegrationTests;

public sealed class LegacyImportTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("infraorgit")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        var options = new DbContextOptionsBuilder<InfraOrgItDbContext>()
            .UseMySQL(_mysql.GetConnectionString())
            .Options;
        await using var db = new InfraOrgItDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _mysql.DisposeAsync();

    [Fact]
    public async Task Dry_run_reports_without_writing()
    {
        var report = await new Importer().RunAsync(Options(apply: false), CancellationToken.None);

        Assert.True(report.DryRun);
        Assert.Empty(report.Errors);
        Assert.Contains(report.Tables, x => x.Table == "Servers" && x.Read == 2);
        Assert.Equal(1, report.NeedsReauth);
        Assert.Equal(0, report.Tables.Sum(x => x.Inserted));
    }

    [Fact]
    public async Task Apply_preserves_ids_deferred_links_and_audit_redaction()
    {
        var report = await new Importer().RunAsync(Options(apply: true), CancellationToken.None);
        Assert.Empty(report.Errors);
        Assert.Equal(1, report.RedactedAudit);

        var options = new DbContextOptionsBuilder<InfraOrgItDbContext>()
            .UseMySQL(_mysql.GetConnectionString())
            .Options;
        await using var db = new InfraOrgItDbContext(options);

        Assert.Equal("Provider Import",
            (await db.Providers.SingleAsync(x => x.Id == 8101)).Name);
        Assert.Equal(2, await db.Servers.CountAsync(x => x.ProviderEnvironmentId == 8201));
        Assert.Equal(9001,
            (await db.ServerApplications.SingleAsync(x => x.Id == 8901)).ProxyRouteId);
        Assert.Equal(9201,
            (await db.ApplicationActions.SingleAsync(x => x.Id == 9301)).CommandTemplateId);
        Assert.Equal(3, await db.DiagramNodes.CountAsync(x => x.DiagramId == 9101));
        Assert.Equal("s-left",
            (await db.DiagramEdges.SingleAsync(x => x.Id == 9106)).SourceHandle);

        var audit = await db.CommandExecutions.SingleAsync(x => x.Id == 9501);
        Assert.Equal("", audit.Script);
        Assert.Equal("", audit.Output);

        var credential = await db.ServerCredentials.SingleAsync(x => x.Id == 9401);
        Assert.True(credential.NeedsReauth);
        Assert.Equal("", credential.EncryptedSecret);

        var second = await new Importer().RunAsync(Options(apply: true), CancellationToken.None);
        Assert.Empty(second.Errors);
        Assert.All(second.Tables, table => Assert.Equal(0, table.Inserted));
        Assert.All(second.Tables.Where(table => table.Read > 0),
            table => Assert.Equal(table.Read, table.Skipped));
    }

    private ImportOptions Options(bool apply) => new(
        Path.Combine(AppContext.BaseDirectory, "Legacy", "Fixtures", "import-rehearsal.json"),
        Path.Combine(Path.GetTempPath(), $"legacy-import-{Guid.NewGuid():N}.json"),
        apply ? _mysql.GetConnectionString() : null,
        apply,
        null,
        null,
        true);
}

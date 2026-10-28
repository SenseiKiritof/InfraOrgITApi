using System.Security.Cryptography;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.LegacyImport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;

namespace InfraOrgIt.IntegrationTests;

public sealed class LegacyMySqlSourceTests : IAsyncLifetime
{
    private readonly MySqlContainer _target = new MySqlBuilder("mysql:8.4")
        .WithDatabase("infraorgit")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly MySqlContainer _legacy = new MySqlBuilder("mysql:8.4")
        .WithDatabase("infraorgit_legacy")
        .WithUsername("legacy")
        .WithPassword("legacy")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_legacy.StartAsync(), _target.StartAsync());

        var options = new DbContextOptionsBuilder<InfraOrgItDbContext>()
            .UseMySQL(_target.GetConnectionString())
            .Options;
        await using var db = new InfraOrgItDbContext(options);
        await db.Database.MigrateAsync();

        await using var connection = new MySqlConnection(_legacy.GetConnectionString());
        await connection.OpenAsync();
        foreach (var file in new[] { "01-schema.sql", "02-data.sql" })
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(
                AppContext.BaseDirectory, "Legacy", "Sql", file));
            new MySqlScript(connection, sql).Execute();
        }
    }

    public async Task DisposeAsync()
    {
        await _target.DisposeAsync();
        await _legacy.DisposeAsync();
    }

    [Fact]
    public async Task Apply_from_legacy_mysql_reencrypts_and_is_idempotent()
    {
        var oldKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var newKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await SetLegacySecretAsync(new LegacyCipher(oldKey).Encrypt("s3cret-fittizio"));

        var report = await new Importer().RunAsync(
            Options(oldKey, newKey), CancellationToken.None);

        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
        Assert.False(report.RolledBack);
        Assert.Equal(0, report.NeedsReauth);
        Assert.Equal(1, report.RedactedAudit);
        Assert.All(report.Tables, table => Assert.True(table.Read > 0, table.Table));
        Assert.Equal(report.Tables.Sum(x => x.Read), report.Tables.Sum(x => x.Inserted));

        await using var db = Db();
        Assert.Equal("Rehearsal",
            (await db.EnvironmentDefinitions.SingleAsync(x => x.Id == 7101)).Name);
        Assert.Equal(2, await db.Servers.CountAsync(x => x.ProviderEnvironmentId == 7301));
        Assert.Equal(7941,
            (await db.ServerApplications.SingleAsync(x => x.Id == 7931)).ProxyRouteId);
        Assert.Equal(7501, (await db.Menus.SingleAsync(x => x.Id == 7502)).ParentId);
        Assert.Equal(2, await db.DiagramNodes.CountAsync(x => x.DiagramId == 7961));
        Assert.Equal("right",
            (await db.DiagramEdges.SingleAsync(x => x.Id == 7991)).SourceHandle);

        var audit = await db.CommandExecutions.SingleAsync(x => x.Id == 7999);
        Assert.Equal("", audit.Script);
        Assert.Equal("", audit.Output);

        var credential = await db.ServerCredentials.SingleAsync(x => x.Id == 7901);
        Assert.False(credential.NeedsReauth);
        Assert.Equal(1, credential.KeyVersion);
        Assert.Equal("s3cret-fittizio",
            SSecureUtils.Utils.SecureUtils.Decrypt(credential.EncryptedSecret, newKey));

        var second = await new Importer().RunAsync(
            Options(oldKey, newKey), CancellationToken.None);
        Assert.Empty(second.Errors);
        Assert.All(second.Tables, table => Assert.Equal(0, table.Inserted));
        Assert.All(second.Tables.Where(table => table.Read > 0),
            table => Assert.Equal(table.Read, table.Skipped));
    }

    private ImportOptions Options(string oldKey, string newKey) => new(
        InputPath: "mysql:legacy",
        ReportPath: Path.Combine(Path.GetTempPath(), $"legacy-import-{Guid.NewGuid():N}.json"),
        ConnectionString: _target.GetConnectionString(),
        Apply: true,
        OldKey: oldKey,
        NewKey: newKey,
        RedactAudit: true,
        NewKeyVersion: 1,
        AllowReauth: false,
        SourceConnection: _legacy.GetConnectionString());

    private InfraOrgItDbContext Db() => new(
        new DbContextOptionsBuilder<InfraOrgItDbContext>()
            .UseMySQL(_target.GetConnectionString())
            .Options);

    private async Task SetLegacySecretAsync(string ciphertext)
    {
        await using var connection = new MySqlConnection(_legacy.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "UPDATE `ServerCredentials` SET `EncryptedSecret` = @secret WHERE `Id` = 7901",
            connection);
        command.Parameters.AddWithValue("@secret", ciphertext);
        await command.ExecuteNonQueryAsync();
    }
}

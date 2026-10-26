using System.Security.Cryptography;
using System.Text.Json.Nodes;
using InfraOrgIt.Api.BuildingBlocks.Security;
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
        Assert.False(report.RolledBack);
        Assert.Contains(report.Tables, x => x.Table == "Servers" && x.Read == 2);
        Assert.Equal(1, report.NeedsReauth);
        Assert.Equal(0, report.Tables.Sum(x => x.Inserted));
    }

    [Fact]
    public async Task Apply_preserves_ids_deferred_links_and_audit_redaction()
    {
        var report = await new Importer().RunAsync(
            Options(apply: true, allowReauth: true), CancellationToken.None);

        Assert.Empty(report.Errors);
        Assert.False(report.RolledBack);
        Assert.Equal(1, report.RedactedAudit);

        await using var db = Db();

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

        var second = await new Importer().RunAsync(
            Options(apply: true, allowReauth: true), CancellationToken.None);
        Assert.Empty(second.Errors);
        Assert.All(second.Tables, table => Assert.Equal(0, table.Inserted));
        Assert.All(second.Tables.Where(table => table.Read > 0),
            table => Assert.Equal(table.Read, table.Skipped));
    }

    [Fact]
    public async Task Apply_without_keys_requires_allow_reauth()
    {
        var report = await new Importer().RunAsync(
            Options(apply: true), CancellationToken.None);

        Assert.Contains(report.Errors, error => error.Contains("NeedsReauth"));
        Assert.Equal(1, report.NeedsReauth);
        Assert.Equal(0, report.Tables.Sum(x => x.Inserted));

        await using var db = Db();
        Assert.False(await db.Providers.AnyAsync());
    }

    [Fact]
    public async Task Apply_reencrypts_credentials_with_the_new_key()
    {
        var oldKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var newKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var legacy = new LegacyCipher(oldKey);
        var input = WriteFixture(fixture =>
            fixture["ServerCredentials"]![0]!["EncryptedSecret"] = legacy.Encrypt("s3cret"));

        var report = await new Importer().RunAsync(
            Options(apply: true, oldKey: oldKey, newKey: newKey, input: input),
            CancellationToken.None);

        Assert.Empty(report.Errors);
        Assert.Equal(0, report.NeedsReauth);

        await using var db = Db();
        var credential = await db.ServerCredentials.SingleAsync(x => x.Id == 9401);
        Assert.False(credential.NeedsReauth);
        Assert.Equal(1, credential.KeyVersion);

        var cipher = new CredentialCipher(Microsoft.Extensions.Options.Options.Create(new ServerAccessOptions
        {
            CurrentKeyVersion = 1,
            CredentialKeys = new Dictionary<string, string> { ["1"] = newKey }
        }));
        Assert.Equal("s3cret", cipher.Decrypt(credential.EncryptedSecret, credential.KeyVersion));
    }

    [Fact]
    public async Task Identity_collision_blocks_apply_and_rolls_back()
    {
        var input = WriteFixture(fixture =>
        {
            fixture["Roles"] = new JsonArray(new JsonObject
            {
                ["Id"] = 1,
                ["Name"] = "SystemAdmin",
                ["Label"] = "Legacy admin",
                ["ScopeEnum"] = "System"
            });
            fixture["Menus"] = new JsonArray(
                new JsonObject
                {
                    ["Id"] = 9701,
                    ["Name"] = "Legacy parent",
                    ["Path"] = "/legacy-parent",
                    ["IsActive"] = true,
                    ["HideBackButton"] = true,
                    ["IsVisible"] = true,
                    ["Order"] = 1,
                    ["ParentId"] = null
                },
                new JsonObject
                {
                    ["Id"] = 5,
                    ["Name"] = "Legacy Servers",
                    ["Path"] = "/legacy-servers",
                    ["IsActive"] = true,
                    ["HideBackButton"] = true,
                    ["IsVisible"] = true,
                    ["Order"] = 1,
                    ["ParentId"] = 9701
                });
            fixture["Groups"] = new JsonArray(new JsonObject
            {
                ["Id"] = 8801,
                ["Name"] = "Legacy group"
            });
            fixture["ProviderEnvironmentGroupRoles"] = new JsonArray(new JsonObject
            {
                ["Id"] = 8802,
                ["ProviderEnvironmentId"] = 8201,
                ["GroupId"] = 8801,
                ["RoleId"] = 1
            });
        });

        var report = await new Importer().RunAsync(
            Options(apply: true, allowReauth: true, input: input), CancellationToken.None);

        Assert.True(report.RolledBack);
        Assert.Contains(report.Errors, error => error.Contains("Id già usato"));
        Assert.All(report.Tables, table => Assert.Equal(0, table.Inserted));

        await using var db = Db();
        Assert.Equal("ServerAdmin", (await db.Roles.SingleAsync(x => x.Id == 1)).Name);
        Assert.False(await db.Groups.AnyAsync(x => x.Id == 8801));
        Assert.False(await db.ProviderEnvironmentGroupRoles.AnyAsync(x => x.Id == 8802));

        var menu = await db.Menus.SingleAsync(x => x.Id == 5);
        Assert.Equal("/servers", menu.Path);
        Assert.Null(menu.ParentId);
    }

    [Fact]
    public async Task Deferred_orphan_is_rejected_by_preflight()
    {
        var input = WriteFixture(fixture =>
            fixture["ServerApplications"]![0]!["ProxyRouteId"] = 9999);

        var dryRun = await new Importer().RunAsync(
            Options(apply: false, input: input), CancellationToken.None);
        Assert.Contains(dryRun.Errors, error => error.Contains("ProxyRouteId=9999"));
        Assert.Equal(0, dryRun.Tables.Sum(x => x.Inserted));

        var apply = await new Importer().RunAsync(
            Options(apply: true, allowReauth: true, input: input), CancellationToken.None);
        Assert.NotEmpty(apply.Errors);
        Assert.Equal(0, apply.Tables.Sum(x => x.Inserted));

        await using var db = Db();
        Assert.False(await db.Providers.AnyAsync());
    }

    private InfraOrgItDbContext Db() => new(
        new DbContextOptionsBuilder<InfraOrgItDbContext>()
            .UseMySQL(_mysql.GetConnectionString())
            .Options);

    private ImportOptions Options(
        bool apply,
        bool allowReauth = false,
        string? oldKey = null,
        string? newKey = null,
        string? input = null) => new(
        input ?? FixturePath,
        Path.Combine(Path.GetTempPath(), $"legacy-import-{Guid.NewGuid():N}.json"),
        apply ? _mysql.GetConnectionString() : null,
        apply,
        oldKey,
        newKey,
        true,
        1,
        allowReauth);

    private static string WriteFixture(Action<JsonObject> mutate)
    {
        var fixture = JsonNode.Parse(File.ReadAllText(FixturePath))!.AsObject();
        mutate(fixture);
        var path = Path.Combine(Path.GetTempPath(), $"legacy-fixture-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, fixture.ToJsonString());
        return path;
    }

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Legacy", "Fixtures", "import-rehearsal.json");
}

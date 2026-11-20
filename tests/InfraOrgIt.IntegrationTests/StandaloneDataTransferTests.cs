using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InfraOrgIt.IntegrationTests;

public sealed class StandaloneDataTransferTests : IAsyncLifetime
{
    private string _directory = "";
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            $"infraorgit-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _factory = NewFactory(Path.Combine(_directory, "source.db"));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // best effort on a temporary directory
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Inventory_export_imports_into_a_fresh_sqlite_database()
    {
        using var source = _factory.CreateClient();
        await LocalLoginAsync(source);
        Assert.Equal(
            HttpStatusCode.OK,
            (await source.GetAsync("/health/ready")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var provider = new Provider { Name = "Acme Standalone" };
            var environment = new EnvironmentDefinition
            {
                Name = "PROD",
                Description = "Standalone test",
            };
            db.Providers.Add(provider);
            db.EnvironmentDefinitions.Add(environment);
            await db.SaveChangesAsync();
            var providerEnvironment = new ProviderEnvironment
            {
                ProviderId = provider.Id,
                EnvironmentDefinitionId = environment.Id,
            };
            db.ProviderEnvironments.Add(providerEnvironment);
            await db.SaveChangesAsync();
            db.Servers.Add(new Server
            {
                ProviderEnvironmentId = providerEnvironment.Id,
                Name = "prd-app-01",
                IpAddress = "10.0.0.10",
                Hostname = "prd-app-01.acme.example",
                SshPort = 22,
            });
            db.ApplicationTypes.Add(new ApplicationType
            {
                Name = "Apache",
                Label = "Apache HTTP Server",
                SortOrder = 130,
            });
            await db.SaveChangesAsync();
            db.Applications.Add(new Application
            {
                Name = "edge-proxy",
                BasePath = "/edge",
                ListeningOnPort = 80,
                Type = "Apache",
            });
            await db.SaveChangesAsync();
        }

        var exported = await source.GetAsync("/api/data/export");
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        var bytes = await exported.Content.ReadAsByteArrayAsync();
        Assert.Contains("Acme Standalone", Encoding.UTF8.GetString(bytes));

        using var targetFactory = NewFactory(Path.Combine(_directory, "target.db"));
        using var target = targetFactory.CreateClient();
        await LocalLoginAsync(target);
        var targetCsrf = await CsrfAsync(target);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", "inventory.json");
        using var previewRequest = new HttpRequestMessage(
            HttpMethod.Post, "/api/data/import") { Content = form };
        previewRequest.Headers.Add("X-CSRF-Token", targetCsrf);
        previewRequest.Headers.Add("Origin", "http://localhost");
        var preview = await target.SendAsync(previewRequest);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        var token = previewJson.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        using var confirm = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/data/import/confirm")
        {
            Content = JsonContent.Create(new { token, mode = "replace" }),
        };
        confirm.Headers.Add("X-CSRF-Token", targetCsrf);
        confirm.Headers.Add("Origin", "http://localhost");
        var applied = await target.SendAsync(confirm);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);

        using var verify = targetFactory.Services.CreateScope();
        var targetDb = verify.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        Assert.Equal(1, await targetDb.Providers.CountAsync());
        Assert.Equal(1, await targetDb.Servers.CountAsync());
        Assert.Equal("prd-app-01", (await targetDb.Servers.SingleAsync()).Name);
        Assert.Equal(16, await targetDb.ApplicationTypes.CountAsync());
        Assert.True(await targetDb.ApplicationTypes.AnyAsync(x => x.Name == "Apache"));
        Assert.Equal("Apache", (await targetDb.Applications.SingleAsync()).Type);
    }

    [Fact]
    public async Task Import_rejects_a_document_without_data()
    {
        using var client = _factory.CreateClient();
        await LocalLoginAsync(client);
        var csrf = await CsrfAsync(client);
        var exported = await client.GetAsync("/api/data/export");
        var node = System.Text.Json.Nodes.JsonNode.Parse(
            await exported.Content.ReadAsStringAsync())!;
        node.AsObject().Remove("data");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json"),
            "file", "missing-data.json");
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/data/import") { Content = form };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("Origin", "http://localhost");
        var preview = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, preview.StatusCode);
    }

    [Fact]
    public async Task Import_rejects_a_tampered_checksum()
    {
        using var client = _factory.CreateClient();
        await LocalLoginAsync(client);
        var csrf = await CsrfAsync(client);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            db.Providers.Add(new Provider { Name = "Checksum source" });
            await db.SaveChangesAsync();
        }
        var exported = await client.GetAsync("/api/data/export");
        var json = await exported.Content.ReadAsStringAsync();
        json = json.Replace("\"checksum\": \"", "\"checksum\": \"00");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(json, Encoding.UTF8, "application/json"), "file", "bad.json");
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/data/import") { Content = form };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("Origin", "http://localhost");
        var preview = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, preview.StatusCode);
    }

    private static WebApplicationFactory<Program> NewFactory(string database) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={database}");
            builder.UseSetting("Local:Enabled", "true");
            builder.UseSetting("Local:BootstrapSecret", "test-bootstrap-secret-123456");
            builder.UseSetting("Local:Username", "local");
            builder.UseSetting("Local:BackupDirectory", Path.Combine(
                Path.GetDirectoryName(database)!, "backups"));
            builder.UseSetting("Auth:SecureCookies", "false");
            builder.UseSetting("Auth:BootstrapAdminUsername", "local");
            builder.UseSetting("Frontend:BaseUrl", "http://localhost");
            builder.UseSetting("Mcp:Enabled", "false");
            builder.UseSetting("ServerAccess:CurrentKeyVersion", "1");
            builder.UseSetting("ServerAccess:CredentialKeys:1",
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray()));
        });

    private static async Task LocalLoginAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/local/session");
        request.Headers.Add("X-InfraOrgIT-Local", "test-bootstrap-secret-123456");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/session");
        return session.GetProperty("csrfToken").GetString()!;
    }
}

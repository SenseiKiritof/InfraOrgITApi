using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace InfraOrgIt.IntegrationTests;

public sealed class ApplicationTypesApiTests : IAsyncLifetime
{
    private string _directory = "";
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            $"infraorgit-app-types-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _factory = NewFactory(Path.Combine(_directory, "types.db"));
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
    public async Task Application_types_are_crud_and_accept_a_new_code()
    {
        using var client = _factory.CreateClient();
        await LocalLoginAsync(client);
        var csrf = await CsrfAsync(client);

        var defaults = await client.GetFromJsonAsync<JsonElement>("/api/application-types");
        Assert.Equal(12, defaults.GetArrayLength());
        Assert.Contains(
            defaults.EnumerateArray(),
            item => item.GetProperty("name").GetString() == "HAProxy");

        var created = await SendAsync(
            client, csrf, HttpMethod.Post, "/api/application-types",
            new { name = "Apache", label = "Apache HTTP Server", sortOrder = 130 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var createdId = createdBody.GetProperty("id").GetInt32();

        var duplicate = await SendAsync(
            client, csrf, HttpMethod.Post, "/api/application-types",
            new { name = "apache", label = "Duplicate", sortOrder = 131 });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var invalid = await SendAsync(
            client, csrf, HttpMethod.Post, "/api/application-types",
            new { name = "Bad Type", label = "Invalid", sortOrder = 132 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

        var application = await SendAsync(
            client, csrf, HttpMethod.Post, "/api/applications",
            new
            {
                name = "apache-edge",
                description = "",
                basePath = "/edge",
                listeningOnPort = 8080,
                type = "apache"
            });
        Assert.Equal(HttpStatusCode.OK, application.StatusCode);
        var applicationBody = await application.Content.ReadFromJsonAsync<JsonElement>();
        var applicationId = applicationBody.GetProperty("id").GetInt32();
        Assert.Equal("Apache", applicationBody.GetProperty("type").GetString());

        var rename = await SendAsync(
            client, csrf, HttpMethod.Put, $"/api/application-types/{createdId}",
            new { name = "Httpd", label = "Apache httpd", sortOrder = 130 });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        var renamedId = (await rename.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt32();
        var reload = await client.GetFromJsonAsync<JsonElement>($"/api/applications/{applicationId}");
        Assert.Equal("Httpd", reload.GetProperty("type").GetString());

        var used = await SendAsync(
            client, csrf, HttpMethod.Delete, $"/api/application-types/{renamedId}", null);
        Assert.Equal(HttpStatusCode.Conflict, used.StatusCode);

        var unused = await SendAsync(
            client, csrf, HttpMethod.Post, "/api/application-types",
            new { name = "Unused", label = "Unused", sortOrder = 200 });
        var unusedId = (await unused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt32();
        var removed = await SendAsync(
            client, csrf, HttpMethod.Delete, $"/api/application-types/{unusedId}", null);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        var backend = await SendAsync(
            client, csrf, HttpMethod.Put, "/api/application-types/1",
            new { name = "Default", label = "Backend", sortOrder = 10 });
        Assert.Equal(HttpStatusCode.Conflict, backend.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string csrf,
        HttpMethod method,
        string path,
        object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("Origin", "http://localhost");
        if (body is not null)
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
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

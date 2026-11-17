using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InfraOrgIt.IntegrationTests;

public sealed class RealFtpTests : IAsyncLifetime
{
    private const int PassivePort = 21100;


    private readonly IContainer _ftp = new ContainerBuilder("fauria/vsftpd:latest")
        .WithEnvironment("FTP_USER", "test")
        .WithEnvironment("FTP_PASS", "testpass")
        .WithEnvironment("PASV_ADDRESS", "127.0.0.1")
        .WithEnvironment("PASV_MIN_PORT", PassivePort.ToString())
        .WithEnvironment("PASV_MAX_PORT", PassivePort.ToString())
        .WithPortBinding(21, true)
        .WithPortBinding(PassivePort, PassivePort)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(21))
        .Build();

    private string _connectionString = "";
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await TestMySql.CreateDatabaseAsync("RealFtpTests");
        await _ftp.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:MySql", _connectionString);
            builder.UseSetting("Auth:SecureCookies", "false");
            builder.UseSetting("Auth:BootstrapAdminUsername", "");
            builder.UseSetting("Oidc:Issuer", "http://localhost:8080/realms/test");
            builder.UseSetting("Oidc:ClientId", "test");
            builder.UseSetting("Oidc:ClientSecret", "test");
            builder.UseSetting("Oidc:RedirectUri", "http://localhost:3000/auth/callback");
            builder.UseSetting("Frontend:BaseUrl", "http://localhost:3000");
            builder.UseSetting("ServerAccess:CurrentKeyVersion", "1");
            builder.UseSetting("ServerAccess:CredentialKeys:1",
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray()));
            builder.UseSetting("ServerAccess:CommandTimeoutMinutes", "1");
        });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _ftp.DisposeAsync();
    }

    [Fact]
    public async Task Real_ftp_lists_downloads_uploads_and_deletes()
    {
        using var admin = await ClientForAsync("ftp.admin", systemAdmin: true);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var provider = new Provider { Name = "FTP Provider " + suffix };
        var environment = new EnvironmentDefinition { Name = "FTP Env " + suffix, Description = "" };
        db.Providers.Add(provider);
        db.EnvironmentDefinitions.Add(environment);
        await db.SaveChangesAsync();

        var providerEnvironment = new ProviderEnvironment
        {
            ProviderId = provider.Id,
            EnvironmentDefinitionId = environment.Id
        };
        db.ProviderEnvironments.Add(providerEnvironment);
        await db.SaveChangesAsync();

        var server = new Server
        {
            ProviderEnvironmentId = providerEnvironment.Id,
            Name = "FTP Server " + suffix,
            IpAddress = "127.0.0.1",
            Hostname = "localhost"
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();

        var credential = await ReadAsync(await SendAsync(admin, HttpMethod.Post,
            "/api/server-credentials", new
            {
                name = "FTP Credential " + suffix,
                username = "test",
                authType = "Password",
                secret = "testpass"
            }));
        await SendAsync(admin, HttpMethod.Put, $"/api/servers/{server.Id}/file-transfer", new
        {
            protocol = "Ftp",
            port = _ftp.GetMappedPublicPort(21),
            credentialId = credential.GetProperty("id").GetInt32()
        });
        await AssignServerAdminAsync(admin, providerEnvironment.Id, "ftp.admin");
        // PT-07: the role assignment revoked the old session; sign in again.
        await RefreshSessionAsync(admin, "ftp.admin", systemAdmin: true);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("contenuto ftp"), "file", "uploaded.txt");
        using var uploadRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/servers/{server.Id}/files/upload?path=/")
        {
            Content = form
        };
        uploadRequest.Headers.Add("X-CSRF-Token", "csrf");
        uploadRequest.Headers.Add("Origin", "http://localhost:3000");
        var upload = await admin.SendAsync(uploadRequest);
        Assert.True(
            upload.StatusCode == HttpStatusCode.OK,
            await upload.Content.ReadAsStringAsync());

        var listing = await ReadAsync(await admin.GetAsync(
            $"/api/servers/{server.Id}/files?path=/"));
        Assert.Contains(
            listing.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "uploaded.txt");

        var download = await admin.GetAsync(
            $"/api/servers/{server.Id}/files/download?path=/uploaded.txt");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Contains("contenuto ftp", await download.Content.ReadAsStringAsync());

        var folder = await SendAsync(
            admin,
            HttpMethod.Post,
            $"/api/servers/{server.Id}/files/directory",
            new { path = "/docs" });
        Assert.Equal(HttpStatusCode.OK, folder.StatusCode);

        var renamed = await SendAsync(
            admin,
            HttpMethod.Post,
            $"/api/servers/{server.Id}/files/rename",
            new { path = "/docs", newName = "docs2", isDirectory = true });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var removedFolder = await SendAsync(
            admin,
            HttpMethod.Delete,
            $"/api/servers/{server.Id}/files?path=/docs2&isDirectory=true");
        Assert.Equal(HttpStatusCode.NoContent, removedFolder.StatusCode);

        var removed = await SendAsync(
            admin,
            HttpMethod.Delete,
            $"/api/servers/{server.Id}/files?path=/uploaded.txt&isDirectory=false");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }

    private async Task AssignServerAdminAsync(
        HttpClient admin, int environmentId, string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var user = await db.Users.SingleAsync(x => x.Username == username);
        var group = new Group { Name = "FTP Group " + Guid.NewGuid().ToString("N")[..8] };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        db.GroupUsers.Add(new GroupUser { GroupsId = group.Id, UsersId = user.Id });
        var role = await db.Roles.SingleAsync(x => x.Name == "ServerAdmin");
        await db.SaveChangesAsync();

        var response = await SendAsync(admin, HttpMethod.Post,
            $"/api/provider-environments/{environmentId}/group-roles",
            new { groupId = group.Id, roleId = role.Id });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task RefreshSessionAsync(
        HttpClient client, string username, bool systemAdmin = false)
    {
        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var sessionId = sessions.Create(new SessionData(
            username, "Test", "User", null,
            DateTimeOffset.UtcNow.AddHours(1), null, "csrf",
            systemAdmin ? ["SystemAdmin"] : []));
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie",
            $"{SessionCookie.Session(false)}={sessionId}");
    }

    private async Task<HttpClient> ClientForAsync(string username, bool systemAdmin = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var user = new User { Username = username, Name = "Test", Surname = "User" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        if (systemAdmin)
        {
            var role = await db.Roles.SingleAsync(x => x.Name == "SystemAdmin");
            db.RoleUsers.Add(new RoleUser { RolesId = role.Id, UsersId = user.Id });
            await db.SaveChangesAsync();
        }

        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var sessionId = sessions.Create(new SessionData(
            username, "Test", "User", null,
            DateTimeOffset.UtcNow.AddHours(1), null, "csrf",
            systemAdmin ? ["SystemAdmin"] : []));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Session(false)}={sessionId}");
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-CSRF-Token", "csrf");
        request.Headers.Add("Origin", "http://localhost:3000");
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}

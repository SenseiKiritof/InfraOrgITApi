using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MySql;

namespace InfraOrgIt.IntegrationTests;

public sealed class TunnelApiTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("infraorgit")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:MySql", _mysql.GetConnectionString());
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
        });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>()
            .Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _mysql.DisposeAsync();
    }

    [Fact]
    public async Task Tunnel_targets_require_scope_and_operator_role()
    {
        var seeded = await SeedAsync();
        using var userSession = await SessionClientAsync("tunnel.user");
        await AddOperatorRoleAsync("tunnel.user", seeded.ProviderEnvironmentId);

        var created = await SendAsync(userSession, HttpMethod.Post,
            "/api/api-tokens",
            new { name = "tunnel", scopes = "tunnels:operate" });
        var token = (await ReadAsync(created)).GetProperty("token").GetString()!;

        using var tunnelClient = BearerClient(token);
        var targets = await tunnelClient.GetAsync("/api/tunnels/targets");
        Assert.Equal(HttpStatusCode.OK, targets.StatusCode);
        var payload = await ReadAsync(targets);
        var server = payload.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(seeded.ServerId, server.GetProperty("serverId").GetInt32());
        Assert.Equal("deploy", server.GetProperty("username").GetString());
        Assert.Equal("10.40.0.10", server.GetProperty("sshHost").GetString());
        Assert.Equal("10.40.0.9", server.GetProperty("jump").GetProperty("host").GetString());
        Assert.Equal("jump-user",
            server.GetProperty("jump").GetProperty("username").GetString());

        var target = server.GetProperty("targets").EnumerateArray().Single();
        Assert.Equal(seeded.DeploymentId, target.GetProperty("serverApplicationId").GetInt32());
        Assert.Equal(8081, target.GetProperty("targetPort").GetInt32());
        Assert.Equal(8081, target.GetProperty("suggestedLocalPort").GetInt32());

        var readOnly = await SendAsync(userSession, HttpMethod.Post,
            "/api/api-tokens",
            new { name = "read", scopes = "mcp:read" });
        var readOnlyToken = (await ReadAsync(readOnly)).GetProperty("token").GetString()!;
        using var readClient = BearerClient(readOnlyToken);
        var forbidden = await readClient.GetAsync("/api/tunnels/targets");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var start = await tunnelClient.PostAsJsonAsync("/api/tunnels/events", new
        {
            kind = "start",
            serverId = seeded.ServerId,
            serverApplicationId = seeded.DeploymentId,
            targetHost = "127.0.0.1",
            targetPort = 8081,
            localPort = 18081,
            correlationId = "run-1"
        });
        Assert.Equal(HttpStatusCode.NoContent, start.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var audit = await db.AuditEvents
                .SingleAsync(x => x.Action == "tunnel.start");
            Assert.Equal("TunnelStart", audit.Kind);
            Assert.Equal("Ok", audit.Outcome);
            Assert.Equal(seeded.ServerId, audit.ServerId);
            Assert.Equal(8081, audit.TargetPort);
            Assert.Equal(18081, audit.LocalPort);
            Assert.Equal("run-1", audit.CorrelationId);
            Assert.Equal("tunnel.user", audit.ActorUsername);
        }

        var outsiderSession = await SessionClientAsync("tunnel.outsider");
        var outsiderCreated = await SendAsync(outsiderSession, HttpMethod.Post,
            "/api/api-tokens",
            new { name = "outsider", scopes = "tunnels:operate" });
        var outsiderToken =
            (await ReadAsync(outsiderCreated)).GetProperty("token").GetString()!;
        using var outsider = BearerClient(outsiderToken);

        var empty = await outsider.GetAsync("/api/tunnels/targets");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Empty((await ReadAsync(empty)).GetProperty("items").EnumerateArray());

        var denied = await outsider.PostAsJsonAsync("/api/tunnels/events",
            new { kind = "start", serverId = seeded.ServerId });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var invalid = await tunnelClient.PostAsJsonAsync("/api/tunnels/events",
            new { kind = "maybe", serverId = seeded.ServerId });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
    }

    private async Task<SeededTunnel> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var provider = new Provider { Name = "Tunnel Provider" };
        var environment = new EnvironmentDefinition { Name = "Tunnel Env", Description = "" };
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

        var jumpCredential = Credential("jump-cred", "jump-user");
        var targetCredential = Credential("target-cred", "deploy");
        db.ServerCredentials.AddRange(jumpCredential, targetCredential);
        await db.SaveChangesAsync();

        var jump = new Server
        {
            ProviderEnvironmentId = providerEnvironment.Id,
            Name = "jump",
            IpAddress = "10.40.0.9",
            Hostname = "jump.local",
            SshPort = 2222,
            CredentialId = jumpCredential.Id
        };
        db.Servers.Add(jump);
        await db.SaveChangesAsync();

        var server = new Server
        {
            ProviderEnvironmentId = providerEnvironment.Id,
            Name = "target",
            IpAddress = "10.40.0.10",
            Hostname = "target.local",
            SshPort = 22,
            CredentialId = targetCredential.Id,
            JumpServerId = jump.Id
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();

        var application = new Application
        {
            Name = "Tunnel App",
            Description = "",
            BasePath = "/tunnel",
            ListeningOnPort = 8080
        };
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var version = new ApplicationVersion
        {
            ApplicationId = application.Id,
            VersionNumber = "1.0.0"
        };
        db.ApplicationVersions.Add(version);
        await db.SaveChangesAsync();

        var deploy = new ServerApplication
        {
            ServerId = server.Id,
            ApplicationVersionId = version.Id,
            ExposedPort = 8080,
            HostPort = 8081
        };
        db.ServerApplications.Add(deploy);
        await db.SaveChangesAsync();

        return new SeededTunnel(
            server.Id, providerEnvironment.Id, deploy.Id);
    }

    private static ServerCredential Credential(string name, string username) =>
        new()
        {
            Name = name,
            Username = username,
            AuthType = "Password",
            EncryptedSecret = "unused",
            KeyVersion = 1
        };

    private async Task AddOperatorRoleAsync(
        string username, int providerEnvironmentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var user = await db.Users.SingleAsync(x => x.Username == username);
        var role = await db.Roles.SingleAsync(x => x.Name == "ServerOperator");
        var group = new Group { Name = "Tunnel operators " + Guid.NewGuid().ToString("N")[..8] };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        db.GroupUsers.Add(new GroupUser { GroupsId = group.Id, UsersId = user.Id });
        db.ProviderEnvironmentGroupRoles.Add(new ProviderEnvironmentGroupRole
        {
            ProviderEnvironmentId = providerEnvironmentId,
            GroupId = group.Id,
            RoleId = role.Id
        });
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> SessionClientAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Username == username);
        if (user is null)
        {
            user = new User { Username = username, Name = "Test", Surname = "User" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var sessionId = sessions.Create(new SessionData(
            username, "Test", "User", null,
            DateTimeOffset.UtcNow.AddHours(1), null, "csrf", []));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie",
            $"{SessionCookie.Session(false)}={sessionId}");
        return client;
    }

    private HttpClient BearerClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, object? body = null)
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
        Assert.True(response.IsSuccessStatusCode,
            await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private sealed record SeededTunnel(
        int ServerId, int ProviderEnvironmentId, int DeploymentId);
}

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
using Testcontainers.MySql;

namespace InfraOrgIt.IntegrationTests;

public sealed class RealSshTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("infraorgit")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly IContainer _ssh = new ContainerBuilder("linuxserver/openssh-server:latest")
        .WithEnvironment("PUID", "1000")
        .WithEnvironment("PGID", "1000")
        .WithEnvironment("TZ", "Etc/UTC")
        .WithEnvironment("USER_NAME", "test")
        .WithEnvironment("USER_PASSWORD", "testpass")
        .WithEnvironment("PASSWORD_ACCESS", "true")
        .WithEnvironment("SUDO_ACCESS", "false")
        .WithPortBinding(2222, true)
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_mysql.StartAsync(), _ssh.StartAsync());

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
            builder.UseSetting("ServerAccess:CommandTimeoutMinutes", "1");
        });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await Task.WhenAll(_mysql.DisposeAsync().AsTask(), _ssh.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Real_ssh_run_rejects_changed_host_key_and_audits()
    {
        using var admin = await ClientForAsync("ssh.admin", systemAdmin: true);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var provider = new Provider { Name = "SSH Provider " + suffix };
        var environment = new EnvironmentDefinition
        {
            Name = "SSH Env " + suffix,
            Description = ""
        };
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
            Name = "SSH Server " + suffix,
            IpAddress = "127.0.0.1",
            Hostname = "localhost",
            SshPort = _ssh.GetMappedPublicPort(2222)
        };
        db.Servers.Add(server);

        var application = new Application
        {
            Name = "SSH App " + suffix,
            Description = "",
            BasePath = "/ssh",
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
            ExposedPort = 8080
        };
        db.ServerApplications.Add(deploy);
        await db.SaveChangesAsync();

        var credential = await ReadAsync(await SendAsync(admin, HttpMethod.Post,
            "/api/server-credentials", new
            {
                name = "SSH Credential " + suffix,
                username = "test",
                authType = "Password",
                secret = "testpass"
            }));
        await SendAsync(admin, HttpMethod.Put, $"/api/servers/{server.Id}/ssh", new
        {
            credentialId = credential.GetProperty("id").GetInt32(),
            sshPort = _ssh.GetMappedPublicPort(2222)
        });

        await AssignOperatorAsync(admin, providerEnvironment.Id, "ssh.admin");

        var template = await ReadAsync(await SendAsync(admin, HttpMethod.Post,
            "/api/command-templates", new
            {
                name = "SSH Template " + suffix,
                description = "",
                script = "echo ok"
            }));
        var action = await ReadAsync(await SendAsync(admin, HttpMethod.Post,
            $"/api/applications/{application.Id}/actions", new
            {
                label = "Echo",
                order = 1,
                commandTemplateId = template.GetProperty("id").GetInt32(),
                parameterValues = new Dictionary<string, string>(),
                requiresConfirmation = false
            }));

        var first = await SendAsync(admin, HttpMethod.Post,
            $"/api/server-applications/{deploy.Id}/actions/{action.GetProperty("id").GetInt32()}/run");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body = await ReadAsync(first);
        Assert.Equal(0, body.GetProperty("exitCode").GetInt32());
        Assert.Contains("ok", body.GetProperty("output").GetString());

        using (var verify = _factory.Services.CreateScope())
        {
            var verifyDb = verify.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var stored = await verifyDb.Servers.AsNoTracking().SingleAsync(x => x.Id == server.Id);
            Assert.False(string.IsNullOrWhiteSpace(stored.HostKeyFingerprint));
            var audit = await verifyDb.CommandExecutions.AsNoTracking()
                .Where(x => x.ServerId == server.Id && x.Kind == "Action")
                .OrderByDescending(x => x.Id)
                .FirstAsync();
            Assert.Equal("echo ok", audit.Script);
            Assert.Contains("ok", audit.Output);
        }

        using (var tamper = _factory.Services.CreateScope())
        {
            var tamperDb = tamper.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var stored = await tamperDb.Servers.SingleAsync(x => x.Id == server.Id);
            stored.HostKeyFingerprint = "SHA256:not-the-real-key";
            await tamperDb.SaveChangesAsync();
        }

        var rejected = await SendAsync(admin, HttpMethod.Post,
            $"/api/server-applications/{deploy.Id}/actions/{action.GetProperty("id").GetInt32()}/run");
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using var problem = await JsonDocument.ParseAsync(
            await rejected.Content.ReadAsStreamAsync());
        Assert.Equal("host_key_mismatch", problem.RootElement.GetProperty("code").GetString());
    }

    private async Task AssignOperatorAsync(HttpClient admin, int environmentId, string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var user = await db.Users.SingleAsync(x => x.Username == username);
        var group = new Group { Name = "SSH Group " + Guid.NewGuid().ToString("N")[..8] };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        db.GroupUsers.Add(new GroupUser { GroupsId = group.Id, UsersId = user.Id });
        var role = await db.Roles.SingleAsync(x => x.Name == "ServerOperator");
        await db.SaveChangesAsync();

        var response = await SendAsync(admin, HttpMethod.Post,
            $"/api/provider-environments/{environmentId}/group-roles",
            new { groupId = group.Id, roleId = role.Id });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

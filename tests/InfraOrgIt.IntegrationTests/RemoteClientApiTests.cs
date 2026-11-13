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

public sealed class RemoteClientApiTests : IAsyncLifetime
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
    public async Task Remote_target_console_audit_and_file_event()
    {
        var seeded = await SeedAsync();
        using var session = await SessionClientAsync("remote.user");
        await AddEnvironmentRoleAsync("remote.user", seeded.ProviderEnvironmentId, "ServerAdmin");

        var token = await CreateTokenAsync("remote.user", "tunnels:operate");
        using var client = BearerClient(token);

        var targetResponse = await client.GetAsync(
            $"/api/remote/targets/{seeded.ServerId}");
        Assert.Equal(HttpStatusCode.OK, targetResponse.StatusCode);
        var target = await ReadAsync(targetResponse);
        Assert.Equal("10.40.0.10", target.GetProperty("host").GetString());
        Assert.Equal("deploy", target.GetProperty("username").GetString());
        Assert.Equal("10.40.0.9", target.GetProperty("jumps")[0]
            .GetProperty("host").GetString());
        Assert.Equal("SHA256:test", target.GetProperty("hostKeyFingerprint").GetString());

        var start = await client.PostAsJsonAsync("/api/remote/console-sessions",
            new { serverId = seeded.ServerId, correlationId = "desktop-console-1" });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var executionId = (await ReadAsync(start))
            .GetProperty("executionId").GetInt32();

        var complete = await client.PostAsJsonAsync(
            $"/api/remote/console-sessions/{executionId}/complete",
            new { exitCode = 0, output = "client console output" });
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);

        var fileEvent = await client.PostAsJsonAsync("/api/remote/file-events",
            new
            {
                serverId = seeded.ServerId,
                operation = "Download",
                path = "/var/log/app.log"
            });
        Assert.Equal(HttpStatusCode.NoContent, fileEvent.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var execution = await db.CommandExecutions
                .SingleAsync(x => x.Id == executionId);
            Assert.Equal("Console", execution.Kind);
            Assert.Equal("Console desktop", execution.Label);
            Assert.Equal("client console output", execution.Output);
            Assert.NotNull(execution.EndedAt);

            var audit = await db.AuditEvents
                .SingleAsync(x => x.Kind == "FileTransfer");
            Assert.Equal("Download", audit.Action);
            Assert.Equal("Ok", audit.Outcome);
            Assert.Equal(seeded.ServerId, audit.ServerId);
            Assert.Equal("/var/log/app.log", audit.TargetHost);
        }
    }

    [Fact]
    public async Task Client_mode_blocks_server_side_files_but_allows_remote_target()
    {
        var seeded = await SeedAsync();
        using var session = await SessionClientAsync("remote.admin");
        await AddEnvironmentRoleAsync("remote.admin", seeded.ProviderEnvironmentId, "ServerAdmin");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var policy = await db.ConnectionPolicies.SingleAsync();
            policy.Mode = "Client";
            await db.SaveChangesAsync();
        }

        var files = await session.GetAsync($"/api/servers/{seeded.ServerId}/files");
        Assert.Equal(HttpStatusCode.Forbidden, files.StatusCode);

        var token = await CreateTokenAsync("remote.admin", "tunnels:operate");
        using var client = BearerClient(token);
        var target = await client.GetAsync($"/api/remote/targets/{seeded.ServerId}");
        Assert.Equal(HttpStatusCode.OK, target.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var policy = await db.ConnectionPolicies.SingleAsync();
            policy.Mode = "Auto";
            await db.SaveChangesAsync();
        }

        var autoFiles = await session.GetAsync($"/api/servers/{seeded.ServerId}/files");
        Assert.NotEqual(HttpStatusCode.Forbidden, autoFiles.StatusCode);
    }

    private async Task<SeededRemote> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var provider = new Provider { Name = $"Remote {Guid.NewGuid():N}" };
        var environment = new EnvironmentDefinition
        {
            Name = $"Remote env {Guid.NewGuid():N}",
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
            JumpServerId = jump.Id,
            HostKeyFingerprint = "SHA256:test"
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();

        var application = new Application
        {
            Name = "Remote App",
            Description = "",
            BasePath = "/remote",
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
        var action = new ApplicationAction
        {
            ApplicationId = application.Id,
            Label = "Health",
            Order = 1,
            CustomScript = "echo hello",
            ParameterValuesJson = "{}",
            RequiresConfirmation = false
        };
        db.ApplicationActions.Add(action);
        await db.SaveChangesAsync();

        return new SeededRemote(
            server.Id,
            providerEnvironment.Id,
            deploy.Id,
            action.Id);
    }

    [Fact]
    public async Task Action_intent_and_complete_use_the_client_execution_row()
    {
        var seeded = await SeedAsync();
        using var session = await SessionClientAsync("action.user");
        await AddEnvironmentRoleAsync(
            "action.user", seeded.ProviderEnvironmentId, "ServerOperator");

        var actionTarget = await session.GetAsync(
            $"/api/remote/targets/{seeded.ServerId}?scope=actions");
        Assert.Equal(HttpStatusCode.OK, actionTarget.StatusCode);
        var adminTarget = await session.GetAsync(
            $"/api/remote/targets/{seeded.ServerId}");
        Assert.Equal(HttpStatusCode.Forbidden, adminTarget.StatusCode);

        var intent = await SendAsync(session, HttpMethod.Post,
            $"/api/server-applications/{seeded.DeploymentId}" +
            $"/actions/{seeded.ActionId}/intent",
            new { confirm = false });
        Assert.Equal(HttpStatusCode.OK, intent.StatusCode);
        var prepared = await ReadAsync(intent);
        var executionId = prepared.GetProperty("executionId").GetInt32();
        Assert.Equal(seeded.ServerId, prepared.GetProperty("serverId").GetInt32());
        Assert.Contains("echo hello", prepared.GetProperty("script").GetString());

        var complete = await SendAsync(session, HttpMethod.Post,
            $"/api/remote/actions/{executionId}/complete",
            new { exitCode = 0, output = "hello", timedOut = false });
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var execution = await db.CommandExecutions.SingleAsync(x => x.Id == executionId);
            Assert.Equal("Action", execution.Kind);
            Assert.Equal("hello", execution.Output);
            Assert.NotNull(execution.EndedAt);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var policy = await db.ConnectionPolicies.SingleAsync();
            policy.Mode = "Server";
            await db.SaveChangesAsync();
        }
        var rejected = await SendAsync(session, HttpMethod.Post,
            $"/api/server-applications/{seeded.DeploymentId}" +
            $"/actions/{seeded.ActionId}/intent",
            new { confirm = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
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

    private async Task AddEnvironmentRoleAsync(
        string username, int providerEnvironmentId, string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var user = await db.Users.SingleAsync(x => x.Username == username);
        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        var group = new Group
        {
            Name = $"Remote group {Guid.NewGuid():N}"[..40]
        };
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

    private async Task<string> CreateTokenAsync(string username, string scopes)
    {
        using var session = await SessionClientAsync(username);
        var created = await SendAsync(session, HttpMethod.Post, "/api/api-tokens",
            new { name = "remote-client", scopes });
        return (await ReadAsync(created)).GetProperty("token").GetString()!;
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

    private sealed record SeededRemote(
        int ServerId,
        int ProviderEnvironmentId,
        int DeploymentId,
        int ActionId);
}

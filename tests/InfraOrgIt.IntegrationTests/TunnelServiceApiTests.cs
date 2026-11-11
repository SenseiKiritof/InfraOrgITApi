using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MySql;

namespace InfraOrgIt.IntegrationTests;

public sealed class TunnelServiceApiTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("infraorgit")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly FakeSsh _ssh = new();
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
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISshConnector>();
                services.AddSingleton<ISshConnector>(_ssh);
            });
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
    public async Task Tunnel_service_crud_validates_modes_and_requires_admin()
    {
        var seeded = await SeedAsync();
        using var admin = await SessionClientAsync("service.admin");
        await AddRoleAsync("service.admin", seeded.ProviderEnvironmentId, "ServerAdmin");
        using var operatorSession = await SessionClientAsync("service.operator");
        await AddRoleAsync(
            "service.operator", seeded.ProviderEnvironmentId, "ServerOperator");
        using var outsider = await SessionClientAsync("service.outsider");

        // Reads are open to any authenticated user.
        var list = await SendAsync(outsider, HttpMethod.Get, Url(seeded.ServerId));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Empty((await ReadAsync(list)).EnumerateArray());

        var denied = await SendAsync(operatorSession, HttpMethod.Post,
            Url(seeded.ServerId), LocalBody("denied"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var local = await SendAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            LocalBody("postgres", localPort: 15432, kind: "Http",
                openPath: "/admin", autoStart: true));
        Assert.Equal(HttpStatusCode.OK, local.StatusCode);
        var localDto = await ReadAsync(local);
        var localId = localDto.GetProperty("id").GetInt32();
        Assert.Equal(seeded.ServerId, localDto.GetProperty("serverId").GetInt32());
        Assert.Equal("postgres", localDto.GetProperty("name").GetString());
        Assert.Equal("Local", localDto.GetProperty("mode").GetString());
        Assert.Equal("127.0.0.1", localDto.GetProperty("targetHost").GetString());
        Assert.Equal(5432, localDto.GetProperty("targetPort").GetInt32());
        Assert.Equal(15432, localDto.GetProperty("localPort").GetInt32());
        Assert.Equal(JsonValueKind.Null, localDto.GetProperty("remotePort").ValueKind);
        Assert.Equal("Http", localDto.GetProperty("kind").GetString());
        Assert.Equal("/admin", localDto.GetProperty("openPath").GetString());
        Assert.True(localDto.GetProperty("autoStart").GetBoolean());
        Assert.True(localDto.GetProperty("enabled").GetBoolean());

        var duplicate = await SendAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            LocalBody("postgres", localPort: 15433));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var dynamicService = await SendAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            new
            {
                name = "socks",
                mode = "Dynamic",
                localPort = 1080,
                kind = "Tcp",
                enabled = true
            });
        Assert.Equal(HttpStatusCode.OK, dynamicService.StatusCode);
        var dynamicDto = await ReadAsync(dynamicService);
        Assert.Equal("Dynamic", dynamicDto.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, dynamicDto.GetProperty("targetPort").ValueKind);
        Assert.Equal(JsonValueKind.Null, dynamicDto.GetProperty("remotePort").ValueKind);

        var remoteService = await SendAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            new
            {
                name = "remote-db",
                mode = "Remote",
                targetHost = "10.70.0.20",
                targetPort = 5432,
                remotePort = 15432,
                kind = "Tcp",
                enabled = true
            });
        Assert.Equal(HttpStatusCode.OK, remoteService.StatusCode);
        var remoteDto = await ReadAsync(remoteService);
        var remoteId = remoteDto.GetProperty("id").GetInt32();
        Assert.Equal(15432, remoteDto.GetProperty("remotePort").GetInt32());
        Assert.Equal(JsonValueKind.Null, remoteDto.GetProperty("localPort").ValueKind);

        // Per-mode validation.
        await AssertUnprocessableAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            new { name = "no-host", mode = "Local", targetPort = 5432, localPort = 15434 });
        await AssertUnprocessableAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            new { name = "no-target", mode = "Local", targetHost = "127.0.0.1", localPort = 15434 });
        await AssertUnprocessableAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            LocalBody("low-port", localPort: 80));
        await AssertUnprocessableAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            new
            {
                name = "dynamic-target",
                mode = "Dynamic",
                targetHost = "127.0.0.1",
                targetPort = 5432,
                localPort = 1080
            });
        await AssertUnprocessableAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            new
            {
                name = "remote-no-port",
                mode = "Remote",
                targetHost = "10.70.0.20",
                targetPort = 5432
            });
        await AssertUnprocessableAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            new
            {
                name = "remote-local",
                mode = "Remote",
                targetHost = "10.70.0.20",
                targetPort = 5432,
                remotePort = 15432,
                localPort = 15433
            });
        await AssertUnprocessableAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            new
            {
                name = "remote-http",
                mode = "Remote",
                targetHost = "10.70.0.20",
                targetPort = 5432,
                remotePort = 15432,
                kind = "Http"
            });
        await AssertUnprocessableAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            LocalBody("bad-path", openPath: "admin", kind: "Http"));
        await AssertUnprocessableAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            LocalBody("tcp-path", openPath: "/admin", kind: "Tcp"));

        var update = await SendAsync(admin, HttpMethod.Put,
            $"{Url(seeded.ServerId)}/{localId}",
            LocalBody("postgres-main", localPort: 15435));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var renamed = await SendAsync(admin, HttpMethod.Put,
            $"{Url(seeded.ServerId)}/{localId}", LocalBody("socks"));
        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);

        var operatorUpdate = await SendAsync(operatorSession, HttpMethod.Put,
            $"{Url(seeded.ServerId)}/{localId}", LocalBody("postgres-main"));
        Assert.Equal(HttpStatusCode.Forbidden, operatorUpdate.StatusCode);

        // Disabled rows stay in the admin list, ordered by name.
        var disabled = await SendAsync(admin, HttpMethod.Post, Url(seeded.ServerId),
            new
            {
                name = "a-disabled",
                mode = "Local",
                targetHost = "127.0.0.1",
                targetPort = 9000,
                enabled = false
            });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

        var listed = await ReadAsync(await SendAsync(outsider, HttpMethod.Get,
            Url(seeded.ServerId)));
        var names = listed.EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()).ToList();
        Assert.Equal(["a-disabled", "postgres-main", "remote-db", "socks"], names);

        var operatorDelete = await SendAsync(operatorSession, HttpMethod.Delete,
            $"{Url(seeded.ServerId)}/{remoteId}");
        Assert.Equal(HttpStatusCode.Forbidden, operatorDelete.StatusCode);
        var deleted = await SendAsync(admin, HttpMethod.Delete,
            $"{Url(seeded.ServerId)}/{remoteId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Discover_parses_ss_output_and_maps_ssh_failure_to_422()
    {
        var seeded = await SeedAsync();
        using var admin = await SessionClientAsync("discover.admin");
        await AddRoleAsync("discover.admin", seeded.ProviderEnvironmentId, "ServerAdmin");
        using var operatorSession = await SessionClientAsync("discover.operator");
        await AddRoleAsync(
            "discover.operator", seeded.ProviderEnvironmentId, "ServerOperator");

        var forbidden = await SendAsync(operatorSession, HttpMethod.Post,
            $"{Url(seeded.ServerId)}/discover");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        _ssh.Result = new SshCommandResult(0, """
            LISTEN 0      4096         0.0.0.0:22         0.0.0.0:*
            LISTEN 0      511          0.0.0.0:80         0.0.0.0:*
            LISTEN 0      128        127.0.0.1:5432       0.0.0.0:*
            LISTEN 0      128        127.0.0.1:8080       0.0.0.0:*
            LISTEN 0      128             [::]:8080            [::]:*
            """);

        var discovered = await SendAsync(admin, HttpMethod.Post,
            $"{Url(seeded.ServerId)}/discover");
        Assert.Equal(HttpStatusCode.OK, discovered.StatusCode);
        var items = (await ReadAsync(discovered)).GetProperty("items")
            .EnumerateArray().ToList();
        Assert.Equal([22, 80, 5432, 8080],
            items.Select(x => x.GetProperty("port").GetInt32()));
        Assert.Equal("0.0.0.0", items[0].GetProperty("address").GetString());
        Assert.Equal("127.0.0.1", items[2].GetProperty("address").GetString());
        Assert.Equal("ss -tlnH", Assert.Single(_ssh.Scripts));

        _ssh.Result = new SshCommandResult(127, "sh: ss: command not found");
        var missing = await SendAsync(admin, HttpMethod.Post,
            $"{Url(seeded.ServerId)}/discover");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);

        _ssh.Result = new SshCommandResult(0, "not an ss line");
        var garbage = await SendAsync(admin, HttpMethod.Post,
            $"{Url(seeded.ServerId)}/discover");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, garbage.StatusCode);

        _ssh.Fail = true;
        var unreachable = await SendAsync(admin, HttpMethod.Post,
            $"{Url(seeded.ServerId)}/discover");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unreachable.StatusCode);
    }

    [Fact]
    public async Task Targets_include_custom_services_and_jump_chain()
    {
        var seeded = await SeedAsync();
        using var admin = await SessionClientAsync("targets.admin");
        await AddRoleAsync("targets.admin", seeded.ProviderEnvironmentId, "ServerAdmin");
        using var operatorSession = await SessionClientAsync("targets.operator");
        await AddRoleAsync(
            "targets.operator", seeded.ProviderEnvironmentId, "ServerOperator");

        await AddServiceAsync("targets.admin", seeded.ServerId, new
        {
            name = "api",
            mode = "Local",
            targetHost = "127.0.0.1",
            targetPort = 3000,
            localPort = 13000,
            kind = "Http",
            openPath = "/health"
        });
        await AddServiceAsync("targets.admin", seeded.ServerId, new
        {
            name = "socks",
            mode = "Dynamic",
            localPort = 1080
        });
        await AddServiceAsync("targets.admin", seeded.ServerId, new
        {
            name = "remote-db",
            mode = "Remote",
            targetHost = "10.70.0.20",
            targetPort = 5432,
            remotePort = 15432
        });
        await AddServiceAsync("targets.admin", seeded.ServerId, new
        {
            name = "disabled",
            mode = "Local",
            targetHost = "127.0.0.1",
            targetPort = 9999,
            enabled = false
        });

        var operatorToken = await CreateTokenAsync(
            operatorSession, "targets.operator");
        using var operatorClient = BearerClient(operatorToken);
        var operatorItem = await ReadTargetAsync(operatorClient, seeded.ServerId);
        var operatorServices = operatorItem.GetProperty("services")
            .EnumerateArray().ToList();
        Assert.Equal(["api", "socks"],
            operatorServices.Select(x => x.GetProperty("name").GetString()));
        Assert.DoesNotContain(operatorServices,
            x => x.GetProperty("mode").GetString() == "Remote");
        Assert.Equal("Http",
            operatorServices[0].GetProperty("kind").GetString());
        Assert.Equal("/health",
            operatorServices[0].GetProperty("openPath").GetString());

        var deployment = operatorItem.GetProperty("targets").EnumerateArray().Single();
        Assert.Equal(seeded.DeploymentId, deployment.GetProperty("serverApplicationId").GetInt32());
        Assert.Equal(8081, deployment.GetProperty("targetPort").GetInt32());

        var jumps = operatorItem.GetProperty("jumps").EnumerateArray().ToList();
        Assert.Equal(2, jumps.Count);
        Assert.Equal("10.70.0.9", jumps[0].GetProperty("host").GetString());
        Assert.Equal(2222, jumps[0].GetProperty("port").GetInt32());
        Assert.Equal("jump-user", jumps[0].GetProperty("username").GetString());
        Assert.Equal("10.70.0.8", jumps[1].GetProperty("host").GetString());
        Assert.Equal(2200, jumps[1].GetProperty("port").GetInt32());
        Assert.Equal("jump2-user", jumps[1].GetProperty("username").GetString());
        Assert.Equal("10.70.0.9",
            operatorItem.GetProperty("jump").GetProperty("host").GetString());

        var adminToken = await CreateTokenAsync(admin, "targets.admin");
        using var adminClient = BearerClient(adminToken);
        var adminItems = (await ReadAsync(
                await adminClient.GetAsync("/api/tunnels/targets")))
            .GetProperty("items").EnumerateArray().ToList();
        var adminItem = adminItems.Single(x =>
            x.GetProperty("serverId").GetInt32() == seeded.ServerId);
        var adminServices = adminItem.GetProperty("services")
            .EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToList();
        Assert.Equal(["api", "remote-db", "socks"], adminServices);
    }

    private static string Url(int serverId) =>
        $"/api/servers/{serverId}/tunnel-services";

    private static object LocalBody(
        string name,
        int localPort = 15432,
        string kind = "Tcp",
        string? openPath = null,
        bool autoStart = false) => new
    {
        name,
        mode = "Local",
        targetHost = "127.0.0.1",
        targetPort = 5432,
        localPort,
        kind,
        openPath,
        autoStart,
        enabled = true
    };

    private static async Task AssertUnprocessableAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        object body)
    {
        var response = await SendAsync(client, method, url, body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private async Task AddServiceAsync(
        string username, int serverId, object body)
    {
        using var session = await SessionClientAsync(username);
        var created = await SendAsync(session, HttpMethod.Post, Url(serverId), body);
        Assert.True(created.IsSuccessStatusCode,
            await created.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> ReadTargetAsync(HttpClient client, int serverId)
    {
        var payload = await ReadAsync(await client.GetAsync("/api/tunnels/targets"));
        return payload.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("serverId").GetInt32() == serverId);
    }

    private async Task<string> CreateTokenAsync(
        HttpClient session, string username)
    {
        var created = await SendAsync(session, HttpMethod.Post,
            "/api/api-tokens", new { name = "tunnel-" + username, scopes = "tunnels:operate" });
        return (await ReadAsync(created)).GetProperty("token").GetString()!;
    }

    private HttpClient BearerClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<SeededTunnelService> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var provider = new Provider { Name = "Tunnel Service Provider" };
        var environment = new EnvironmentDefinition
        {
            Name = "Tunnel Service Env",
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

        var jump2Credential = Credential("service-jump2-cred", "jump2-user");
        var jumpCredential = Credential("service-jump-cred", "jump-user");
        var targetCredential = Credential("service-target-cred", "deploy");
        db.ServerCredentials.AddRange(
            jump2Credential, jumpCredential, targetCredential);
        await db.SaveChangesAsync();

        var jump2 = new Server
        {
            ProviderEnvironmentId = providerEnvironment.Id,
            Name = "jump2",
            IpAddress = "10.70.0.8",
            Hostname = "jump2.local",
            SshPort = 2200,
            CredentialId = jump2Credential.Id
        };
        db.Servers.Add(jump2);
        await db.SaveChangesAsync();

        var jump = new Server
        {
            ProviderEnvironmentId = providerEnvironment.Id,
            Name = "jump",
            IpAddress = "10.70.0.9",
            Hostname = "jump.local",
            SshPort = 2222,
            CredentialId = jumpCredential.Id,
            JumpServerId = jump2.Id
        };
        db.Servers.Add(jump);
        await db.SaveChangesAsync();

        var server = new Server
        {
            ProviderEnvironmentId = providerEnvironment.Id,
            Name = "target",
            IpAddress = "10.70.0.10",
            Hostname = "target.local",
            SshPort = 22,
            CredentialId = targetCredential.Id,
            JumpServerId = jump.Id
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();

        var application = new Application
        {
            Name = "Tunnel Service App",
            Description = "",
            BasePath = "/tunnel-service",
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

        return new SeededTunnelService(
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

    private async Task AddRoleAsync(
        string username, int providerEnvironmentId, string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var user = await db.Users.SingleAsync(x => x.Username == username);
        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        var group = new Group
        {
            Name = $"Tunnel service {roleName} " + Guid.NewGuid().ToString("N")[..8]
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

    private sealed record SeededTunnelService(
        int ServerId, int ProviderEnvironmentId, int DeploymentId);

    private sealed class FakeSsh : ISshConnector
    {
        public SshCommandResult Result { get; set; } = new(0, "");
        public bool Fail { get; set; }
        public List<string> Scripts { get; } = [];

        public Task<ISshSession> ConnectAsync(Server server, CancellationToken ct) =>
            Fail
                ? throw new InvalidOperationException("SSH authentication failed")
                : Task.FromResult<ISshSession>(new Session(this));

        public Task<InfraOrgIt.Api.Infrastructure.Files.IFileTransferSession> OpenSftpAsync(
            Server server, ServerCredential credential, int port, CancellationToken ct) =>
            throw new NotSupportedException();

        private sealed class Session(FakeSsh owner) : ISshSession
        {
            public Task<SshCommandResult> RunAsync(
                string script, TimeSpan timeout, CancellationToken ct)
            {
                owner.Scripts.Add(script);
                return Task.FromResult(owner.Result);
            }

            public Task<IShellSession> OpenShellAsync(
                uint columns, uint rows, CancellationToken ct) =>
                throw new NotSupportedException();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

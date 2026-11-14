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

namespace InfraOrgIt.IntegrationTests;

public sealed class TunnelSessionApiTests : IAsyncLifetime
{

    private string _connectionString = "";
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await TestMySql.CreateDatabaseAsync("TunnelSessionApiTests");

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
        });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>()
            .Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Session_upsert_requires_scope_operator_and_valid_target()
    {
        var seeded = await SeedAsync();
        await SessionClientAsync("session.user");
        await AddOperatorRoleAsync("session.user", seeded.ProviderEnvironmentId);

        var created = await CreateTokenAsync("session.user", "tunnels:operate");
        using var tunnelClient = BearerClient(created.Token);

        var active = await tunnelClient.PostAsJsonAsync("/api/tunnels/sessions",
            SessionBody(seeded, "desktop-1", "active"));
        Assert.Equal(HttpStatusCode.NoContent, active.StatusCode);

        var first = await ReadSessionAsync(created.Id);
        Assert.Equal("Active", first.Outcome);
        Assert.Equal(seeded.ServerId, first.ServerId);
        Assert.Equal(seeded.DeploymentId, first.ServerApplicationId);
        Assert.Equal("session.user", first.ActorUsername);
        Assert.Equal(created.Id.ToString(), first.ClientId);
        Assert.Equal(8081, first.TargetPort);
        Assert.Equal(18081, first.LocalPort);
        Assert.Equal("Server", first.Transport);
        Assert.Null(first.EndedAt);
        Assert.Null(first.ErrorCode);

        // Heartbeat on the same session key updates the row, not the history.
        var heartbeat = await tunnelClient.PostAsJsonAsync("/api/tunnels/sessions",
            SessionBody(seeded, "desktop-1", "active", localPort: 18082));
        Assert.Equal(HttpStatusCode.NoContent, heartbeat.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var rows = await db.TunnelSessions
                .Where(x => x.SessionKey == "desktop-1").ToListAsync();
            var session = Assert.Single(rows);
            Assert.Equal(first.StartedAt, session.StartedAt);
            Assert.True(session.LastSeenAt >= first.LastSeenAt);
            Assert.Equal(18082, session.LocalPort);
            Assert.Empty(await db.AuditEvents.ToListAsync());
        }

        var readOnly = await CreateTokenAsync("session.user", "mcp:read");
        using (var readClient = BearerClient(readOnly.Token))
        {
            var forbidden = await readClient.PostAsJsonAsync("/api/tunnels/sessions",
                SessionBody(seeded, "desktop-2", "active"));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        await SessionClientAsync("session.outsider");
        var outsider = await CreateTokenAsync("session.outsider", "tunnels:operate");
        using (var outsiderClient = BearerClient(outsider.Token))
        {
            var denied = await outsiderClient.PostAsJsonAsync("/api/tunnels/sessions",
                SessionBody(seeded, "desktop-3", "active"));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        var badStatus = await tunnelClient.PostAsJsonAsync("/api/tunnels/sessions",
            SessionBody(seeded, "desktop-4", "maybe"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badStatus.StatusCode);

        var badDeployment = await tunnelClient.PostAsJsonAsync("/api/tunnels/sessions",
            new
            {
                sessionKey = "desktop-5",
                status = "active",
                serverId = seeded.ServerId,
                serverApplicationId = seeded.OtherDeploymentId,
                targetHost = "127.0.0.1",
                targetPort = 8081,
                localPort = 18081
            });
        Assert.Equal(HttpStatusCode.NotFound, badDeployment.StatusCode);

        var badPort = await tunnelClient.PostAsJsonAsync("/api/tunnels/sessions",
            SessionBody(seeded, "desktop-6", "active", targetPort: 0));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badPort.StatusCode);

        var error = await tunnelClient.PostAsJsonAsync("/api/tunnels/sessions",
            SessionBody(seeded, "desktop-1", "error", errorCode: "SSH_AUTH"));
        Assert.Equal(HttpStatusCode.NoContent, error.StatusCode);
        var failed = await ReadSessionAsync(created.Id);
        Assert.Equal("Error", failed.Outcome);
        Assert.Equal("SSH_AUTH", failed.ErrorCode);
        Assert.NotNull(failed.EndedAt);

        var stopped = await tunnelClient.PostAsJsonAsync("/api/tunnels/sessions",
            SessionBody(seeded, "desktop-1", "stopped"));
        Assert.Equal(HttpStatusCode.NoContent, stopped.StatusCode);
        var closed = await ReadSessionAsync(created.Id);
        Assert.Equal("Stopped", closed.Outcome);
        Assert.Null(closed.ErrorCode);
        Assert.NotNull(closed.EndedAt);

        // Reusing the key after a stop starts a new session window.
        var restarted = await tunnelClient.PostAsJsonAsync("/api/tunnels/sessions",
            SessionBody(seeded, "desktop-1", "active"));
        Assert.Equal(HttpStatusCode.NoContent, restarted.StatusCode);
        var reopened = await ReadSessionAsync(created.Id);
        Assert.Equal("Active", reopened.Outcome);
        Assert.Null(reopened.EndedAt);
        Assert.True(reopened.StartedAt >= closed.StartedAt);
    }

    [Fact]
    public async Task Session_list_is_visible_to_operators_and_system_admin_only()
    {
        var seeded = await SeedAsync();
        await SessionClientAsync("operator.user");
        await AddOperatorRoleAsync("operator.user", seeded.ProviderEnvironmentId);

        var token = await CreateTokenAsync("operator.user", "tunnels:operate");
        using (var tunnelClient = BearerClient(token.Token))
        {
            var created = await tunnelClient.PostAsJsonAsync("/api/tunnels/sessions",
                SessionBody(seeded, "desktop-1", "active"));
            Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        }

        using var operatorSession = await SessionClientAsync("operator.user");
        var listed = await SendAsync(operatorSession, HttpMethod.Get,
            "/api/tunnels/sessions");
        var items = (await ReadAsync(listed)).GetProperty("items").EnumerateArray()
            .ToList();
        var item = Assert.Single(items);
        Assert.Equal("desktop-1", item.GetProperty("sessionKey").GetString());
        Assert.Equal("operator.user", item.GetProperty("username").GetString());
        Assert.Equal(token.Id.ToString(), item.GetProperty("clientId").GetString());
        Assert.Equal(seeded.ServerId, item.GetProperty("serverId").GetInt32());
        Assert.Equal("target", item.GetProperty("serverName").GetString());
        Assert.Equal("Tunnel App", item.GetProperty("application").GetString());
        Assert.Equal(18081, item.GetProperty("localPort").GetInt32());
        Assert.Equal("127.0.0.1", item.GetProperty("targetHost").GetString());
        Assert.Equal(8081, item.GetProperty("targetPort").GetInt32());
        Assert.Equal("Active", item.GetProperty("status").GetString());

        using var outsiderSession = await SessionClientAsync("operator.outsider");
        var empty = await SendAsync(outsiderSession, HttpMethod.Get,
            "/api/tunnels/sessions");
        Assert.Empty((await ReadAsync(empty)).GetProperty("items").EnumerateArray());

        // Fresh rows outside the operator environments are hidden to the
        // operator but visible to SystemAdmin.
        await SeedOperationsAsync(seeded, token.Id);
        using var adminSession = await SessionClientAsync("system.admin");
        await AddSystemAdminRoleAsync("system.admin");
        var admin = (await ReadAsync(await SendAsync(adminSession, HttpMethod.Get,
                "/api/tunnels/sessions")))
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, admin.Count);
        Assert.Contains(admin, x =>
            x.GetProperty("serverName").GetString() == "other");
        Assert.Contains(admin, x =>
            x.GetProperty("sessionKey").GetString() == "recent-b");
        Assert.DoesNotContain(admin, x =>
            x.GetProperty("sessionKey").GetString() == "stale-b");
        Assert.DoesNotContain(admin, x =>
            x.GetProperty("sessionKey").GetString() == "stopped-b");

        var operatorItems = (await ReadAsync(await SendAsync(operatorSession,
                HttpMethod.Get, "/api/tunnels/sessions")))
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, operatorItems.Count);
        Assert.DoesNotContain(operatorItems, x =>
            x.GetProperty("serverName").GetString() == "other");
        Assert.DoesNotContain(operatorItems, x =>
            x.GetProperty("sessionKey").GetString() == "stale-b");
        Assert.True(operatorItems[0].GetProperty("lastSeenAt").GetDateTime() >=
                    operatorItems[1].GetProperty("lastSeenAt").GetDateTime());
    }

    [Fact]
    public async Task Tunnel_policy_returns_429_after_120_requests()
    {
        using var session = await SessionClientAsync("limit.user");

        HttpResponseMessage response = null!;
        for (var i = 0; i < 121; i++)
        {
            response = await SendAsync(session, HttpMethod.Get,
                "/api/tunnels/sessions");
            if (i == 119)
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Session_supports_dynamic_and_remote_modes()
    {
        var seeded = await SeedAsync();
        await SessionClientAsync("mode.user");
        await AddOperatorRoleAsync("mode.user", seeded.ProviderEnvironmentId);
        var token = await CreateTokenAsync("mode.user", "tunnels:operate");
        using var client = BearerClient(token.Token);
        var serviceId = await SeedServiceAsync(
            seeded.ServerId, "socks-preset", "Dynamic", targetHost: "127.0.0.1",
            targetPort: null, localPort: 1080);
        var otherServiceId = await SeedServiceAsync(
            seeded.OtherServerId, "other-preset", "Dynamic", targetHost: "127.0.0.1",
            targetPort: null, localPort: 1080);
        var remoteServiceId = await SeedServiceAsync(
            seeded.ServerId, "remote-db", "Remote", targetHost: "10.50.0.20",
            targetPort: 5432, remotePort: 15432);

        var dynamicSession = await client.PostAsJsonAsync("/api/tunnels/sessions",
            new
            {
                sessionKey = "socks-1",
                status = "active",
                serverId = seeded.ServerId,
                mode = "dynamic",
                localPort = 1080,
                tunnelServiceId = serviceId
            });
        Assert.Equal(HttpStatusCode.NoContent, dynamicSession.StatusCode);
        var socks = await ReadSessionByKeyAsync("socks-1");
        Assert.Equal("Dynamic", socks.Mode);
        Assert.Equal(1080, socks.LocalPort);
        Assert.Null(socks.TargetPort);
        Assert.Null(socks.RemotePort);
        Assert.Equal("", socks.TargetHost);
        Assert.Equal(serviceId, socks.TunnelServiceId);

        // PT-14: Remote mode is reserved to ServerAdmin, as in the target list.
        var remoteSession = await client.PostAsJsonAsync("/api/tunnels/sessions",
            new
            {
                sessionKey = "remote-1",
                status = "active",
                serverId = seeded.ServerId,
                mode = "Remote",
                targetHost = "10.50.0.20",
                targetPort = 5432,
                remotePort = 15432,
                tunnelServiceId = remoteServiceId
            });
        Assert.Equal(HttpStatusCode.Forbidden, remoteSession.StatusCode);

        await SessionClientAsync("mode.admin");
        await AddAdminRoleAsync("mode.admin", seeded.ProviderEnvironmentId);
        var adminToken = await CreateTokenAsync("mode.admin", "tunnels:operate");
        using var adminClient = BearerClient(adminToken.Token);
        var adminRemote = await adminClient.PostAsJsonAsync("/api/tunnels/sessions",
            new
            {
                sessionKey = "remote-1",
                status = "active",
                serverId = seeded.ServerId,
                mode = "Remote",
                targetHost = "10.50.0.20",
                targetPort = 5432,
                remotePort = 15432,
                tunnelServiceId = remoteServiceId
            });
        Assert.Equal(HttpStatusCode.NoContent, adminRemote.StatusCode);
        var remote = await ReadSessionByKeyAsync("remote-1");
        Assert.Equal("Remote", remote.Mode);
        Assert.Null(remote.LocalPort);
        Assert.Equal(5432, remote.TargetPort);
        Assert.Equal(15432, remote.RemotePort);

        var localWithoutPort = await client.PostAsJsonAsync("/api/tunnels/sessions",
            new
            {
                sessionKey = "local-1",
                status = "active",
                serverId = seeded.ServerId,
                targetHost = "127.0.0.1"
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, localWithoutPort.StatusCode);

        var dynamicWithTarget = await client.PostAsJsonAsync("/api/tunnels/sessions",
            new
            {
                sessionKey = "socks-2",
                status = "active",
                serverId = seeded.ServerId,
                mode = "Dynamic",
                targetPort = 5432,
                localPort = 1081
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, dynamicWithTarget.StatusCode);

        var remoteWithLocal = await client.PostAsJsonAsync("/api/tunnels/sessions",
            new
            {
                sessionKey = "remote-2",
                status = "active",
                serverId = seeded.ServerId,
                mode = "Remote",
                targetHost = "10.50.0.20",
                targetPort = 5432,
                remotePort = 15432,
                localPort = 15433
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, remoteWithLocal.StatusCode);

        var wrongService = await client.PostAsJsonAsync("/api/tunnels/sessions",
            new
            {
                sessionKey = "socks-bad",
                status = "active",
                serverId = seeded.ServerId,
                mode = "Dynamic",
                localPort = 1082,
                tunnelServiceId = otherServiceId
            });
        Assert.Equal(HttpStatusCode.NotFound, wrongService.StatusCode);

        using var operatorSession = await SessionClientAsync("mode.user");
        var items = (await ReadAsync(await SendAsync(operatorSession, HttpMethod.Get,
                "/api/tunnels/sessions")))
            .GetProperty("items").EnumerateArray().ToList();

        var socksItem = items.Single(x =>
            x.GetProperty("sessionKey").GetString() == "socks-1");
        Assert.Equal("Dynamic", socksItem.GetProperty("mode").GetString());
        Assert.Equal(1080, socksItem.GetProperty("localPort").GetInt32());
        Assert.Equal(JsonValueKind.Null, socksItem.GetProperty("targetPort").ValueKind);
        Assert.Equal(JsonValueKind.Null, socksItem.GetProperty("remotePort").ValueKind);
        Assert.Equal(serviceId, socksItem.GetProperty("serviceId").GetInt32());
        Assert.Equal("socks-preset", socksItem.GetProperty("serviceName").GetString());

        var remoteItem = items.Single(x =>
            x.GetProperty("sessionKey").GetString() == "remote-1");
        Assert.Equal("Remote", remoteItem.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, remoteItem.GetProperty("localPort").ValueKind);
        Assert.Equal(5432, remoteItem.GetProperty("targetPort").GetInt32());
        Assert.Equal(15432, remoteItem.GetProperty("remotePort").GetInt32());
        Assert.Equal(remoteServiceId, remoteItem.GetProperty("serviceId").GetInt32());
        Assert.Equal("remote-db", remoteItem.GetProperty("serviceName").GetString());
    }

    private async Task<int> SeedServiceAsync(
        int serverId,
        string name,
        string mode,
        string targetHost,
        int? targetPort,
        int? localPort = null,
        int? remotePort = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var service = new TunnelService
        {
            ServerId = serverId,
            Name = name,
            Mode = mode,
            TargetHost = targetHost,
            TargetPort = targetPort,
            LocalPort = localPort,
            RemotePort = remotePort,
            Enabled = true
        };
        db.TunnelServices.Add(service);
        await db.SaveChangesAsync();
        return service.Id;
    }

    private async Task<TunnelSession> ReadSessionByKeyAsync(string sessionKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        return await db.TunnelSessions.AsNoTracking()
            .SingleAsync(x => x.SessionKey == sessionKey);
    }

    private async Task<TunnelSession> ReadSessionAsync(int tokenId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        return await db.TunnelSessions.AsNoTracking()
            .Where(x => x.ClientId == tokenId.ToString())
            .Where(x => x.SessionKey == "desktop-1")
            .OrderByDescending(x => x.Id)
            .FirstAsync();
    }

    private async Task SeedOperationsAsync(SeededTunnel seeded, int tokenId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var user = await db.Users.SingleAsync(x => x.Username == "operator.user");
        var outsider = await db.Users.SingleAsync(x => x.Username == "operator.outsider");
        var now = DateTime.UtcNow;

        db.TunnelSessions.AddRange(
            new TunnelSession
            {
                SessionKey = "recent-b",
                UserId = user.Id,
                ActorUsername = user.Username,
                ClientId = tokenId.ToString(),
                ServerId = seeded.ServerId,
                ServerApplicationId = seeded.DeploymentId,
                LocalPort = 18082,
                TargetHost = "127.0.0.1",
                TargetPort = 8081,
                StartedAt = now.AddMinutes(-1),
                LastSeenAt = now.AddMinutes(-1),
                Outcome = "Active"
            },
            new TunnelSession
            {
                SessionKey = "stale-b",
                UserId = user.Id,
                ActorUsername = user.Username,
                ServerId = seeded.ServerId,
                LocalPort = 18083,
                TargetHost = "127.0.0.1",
                TargetPort = 8081,
                StartedAt = now.AddMinutes(-5),
                LastSeenAt = now.AddMinutes(-3),
                Outcome = "Active"
            },
            new TunnelSession
            {
                SessionKey = "stopped-b",
                UserId = user.Id,
                ActorUsername = user.Username,
                ServerId = seeded.ServerId,
                LocalPort = 18084,
                TargetHost = "127.0.0.1",
                TargetPort = 8081,
                StartedAt = now.AddMinutes(-4),
                LastSeenAt = now.AddMinutes(-1),
                EndedAt = now.AddMinutes(-1),
                Outcome = "Stopped"
            },
            new TunnelSession
            {
                SessionKey = "other-b",
                UserId = outsider.Id,
                ActorUsername = outsider.Username,
                ServerId = seeded.OtherServerId,
                LocalPort = 18085,
                TargetHost = "127.0.0.1",
                TargetPort = 8081,
                StartedAt = now.AddMinutes(-1),
                LastSeenAt = now.AddMinutes(-1),
                Outcome = "Active"
            });
        await db.SaveChangesAsync();
    }

    private async Task<SeededTunnel> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var provider = new Provider { Name = "Session Provider" };
        var environment = new EnvironmentDefinition { Name = "Session Env", Description = "" };
        var otherEnvironment = new EnvironmentDefinition { Name = "Other Env", Description = "" };
        db.Providers.Add(provider);
        db.EnvironmentDefinitions.AddRange(environment, otherEnvironment);
        await db.SaveChangesAsync();

        var providerEnvironment = new ProviderEnvironment
        {
            ProviderId = provider.Id,
            EnvironmentDefinitionId = environment.Id
        };
        var otherProviderEnvironment = new ProviderEnvironment
        {
            ProviderId = provider.Id,
            EnvironmentDefinitionId = otherEnvironment.Id
        };
        db.ProviderEnvironments.AddRange(providerEnvironment, otherProviderEnvironment);
        await db.SaveChangesAsync();

        var jumpCredential = Credential("session-jump-cred", "jump-user");
        var targetCredential = Credential("session-target-cred", "deploy");
        db.ServerCredentials.AddRange(jumpCredential, targetCredential);
        await db.SaveChangesAsync();

        var jump = new Server
        {
            ProviderEnvironmentId = providerEnvironment.Id,
            Name = "jump",
            IpAddress = "10.50.0.9",
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
            IpAddress = "10.50.0.10",
            Hostname = "target.local",
            SshPort = 22,
            CredentialId = targetCredential.Id,
            JumpServerId = jump.Id
        };
        var otherServer = new Server
        {
            ProviderEnvironmentId = otherProviderEnvironment.Id,
            Name = "other",
            IpAddress = "10.50.0.11",
            Hostname = "other.local",
            SshPort = 22
        };
        db.Servers.AddRange(server, otherServer);
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
        var otherDeploy = new ServerApplication
        {
            ServerId = otherServer.Id,
            ApplicationVersionId = version.Id,
            ExposedPort = 8080,
            HostPort = 8082
        };
        db.ServerApplications.AddRange(deploy, otherDeploy);
        await db.SaveChangesAsync();

        return new SeededTunnel(
            server.Id, providerEnvironment.Id, deploy.Id, otherDeploy.Id,
            otherServer.Id);
    }

    private static object SessionBody(
        SeededTunnel seeded,
        string sessionKey,
        string status,
        int targetPort = 8081,
        int localPort = 18081,
        string? errorCode = null,
        string transport = "Server") => new
    {
        sessionKey,
        status,
        serverId = seeded.ServerId,
        serverApplicationId = seeded.DeploymentId,
        targetHost = "127.0.0.1",
        targetPort,
        localPort,
        errorCode,
        transport
    };

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
        string username, int providerEnvironmentId) =>
        await AddEnvironmentRoleAsync(
            username, providerEnvironmentId, "ServerOperator");

    private async Task AddAdminRoleAsync(
        string username, int providerEnvironmentId) =>
        await AddEnvironmentRoleAsync(
            username, providerEnvironmentId, "ServerAdmin");

    private async Task AddEnvironmentRoleAsync(
        string username, int providerEnvironmentId, string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var user = await db.Users.SingleAsync(x => x.Username == username);
        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        var group = new Group { Name = "Session operators " + Guid.NewGuid().ToString("N")[..8] };
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

    private async Task AddSystemAdminRoleAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var user = await db.Users.SingleAsync(x => x.Username == username);
        var role = await db.Roles.SingleAsync(x => x.Name == "SystemAdmin");
        db.RoleUsers.Add(new RoleUser { RolesId = role.Id, UsersId = user.Id });
        await db.SaveChangesAsync();
    }

    private async Task<CreatedToken> CreateTokenAsync(
        string username, string scopes)
    {
        using var session = await SessionClientAsync(username);
        var created = await SendAsync(session, HttpMethod.Post,
            "/api/api-tokens", new { name = "tunnel-session", scopes });
        var payload = await ReadAsync(created);
        return new CreatedToken(
            payload.GetProperty("id").GetInt32(),
            payload.GetProperty("token").GetString()!);
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

    private sealed record CreatedToken(int Id, string Token);

    private sealed record SeededTunnel(
        int ServerId,
        int ProviderEnvironmentId,
        int DeploymentId,
        int OtherDeploymentId,
        int OtherServerId);
}

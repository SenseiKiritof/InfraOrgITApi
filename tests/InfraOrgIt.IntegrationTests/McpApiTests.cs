using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.Infrastructure.Files;
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

public sealed class McpApiTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("infraorgit")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private readonly FakeSsh _ssh = new();

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
    public async Task Api_token_authentication_exposes_read_only_mcp_tools()
    {
        var seeded = await SeedServerAsync();
        using var session = await SessionClientAsync("mcp.user");

        var created = await SendAsync(session, HttpMethod.Post, "/api/api-tokens",
            new { name = "MCP test", scopes = "mcp:read", expiresInDays = 30 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var createdDto = await ReadAsync(created);
        var rawToken = createdDto.GetProperty("token").GetString();
        Assert.NotNull(rawToken);
        Assert.StartsWith(ApiTokenService.Prefix, rawToken);

        var listed = await ReadAsync(await session.GetAsync("/api/api-tokens"));
        var listedItem = listed.EnumerateArray().Single();
        Assert.Equal("mcp:read", listedItem.GetProperty("scopes").GetString());
        Assert.False(listedItem.TryGetProperty("token", out _));

        using var mcp = McpClientFor(rawToken!);

        var unauthorized = await _factory.CreateClient()
            .PostAsJsonAsync("/mcp", InitializeBody(1));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var initialize = await McpAsync(mcp, InitializeBody(1));
        Assert.True(initialize.TryGetProperty("result", out var initResult));
        Assert.True(initResult.TryGetProperty("serverInfo", out _));

        var tools = await McpAsync(mcp, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list"
        });
        var toolNames = tools.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(x => x.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("list_servers", toolNames);
        Assert.Contains("get_server", toolNames);
        Assert.Contains("list_applications", toolNames);
        Assert.Contains("list_projects", toolNames);
        Assert.Contains("list_deployments", toolNames);

        var resources = await McpAsync(mcp, new
        {
            jsonrpc = "2.0",
            id = 20,
            method = "resources/list"
        });
        var resourceUris = resources.GetProperty("result").GetProperty("resources")
            .EnumerateArray()
            .Select(x => x.GetProperty("uri").GetString())
            .ToList();
        Assert.Contains("infraorgit://inventory/summary", resourceUris);

        var templates = await McpAsync(mcp, new
        {
            jsonrpc = "2.0",
            id = 21,
            method = "resources/templates/list"
        });
        var templateUris = templates.GetProperty("result")
            .GetProperty("resourceTemplates")
            .EnumerateArray()
            .Select(x => x.GetProperty("uriTemplate").GetString())
            .ToList();
        Assert.Contains("infraorgit://servers/{id}", templateUris);
        Assert.Contains("infraorgit://applications/{id}", templateUris);

        var serverResource = await McpAsync(mcp, new
        {
            jsonrpc = "2.0",
            id = 22,
            method = "resources/read",
            @params = new { uri = $"infraorgit://servers/{seeded.Id}" }
        });
        var resourceText = serverResource.GetProperty("result")
            .GetProperty("contents").EnumerateArray().Single()
            .GetProperty("text").GetString();
        var resourcePayload = JsonDocument.Parse(resourceText!);
        Assert.Equal(seeded.Name,
            resourcePayload.RootElement.GetProperty("name").GetString());

        var prompts = await McpAsync(mcp, new
        {
            jsonrpc = "2.0",
            id = 23,
            method = "prompts/list"
        });
        var promptNames = prompts.GetProperty("result").GetProperty("prompts")
            .EnumerateArray()
            .Select(x => x.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("diagnose-server", promptNames);
        Assert.Contains("application-impact", promptNames);

        var prompt = await McpAsync(mcp, new
        {
            jsonrpc = "2.0",
            id = 24,
            method = "prompts/get",
            @params = new
            {
                name = "diagnose-server",
                arguments = new Dictionary<string, string>
                {
                    ["serverId"] = seeded.Id.ToString()
                }
            }
        });
        var promptText = prompt.GetProperty("result").GetProperty("messages")
            .EnumerateArray().Single()
            .GetProperty("content").GetProperty("text").GetString();
        Assert.Contains(seeded.Id.ToString(), promptText);

        var call = await McpAsync(mcp, new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new
            {
                name = "list_servers",
                arguments = new { q = seeded.Name }
            }
        });
        var content = call.GetProperty("result").GetProperty("content")
            .EnumerateArray().Single();
        Assert.Equal("text", content.GetProperty("type").GetString());
        var payload = JsonDocument.Parse(content.GetProperty("text").GetString()!);
        var server = payload.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(seeded.Id, server.GetProperty("id").GetInt32());
        Assert.Equal(seeded.Name, server.GetProperty("name").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var audit = await db.AuditEvents.SingleAsync(x => x.Action == "list_servers");
            Assert.Equal("McpToolCall", audit.Kind);
            Assert.Equal("Ok", audit.Outcome);
            Assert.Equal("mcp.user", audit.ActorUsername);
            Assert.NotNull(audit.ClientId);

            var resourceAudit = await db.AuditEvents
                .SingleAsync(x => x.Action == "read_resource");
            Assert.Equal("Ok", resourceAudit.Outcome);
            Assert.Equal(seeded.Id, resourceAudit.ServerId);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var userId = await db.Users
                .Where(x => x.Username == "mcp.user")
                .Select(x => x.Id)
                .SingleAsync();
            const string noScopesToken = "itk_noscopes";
            db.ApiTokens.Add(new ApiToken
            {
                UserId = userId,
                Name = "no scopes",
                TokenHash = ApiTokenService.Hash(noScopesToken),
                Scopes = "",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var noScopes = McpClientFor("itk_noscopes");
        var denied = await noScopes.PostAsJsonAsync("/mcp", InitializeBody(99));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var tokenId = createdDto.GetProperty("id").GetInt32();
        var revoked = await SendAsync(session, HttpMethod.Delete,
            $"/api/api-tokens/{tokenId}");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        var afterRevoke = await mcp.PostAsJsonAsync("/mcp", InitializeBody(4));
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    private static object InitializeBody(int id) => new
    {
        jsonrpc = "2.0",
        id,
        method = "initialize",
        @params = new
        {
            protocolVersion = "2025-11-25",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" }
        }
    };

    private HttpClient McpClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> McpAsync(HttpClient client, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);

        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            var data = text.Split('\n')
                .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                .Select(line => line["data:".Length..].Trim())
                .LastOrDefault() ?? "{}";
            return JsonDocument.Parse(data).RootElement.Clone();
        }

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<SeededServer> SeedServerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var provider = new Provider { Name = "MCP Provider" };
        var environment = new EnvironmentDefinition { Name = "MCP Env", Description = "" };
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
            Name = "mcp-server",
            IpAddress = "10.30.0.1",
            Hostname = "mcp.local"
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();
        db.ReachabilityTests.Add(new ReachabilityTest
        {
            ServerId = server.Id,
            Hostname = "example.com",
            Port = 443,
            Description = "MCP test"
        });
        await db.SaveChangesAsync();

        return new SeededServer(
            server.Id, server.Name, providerEnvironment.Id, environment.Id);
    }

    private async Task<HttpClient> SessionClientAsync(
        string username, bool systemAdmin = false)
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

        if (systemAdmin)
        {
            var role = await db.Roles.SingleAsync(x => x.Name == "SystemAdmin");
            if (!await db.RoleUsers.AnyAsync(
                    x => x.RolesId == role.Id && x.UsersId == user.Id))
            {
                db.RoleUsers.Add(new RoleUser
                {
                    RolesId = role.Id,
                    UsersId = user.Id
                });
                await db.SaveChangesAsync();
            }
        }

        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var sessionId = sessions.Create(new SessionData(
            username, "Test", "User", null,
            DateTimeOffset.UtcNow.AddHours(1), null, "csrf",
            systemAdmin ? ["SystemAdmin"] : []));

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

    private sealed record SeededServer(
        int Id,
        string Name,
        int ProviderEnvironmentId,
        int EnvironmentDefinitionId);

    [Fact]
    public async Task Mcp_action_requires_scope_grant_and_rbac()
    {
        var seeded = await SeedServerAsync();
        using var userSession = await SessionClientAsync("mcp.action");

        var operate = await SendAsync(userSession, HttpMethod.Post,
            "/api/api-tokens",
            new { name = "action", scopes = "mcp:read mcp:operate" });
        var operateToken = (await ReadAsync(operate)).GetProperty("token").GetString()!;
        using var mcp = McpClientFor(operateToken);

        var noConfirm = await CallTool(mcp, "run_reachability_tests",
            new { serverId = seeded.Id });
        Assert.True(noConfirm.GetProperty("isError").GetBoolean());

        var noGrant = await CallTool(mcp, "run_reachability_tests",
            new { serverId = seeded.Id, confirm = true });
        Assert.True(noGrant.GetProperty("isError").GetBoolean());

        int userId;
        int grantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            userId = await db.Users
                .Where(x => x.Username == "mcp.action")
                .Select(x => x.Id)
                .SingleAsync();
            var role = await db.Roles.SingleAsync(x => x.Name == "ServerOperator");
            var group = new Group { Name = "MCP operators " + Guid.NewGuid().ToString("N")[..8] };
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            db.GroupUsers.Add(new GroupUser
            {
                GroupsId = group.Id,
                UsersId = userId
            });
            db.ProviderEnvironmentGroupRoles.Add(new ProviderEnvironmentGroupRole
            {
                ProviderEnvironmentId = seeded.ProviderEnvironmentId,
                GroupId = group.Id,
                RoleId = role.Id
            });
            await db.SaveChangesAsync();
        }

        using var admin = await SessionClientAsync("mcp.admin", systemAdmin: true);
        var grant = await SendAsync(admin, HttpMethod.Post, "/api/mcp/grants",
            new
            {
                userId,
                action = "reachability.run",
                environmentDefinitionId = seeded.EnvironmentDefinitionId
            });
        grantId = (await ReadAsync(grant)).GetInt32();

        var run = await CallTool(mcp, "run_reachability_tests",
            new { serverId = seeded.Id, confirm = true });
        Assert.False(
            run.TryGetProperty("isError", out var isError) &&
            isError.GetBoolean());
        var payload = JsonDocument.Parse(
            run.GetProperty("content").EnumerateArray().Single()
                .GetProperty("text").GetString()!);
        Assert.True(payload.RootElement.GetProperty("ok").GetBoolean());
        Assert.Single(payload.RootElement.GetProperty("results").EnumerateArray());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var audit = await db.AuditEvents
                .Where(x => x.Action == "run_reachability_tests")
                .OrderByDescending(x => x.Id)
                .FirstAsync();
            Assert.Equal("Ok", audit.Outcome);
            Assert.Equal(seeded.Id, audit.ServerId);
        }

        var removed = await SendAsync(admin, HttpMethod.Delete,
            $"/api/mcp/grants/{grantId}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        var afterRemove = await CallTool(mcp, "run_reachability_tests",
            new { serverId = seeded.Id, confirm = true });
        Assert.True(afterRemove.GetProperty("isError").GetBoolean());

        var readOnly = await SendAsync(userSession, HttpMethod.Post,
            "/api/api-tokens",
            new { name = "read-only", scopes = "mcp:read" });
        var readOnlyToken = (await ReadAsync(readOnly)).GetProperty("token").GetString()!;
        using var readMcp = McpClientFor(readOnlyToken);
        var noScope = await CallTool(readMcp, "run_reachability_tests",
            new { serverId = seeded.Id, confirm = true });
        Assert.True(noScope.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task Mcp_runs_a_configured_application_action()
    {
        var seeded = await SeedServerAsync();
        var (deploymentId, actionId) = await SeedActionAsync(seeded.Id);
        using var userSession = await SessionClientAsync("mcp.actions.user");
        var created = await SendAsync(userSession, HttpMethod.Post,
            "/api/api-tokens",
            new { name = "actions", scopes = "mcp:read mcp:operate" });
        var rawToken = (await ReadAsync(created)).GetProperty("token").GetString()!;
        using var mcp = McpClientFor(rawToken);

        int userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            userId = await db.Users
                .Where(x => x.Username == "mcp.actions.user")
                .Select(x => x.Id)
                .SingleAsync();
            var role = await db.Roles.SingleAsync(x => x.Name == "ServerOperator");
            var group = new Group
            {
                Name = "MCP action operators " + Guid.NewGuid().ToString("N")[..8]
            };
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            db.GroupUsers.Add(new GroupUser
            {
                GroupsId = group.Id,
                UsersId = userId
            });
            db.ProviderEnvironmentGroupRoles.Add(new ProviderEnvironmentGroupRole
            {
                ProviderEnvironmentId = seeded.ProviderEnvironmentId,
                GroupId = group.Id,
                RoleId = role.Id
            });
            await db.SaveChangesAsync();
        }

        using var admin = await SessionClientAsync("mcp.actions.admin", systemAdmin: true);
        var grant = await SendAsync(admin, HttpMethod.Post, "/api/mcp/grants",
            new { userId, action = "actions.run" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var run = await CallTool(mcp, "run_application_action",
            new
            {
                serverApplicationId = deploymentId,
                actionId,
                confirm = true,
                idempotencyKey = "mcp-action-" + Guid.NewGuid().ToString("N")
            });
        Assert.False(
            run.TryGetProperty("isError", out var isError) &&
            isError.GetBoolean());
        var payload = JsonDocument.Parse(
            run.GetProperty("content").EnumerateArray().Single()
                .GetProperty("text").GetString()!);
        Assert.Equal(0, payload.RootElement.GetProperty("exitCode").GetInt32());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var audit = await db.AuditEvents
                .Where(x => x.Action == "run_application_action")
                .OrderByDescending(x => x.Id)
                .FirstAsync();
            Assert.Equal("Ok", audit.Outcome);
            Assert.Equal(seeded.Id, audit.ServerId);
        }
    }

    private async Task<(int DeploymentId, int ActionId)> SeedActionAsync(int serverId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var application = new Application
        {
            Name = "MCP Action App " + Guid.NewGuid().ToString("N")[..8],
            Description = "",
            BasePath = "/mcp-action",
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
            ServerId = serverId,
            ApplicationVersionId = version.Id,
            ExposedPort = 8080
        };
        db.ServerApplications.Add(deploy);
        await db.SaveChangesAsync();

        var action = new ApplicationAction
        {
            ApplicationId = application.Id,
            Label = "Ping",
            Order = 0,
            CustomScript = "echo ok",
            ParameterValuesJson = "{}",
            RequiresConfirmation = true
        };
        db.ApplicationActions.Add(action);
        await db.SaveChangesAsync();
        return (deploy.Id, action.Id);
    }

    private static async Task<JsonElement> CallTool(
        HttpClient client, string name, object arguments)
    {
        var response = await McpAsync(client, new
        {
            jsonrpc = "2.0",
            id = 10,
            method = "tools/call",
            @params = new { name, arguments }
        });
        return response.GetProperty("result");
    }

    private sealed class FakeSsh : ISshConnector
    {
        public Task<ISshSession> ConnectAsync(
            Server server, CancellationToken ct) =>
            Task.FromResult<ISshSession>(new Session());

        public Task<IFileTransferSession> OpenSftpAsync(
            Server server,
            ServerCredential credential,
            int port,
            CancellationToken ct) =>
            throw new NotSupportedException();

        private sealed class Session : ISshSession
        {
            public Task<SshCommandResult> RunAsync(
                string script, TimeSpan timeout, CancellationToken ct) =>
                Task.FromResult(new SshCommandResult(0, ""));

            public Task<IShellSession> OpenShellAsync(
                uint columns, uint rows, CancellationToken ct) =>
                throw new NotSupportedException();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

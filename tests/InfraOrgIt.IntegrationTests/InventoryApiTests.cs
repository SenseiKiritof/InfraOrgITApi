using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;
using InfraOrgIt.IntegrationTests.Legacy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MySql;

namespace InfraOrgIt.IntegrationTests;

public sealed class InventoryApiTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("infraorgit")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private static readonly string TestCredentialKey =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray());

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
            builder.UseSetting("ServerAccess:CredentialKeys:1", TestCredentialKey);
            builder.UseSetting("ServerAccess:CommandTimeoutMinutes", "1");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISshConnector>();
                services.AddSingleton<ISshConnector>(_ssh);
            });
        });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _mysql.DisposeAsync();
    }

    [Fact]
    public async Task Provider_writes_require_system_admin()
    {
        using var viewer = await ClientForAsync("provider.viewer");
        using var admin = await ClientForAsync("provider.admin", systemAdmin: true);

        var denied = await SendAsync(viewer, HttpMethod.Post, "/api/providers", new { name = "Negato" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var readable = await viewer.GetAsync("/api/providers");
        Assert.Equal(HttpStatusCode.OK, readable.StatusCode);

        var created = await SendAsync(admin, HttpMethod.Post, "/api/providers", new { name = "Cloud" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await ReadAsync(created)).GetProperty("id").GetInt32();

        var page = await ReadAsync(await admin.GetAsync("/api/providers?q=Cloud"));
        Assert.Equal(1, page.GetProperty("total").GetInt32());

        var updated = await SendAsync(admin, HttpMethod.Put, $"/api/providers/{id}", new { name = "Cloud 2" });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        var deleted = await SendAsync(admin, HttpMethod.Delete, $"/api/providers/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var missing = await admin.GetAsync($"/api/providers/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Catalog_supports_application_version_api_and_query_param()
    {
        using var admin = await ClientForAsync("catalog.admin", systemAdmin: true);

        var application = await SendAsync(admin, HttpMethod.Post, "/api/applications", new
        {
            name = "Portale",
            description = "Portale web",
            basePath = "/portale",
            listeningOnPort = 8080
        });
        Assert.Equal(HttpStatusCode.OK, application.StatusCode);
        var applicationId = (await ReadAsync(application)).GetProperty("id").GetInt32();

        var version = await SendAsync(admin, HttpMethod.Post, $"/api/applications/{applicationId}/versions",
            new { versionNumber = "1.0.0" });
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
        var versionId = (await ReadAsync(version)).GetProperty("id").GetInt32();

        var api = await SendAsync(admin, HttpMethod.Post, $"/api/application-versions/{versionId}/apis", new
        {
            url = "/health",
            requestType = "GET",
            description = "Health check"
        });
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        var apiId = (await ReadAsync(api)).GetProperty("id").GetInt32();

        var param = await SendAsync(admin, HttpMethod.Post, $"/api/application-version-apis/{apiId}/query-params",
            new { name = "verbose", paramType = "Bool" });
        Assert.Equal(HttpStatusCode.OK, param.StatusCode);
        var paramId = (await ReadAsync(param)).GetProperty("id").GetInt32();

        var apis = (await ReadAsync(await admin.GetAsync($"/api/application-versions/{versionId}/apis")))
            .GetProperty("items");
        Assert.Equal(1, apis.GetArrayLength());
        Assert.Equal(1, apis[0].GetProperty("queryParamCount").GetInt32());

        var updatedVersion = await SendAsync(admin, HttpMethod.Put,
            $"/api/application-versions/{versionId}",
            new { applicationId, versionNumber = "1.0.1" });
        Assert.Equal(HttpStatusCode.NoContent, updatedVersion.StatusCode);
        var versions = (await ReadAsync(await admin.GetAsync($"/api/applications/{applicationId}/versions")))
            .GetProperty("items");
        Assert.Equal("1.0.1", versions[0].GetProperty("versionNumber").GetString());

        var updatedApi = await SendAsync(admin, HttpMethod.Put,
            $"/api/application-version-apis/{apiId}",
            new
            {
                applicationVersionId = versionId,
                url = "/healthz",
                requestType = "GET",
                description = "Health check aggiornato"
            });
        Assert.Equal(HttpStatusCode.NoContent, updatedApi.StatusCode);

        var updatedParam = await SendAsync(admin, HttpMethod.Put,
            $"/api/application-api-query-params/{paramId}",
            new { applicationApiId = apiId, name = "v", paramType = "String" });
        Assert.Equal(HttpStatusCode.NoContent, updatedParam.StatusCode);

        var invalidParam = await SendAsync(admin, HttpMethod.Post,
            $"/api/application-version-apis/{apiId}/query-params",
            new { name = "x", paramType = "Unknown" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidParam.StatusCode);

        var deleteVersion = await SendAsync(admin, HttpMethod.Delete, $"/api/application-versions/{versionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteVersion.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            Assert.False(await db.ApplicationVersions.AnyAsync(x => x.Id == versionId));
            Assert.False(await db.ApplicationVersionApis.AnyAsync(x => x.Id == apiId));
            Assert.False(await db.ApplicationApiQueryParams.AnyAsync(x => x.Id == paramId));
        }

        var deleteApplication = await SendAsync(admin, HttpMethod.Delete, $"/api/applications/{applicationId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteApplication.StatusCode);
    }

    [Fact]
    public async Task Server_writes_require_environment_admin_and_block_delete_with_deployments()
    {
        using var systemAdmin = await ClientForAsync("server.system", systemAdmin: true);
        using var envAdmin = await ClientForAsync("server.envadmin");
        using var operatorUser = await ClientForAsync("server.operator");
        using var outsider = await ClientForAsync("server.outsider");

        var environmentId = await CreateProviderEnvironmentAsync(systemAdmin);
        var versionId = await CreateApplicationVersionAsync(systemAdmin);

        var systemAdminAttempt = await SendAsync(systemAdmin, HttpMethod.Post, "/api/servers", new
        {
            providerEnvironmentId = environmentId,
            name = "srv-1",
            ipAddress = "10.0.0.10",
            hostname = "srv-1.local"
        });
        Assert.Equal(HttpStatusCode.Forbidden, systemAdminAttempt.StatusCode);

        await AssignEnvironmentRoleAsync(systemAdmin, environmentId, "server.envadmin", "ServerAdmin");
        await AssignEnvironmentRoleAsync(systemAdmin, environmentId, "server.operator", "ServerOperator");

        var operatorAttempt = await SendAsync(operatorUser, HttpMethod.Post, "/api/servers", new
        {
            providerEnvironmentId = environmentId,
            name = "srv-op",
            ipAddress = "10.0.0.11",
            hostname = "srv-op.local"
        });
        Assert.Equal(HttpStatusCode.Forbidden, operatorAttempt.StatusCode);

        var created = await SendAsync(envAdmin, HttpMethod.Post, "/api/servers", new
        {
            providerEnvironmentId = environmentId,
            name = "srv-1",
            ipAddress = "10.0.0.10",
            hostname = "srv-1.local",
            sshPort = 2222
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var createdBody = await ReadAsync(created);
        var serverId = createdBody.GetProperty("id").GetInt32();
        Assert.Equal(2222, createdBody.GetProperty("sshPort").GetInt32());

        var deploy = await SendAsync(envAdmin, HttpMethod.Post, $"/api/servers/{serverId}/applications",
            new { applicationVersionId = versionId, exposedPort = 8080 });
        Assert.Equal(HttpStatusCode.OK, deploy.StatusCode);
        var deployId = (await ReadAsync(deploy)).GetProperty("id").GetInt32();

        var blockedDelete = await SendAsync(envAdmin, HttpMethod.Delete, $"/api/servers/{serverId}");
        Assert.Equal(HttpStatusCode.Conflict, blockedDelete.StatusCode);

        var invalidRoute = await SendAsync(envAdmin, HttpMethod.Put,
            $"/api/server-applications/{deployId}",
            new { serverId, applicationVersionId = versionId, exposedPort = 8080, proxyRouteId = 10 });
        Assert.Equal(HttpStatusCode.NotFound, invalidRoute.StatusCode);

        var invalidFilter = await envAdmin.GetAsync("/api/servers?environmentId=abc");
        Assert.Equal(HttpStatusCode.BadRequest, invalidFilter.StatusCode);

        var outsiderAttempt = await SendAsync(outsider, HttpMethod.Put, $"/api/servers/{serverId}", new
        {
            providerEnvironmentId = environmentId,
            name = "srv-1",
            ipAddress = "10.0.0.10",
            hostname = "srv-1.local"
        });
        Assert.Equal(HttpStatusCode.Forbidden, outsiderAttempt.StatusCode);

        var access = await ReadAsync(await envAdmin.GetAsync("/api/server-access/me"));
        var adminEnvironmentIds = access.GetProperty("adminEnvironmentIds")
            .EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Contains(environmentId, adminEnvironmentIds);
    }

    [Fact]
    public async Task Provider_environment_pair_change_is_blocked_when_in_use()
    {
        using var admin = await ClientForAsync("pair.admin", systemAdmin: true);
        var providerEnvironmentId = await CreateProviderEnvironmentAsync(admin);
        var providerEnvironment = await ReadAsync(
            await admin.GetAsync($"/api/provider-environments/{providerEnvironmentId}"));
        var providerId = providerEnvironment.GetProperty("providerId").GetInt32();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var otherEnvironment = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/environments",
            new { name = "Alt " + suffix, description = "" }));
        var otherEnvironmentId = otherEnvironment.GetProperty("id").GetInt32();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            db.Servers.Add(new Server
            {
                ProviderEnvironmentId = providerEnvironmentId,
                Name = "Guard " + suffix,
                IpAddress = "10.0.0.50",
                Hostname = "guard.local"
            });
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(admin, HttpMethod.Put,
            $"/api/provider-environments/{providerEnvironmentId}",
            new { providerId, environmentId = otherEnvironmentId });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Provider_environment_and_project_links_are_guarded()
    {
        using var admin = await ClientForAsync("links.admin", systemAdmin: true);

        var provider = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/providers",
            new { name = "Provider link" }));
        var providerId = provider.GetProperty("id").GetInt32();

        var environment = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/environments",
            new { name = "Produzione link", description = "Ambiente" }));
        var environmentId = environment.GetProperty("id").GetInt32();

        var providerEnvironment = await SendAsync(admin, HttpMethod.Post, "/api/provider-environments",
            new { providerId, environmentId });
        Assert.Equal(HttpStatusCode.OK, providerEnvironment.StatusCode);
        var providerEnvironmentId = (await ReadAsync(providerEnvironment)).GetProperty("id").GetInt32();

        var blockedProviderDelete = await SendAsync(admin, HttpMethod.Delete, $"/api/providers/{providerId}");
        Assert.Equal(HttpStatusCode.Conflict, blockedProviderDelete.StatusCode);

        var application = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/applications", new
        {
            name = "App link",
            description = "",
            basePath = "/link",
            listeningOnPort = 9000
        }));
        var applicationId = application.GetProperty("id").GetInt32();

        var project = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/projects",
            new { name = "Progetto link", description = "Demo" }));
        var projectId = project.GetProperty("id").GetInt32();

        var link = await SendAsync(admin, HttpMethod.Post, $"/api/projects/{projectId}/applications",
            new { applicationId });
        Assert.Equal(HttpStatusCode.NoContent, link.StatusCode);

        var fromProject = await ReadAsync(await admin.GetAsync($"/api/projects/{projectId}/applications"));
        Assert.Equal(1, fromProject.GetArrayLength());

        var fromApplication = await ReadAsync(await admin.GetAsync($"/api/applications/{applicationId}/projects"));
        Assert.Equal(1, fromApplication.GetArrayLength());

        var unlink = await SendAsync(admin, HttpMethod.Delete,
            $"/api/projects/{projectId}/applications/{applicationId}");
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);

        var deleteEnvironment = await SendAsync(admin, HttpMethod.Delete,
            $"/api/provider-environments/{providerEnvironmentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteEnvironment.StatusCode);
    }

    [Fact]
    public async Task Credential_secret_is_encrypted_and_never_returned()
    {
        using var admin = await ClientForAsync("cred.admin", systemAdmin: true);
        using var viewer = await ClientForAsync("cred.viewer");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seeded = await SeedEnvironmentAsync(suffix);

        var created = await SendAsync(admin, HttpMethod.Post, "/api/server-credentials", new
        {
            name = "Credenziale " + suffix,
            username = "root",
            authType = "Password",
            secret = "super-secret"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var raw = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain("super-secret", raw);
        var credentialId = JsonDocument.Parse(raw).RootElement.GetProperty("id").GetInt32();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var row = await db.ServerCredentials.SingleAsync(x => x.Id == credentialId);
            Assert.NotEqual("super-secret", row.EncryptedSecret);
            var cipher = scope.ServiceProvider.GetRequiredService<CredentialCipher>();
            Assert.Equal("super-secret", cipher.Decrypt(row.EncryptedSecret, row.KeyVersion));
        }

        var denied = await viewer.GetAsync("/api/server-credentials");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var assign = await SendAsync(admin, HttpMethod.Put, $"/api/servers/{seeded.ServerId}/ssh",
            new { credentialId, sshPort = 2222 });
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

        var server = await ReadAsync(await admin.GetAsync($"/api/servers/{seeded.ServerId}"));
        Assert.Equal("Credenziale " + suffix, server.GetProperty("credentialName").GetString());

        var blocked = await SendAsync(admin, HttpMethod.Delete,
            $"/api/server-credentials/{credentialId}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var unassign = await SendAsync(admin, HttpMethod.Put, $"/api/servers/{seeded.ServerId}/ssh",
            new { credentialId = (int?)null, sshPort = 2222 });
        Assert.Equal(HttpStatusCode.NoContent, unassign.StatusCode);

        var deleted = await SendAsync(admin, HttpMethod.Delete,
            $"/api/server-credentials/{credentialId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Action_run_is_audited_redacted_and_idempotent()
    {
        using var admin = await ClientForAsync("action.admin", systemAdmin: true);
        using var operatorUser = await ClientForAsync("action.operator");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seeded = await SeedEnvironmentAsync(suffix);
        var catalog = await SeedCatalogAsync(seeded.ServerId, suffix);
        await AssignEnvironmentRoleAsync(admin, seeded.ProviderEnvironmentId,
            "action.operator", "ServerOperator");

        var credential = await ReadAsync(await SendAsync(admin, HttpMethod.Post,
            "/api/server-credentials", new
            {
                name = "Credenziale azione " + suffix,
                username = "root",
                authType = "Password",
                secret = "secret"
            }));
        await SendAsync(admin, HttpMethod.Put, $"/api/servers/{seeded.ServerId}/ssh", new
        {
            credentialId = credential.GetProperty("id").GetInt32(),
            sshPort = 22
        });

        var template = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/command-templates",
            new
            {
                name = "Template " + suffix,
                description = "",
                script = "echo {{Value}} {{ServerName}}"
            }));
        var action = await ReadAsync(await SendAsync(admin, HttpMethod.Post,
            $"/api/applications/{catalog.ApplicationId}/actions", new
            {
                label = "Esegui",
                order = 1,
                commandTemplateId = template.GetProperty("id").GetInt32(),
                parameterValues = new Dictionary<string, string> { ["Value"] = "secret-value" },
                requiresConfirmation = true
            }));
        var actionId = action.GetProperty("id").GetInt32();

        _ssh.Result = new SshCommandResult(0, "secret-value on server");
        var idempotencyKey = "run-" + suffix;
        var run = await SendAsync(operatorUser, HttpMethod.Post,
            $"/api/server-applications/{catalog.DeployId}/actions/{actionId}/run",
            body: null, idempotencyKey: idempotencyKey);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var runBody = await ReadAsync(run);
        Assert.Equal(0, runBody.GetProperty("exitCode").GetInt32());
        Assert.Contains("secret-value", runBody.GetProperty("output").GetString());

        var serverName = "Topo Server " + suffix;
        Assert.Equal($"echo 'secret-value' '{serverName}'", _ssh.Scripts[^1]);

        var executions = (await ReadAsync(await admin.GetAsync(
                $"/api/command-executions?q=Topo%20App%20{suffix}")))
            .GetProperty("items");
        var audit = executions[0];
        Assert.Equal("Action", audit.GetProperty("kind").GetString());
        Assert.DoesNotContain("secret-value", audit.GetProperty("script").GetString());
        Assert.Contains("***", audit.GetProperty("output").GetString());

        var calls = _ssh.Scripts.Count;
        var duplicate = await SendAsync(operatorUser, HttpMethod.Post,
            $"/api/server-applications/{catalog.DeployId}/actions/{actionId}/run",
            body: null, idempotencyKey: idempotencyKey);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(calls, _ssh.Scripts.Count);

        var unsafeAction = await SendAsync(admin, HttpMethod.Post,
            $"/api/applications/{catalog.ApplicationId}/actions", new
            {
                label = "Unsafe",
                order = 2,
                customScript = "echo {{Value}}",
                parameterValues = new Dictionary<string, string> { ["Value"] = "$(id)" }
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsafeAction.StatusCode);

        var denied = await SendAsync(admin, HttpMethod.Post,
            $"/api/server-applications/{catalog.DeployId}/actions/{actionId}/run");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var deniedList = await SendAsync(admin, HttpMethod.Get, "/api/command-executions");
        Assert.Equal(HttpStatusCode.OK, deniedList.StatusCode);
    }

    [Fact]
    public async Task Reachability_run_uses_ssh_and_audits()
    {
        using var admin = await ClientForAsync("reach.admin", systemAdmin: true);
        using var operatorUser = await ClientForAsync("reach.operator");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seeded = await SeedEnvironmentAsync(suffix);
        await AssignEnvironmentRoleAsync(admin, seeded.ProviderEnvironmentId,
            "reach.operator", "ServerOperator");

        var test = await SendAsync(admin, HttpMethod.Post, "/api/reachability-tests", new
        {
            hostname = "example.com",
            port = 443,
            description = "Web"
        });
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);

        _ssh.Result = new SshCommandResult(0, "");
        var run = await SendAsync(operatorUser, HttpMethod.Post,
            $"/api/servers/{seeded.ServerId}/reachability-tests/run");
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var results = await ReadAsync(run);
        Assert.Equal(1, results.GetArrayLength());
        Assert.True(results[0].GetProperty("reachable").GetBoolean());
        Assert.Contains("/dev/tcp/example.com/443", _ssh.Scripts[^1]);

        var denied = await SendAsync(admin, HttpMethod.Post,
            $"/api/servers/{seeded.ServerId}/reachability-tests/run");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var invalid = await SendAsync(admin, HttpMethod.Post, "/api/reachability-tests", new
        {
            hostname = "bad host",
            port = 80,
            description = ""
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

        var executions = (await ReadAsync(await admin.GetAsync(
                "/api/command-executions?kind=Reachability")))
            .GetProperty("items");
        Assert.Contains(executions.EnumerateArray(),
            item => item.GetProperty("kind").GetString() == "Reachability");
    }

    [Fact]
    public async Task Server_jump_host_is_validated_and_exposed()
    {
        using var admin = await ClientForAsync("jump.admin", systemAdmin: true);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seeded = await SeedEnvironmentAsync(suffix);
        await AssignEnvironmentRoleAsync(admin, seeded.ProviderEnvironmentId,
            "jump.admin", "ServerAdmin");

        var first = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/servers", new
        {
            providerEnvironmentId = seeded.ProviderEnvironmentId,
            name = "jump-a-" + suffix,
            ipAddress = "10.0.0.1",
            hostname = "jump-a-" + suffix,
            sshPort = 22
        }));
        var second = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/servers", new
        {
            providerEnvironmentId = seeded.ProviderEnvironmentId,
            name = "target-" + suffix,
            ipAddress = "10.0.0.2",
            hostname = "target-" + suffix,
            sshPort = 22
        }));
        var firstId = first.GetProperty("id").GetInt32();
        var secondId = second.GetProperty("id").GetInt32();

        var update = await SendAsync(admin, HttpMethod.Put, $"/api/servers/{secondId}", new
        {
            providerEnvironmentId = seeded.ProviderEnvironmentId,
            name = "target-" + suffix,
            ipAddress = "10.0.0.2",
            hostname = "target-" + suffix,
            sshPort = 22,
            jumpServerId = firstId
        });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var detail = await ReadAsync(await admin.GetAsync($"/api/servers/{secondId}"));
        Assert.Equal(firstId, detail.GetProperty("jumpServerId").GetInt32());
        Assert.Equal("jump-a-" + suffix,
            detail.GetProperty("jumpServerName").GetString());

        var self = await SendAsync(admin, HttpMethod.Put, $"/api/servers/{firstId}", new
        {
            providerEnvironmentId = seeded.ProviderEnvironmentId,
            name = "jump-a-" + suffix,
            ipAddress = "10.0.0.1",
            hostname = "jump-a-" + suffix,
            sshPort = 22,
            jumpServerId = firstId
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, self.StatusCode);

        var cycle = await SendAsync(admin, HttpMethod.Put, $"/api/servers/{firstId}", new
        {
            providerEnvironmentId = seeded.ProviderEnvironmentId,
            name = "jump-a-" + suffix,
            ipAddress = "10.0.0.1",
            hostname = "jump-a-" + suffix,
            sshPort = 22,
            jumpServerId = secondId
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cycle.StatusCode);

        var blocked = await SendAsync(admin, HttpMethod.Delete, $"/api/servers/{firstId}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
    }

    [Fact]
    public async Task Console_session_resumes_after_socket_close()
    {
        using var admin = await ClientForAsync("console.resume", systemAdmin: true);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seeded = await SeedEnvironmentAsync(suffix);
        await AssignEnvironmentRoleAsync(admin, seeded.ProviderEnvironmentId,
            "console.resume", "ServerAdmin");

        var cookie = admin.DefaultRequestHeaders.GetValues("Cookie").Single();

        var first = await ConnectConsoleAsync(cookie, seeded.ServerId, null);
        var sessionId = await ReadSessionIdAsync(first);

        _ssh.Shells.Single().Emit("output-prima-del-refresh");
        Assert.Contains("output-prima-del-refresh", await ReadBinaryAsync(first));

        first.Abort();
        await Task.Delay(300);

        var second = await ConnectConsoleAsync(cookie, seeded.ServerId, sessionId);
        Assert.Equal(sessionId, await ReadSessionIdAsync(second));
        Assert.Contains("output-prima-del-refresh", await ReadBinaryAsync(second));

        await second.SendAsync(
            Encoding.UTF8.GetBytes("{\"type\":\"terminate\"}"),
            WebSocketMessageType.Text, true, CancellationToken.None);
        await Task.Delay(300);

        var third = await ConnectConsoleAsync(cookie, seeded.ServerId, sessionId);
        Assert.NotEqual(sessionId, await ReadSessionIdAsync(third));
        third.Abort();
    }

    [Fact]
    public async Task Console_rejects_non_websocket_and_foreign_origin()
    {
        using var admin = await ClientForAsync("console.admin", systemAdmin: true);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seeded = await SeedEnvironmentAsync(suffix);

        var plain = await admin.GetAsync($"/api/socket/servers/{seeded.ServerId}/console");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, plain.StatusCode);

        var cookie = admin.DefaultRequestHeaders.GetValues("Cookie").Single();
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers["Cookie"] = cookie;
            request.Headers["Origin"] = "http://evil.invalid";
        };

        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync(
            new Uri($"ws://localhost/api/socket/servers/{seeded.ServerId}/console"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Admin_identity_and_menu_crud_with_guards()
    {
        using var admin = await ClientForAsync("admin.identity", systemAdmin: true);
        using var viewer = await ClientForAsync("admin.viewer");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(viewer, HttpMethod.Post, "/api/groups", new { name = "No " + suffix })).StatusCode);

        var group = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/groups",
            new { name = "Gruppo " + suffix }));
        var groupId = group.GetProperty("id").GetInt32();

        var role = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/roles",
            new { name = "Custom " + suffix, label = "Custom", scopeEnum = "System" }));
        var roleId = role.GetProperty("id").GetInt32();

        var user = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/users", new
        {
            username = "admin.user." + suffix,
            name = "Admin",
            surname = "Test",
            groupIds = new[] { groupId },
            roleIds = new[] { roleId }
        }));
        var userId = user.GetProperty("id").GetInt32();
        Assert.Equal(1, user.GetProperty("groups").GetArrayLength());
        Assert.Equal(1, user.GetProperty("roles").GetArrayLength());

        var updated = await SendAsync(admin, HttpMethod.Put, $"/api/users/{userId}", new
        {
            username = "admin.user." + suffix,
            name = "Admin",
            surname = "Test",
            groupIds = new[] { groupId },
            roleIds = Array.Empty<int>()
        });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        var reloaded = await ReadAsync(await admin.GetAsync($"/api/users/{userId}"));
        Assert.Equal(0, reloaded.GetProperty("roles").GetArrayLength());

        var builtInUpdate = await SendAsync(admin, HttpMethod.Put, "/api/roles/3", new
        {
            name = "Renamed",
            label = "Renamed",
            scopeEnum = "System"
        });
        Assert.Equal(HttpStatusCode.Conflict, builtInUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await SendAsync(admin, HttpMethod.Delete, "/api/roles/3")).StatusCode);

        var parent = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/menus", new
        {
            name = "Menu " + suffix,
            path = "/menu-" + suffix,
            isActive = true,
            hideBackButton = false,
            isVisible = true,
            order = 1,
            parentId = (int?)null,
            roleIds = new[] { 3 }
        }));
        var parentId = parent.GetProperty("id").GetInt32();
        var child = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/menus", new
        {
            name = "Figlio " + suffix,
            path = "/menu-" + suffix + "/figlio",
            isActive = true,
            hideBackButton = false,
            isVisible = true,
            order = 1,
            parentId,
            roleIds = new[] { 3 }
        }));
        var childId = child.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.Conflict,
            (await SendAsync(admin, HttpMethod.Delete, $"/api/menus/{parentId}")).StatusCode);
        var allMenus = await ReadAsync(await admin.GetAsync("/api/menus/all"));
        Assert.Contains(allMenus.EnumerateArray(), item => item.GetProperty("id").GetInt32() == childId);

        var moved = await SendAsync(admin, HttpMethod.Put, $"/api/menus/{childId}", new
        {
            name = "Figlio aggiornato " + suffix,
            path = "/menu-" + suffix + "/figlio",
            isActive = true,
            hideBackButton = false,
            isVisible = true,
            order = 2,
            parentId,
            roleIds = new[] { 3 }
        });
        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(admin, HttpMethod.Delete, $"/api/menus/{childId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(admin, HttpMethod.Delete, $"/api/menus/{parentId}")).StatusCode);

        var seeded = await SeedEnvironmentAsync(suffix);
        int operatorRoleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            operatorRoleId = (await db.Roles.SingleAsync(x => x.Name == "ServerOperator")).Id;
        }
        var assignment = await ReadAsync(await SendAsync(admin, HttpMethod.Post,
            "/api/provider-environment-group-roles", new
            {
                providerEnvironmentId = seeded.ProviderEnvironmentId,
                groupId,
                roleId = operatorRoleId
            }));
        var assignmentId = assignment.GetProperty("id").GetInt32();
        var assignments = await ReadAsync(await admin.GetAsync(
            $"/api/provider-environment-group-roles?providerEnvironmentId={seeded.ProviderEnvironmentId}"));
        Assert.Equal(1, assignments.GetProperty("total").GetInt32());

        int viewerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            viewerId = (await db.Users.SingleAsync(x => x.Username == "admin.viewer")).Id;
        }
        var viewerUser = await ReadAsync(await admin.GetAsync($"/api/users/{viewerId}"));
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(admin, HttpMethod.Put, $"/api/users/{viewerId}", new
            {
                username = viewerUser.GetProperty("username").GetString(),
                name = viewerUser.GetProperty("name").GetString(),
                surname = viewerUser.GetProperty("surname").GetString(),
                groupIds = new[] { groupId },
                roleIds = Array.Empty<int>()
            })).StatusCode);

        var operatorMenu = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/menus", new
        {
            name = "Menu operator " + suffix,
            path = "/menu-operator-" + suffix,
            isActive = true,
            hideBackButton = true,
            isVisible = true,
            order = 2,
            parentId = (int?)null,
            roleIds = new[] { operatorRoleId }
        }));
        var viewerMenus = await ReadAsync(await viewer.GetAsync("/api/menus"));
        Assert.Contains(viewerMenus.EnumerateArray(),
            item => item.GetProperty("name").GetString() == "Menu operator " + suffix);
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(admin, HttpMethod.Delete,
                $"/api/menus/{operatorMenu.GetProperty("id").GetInt32()}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(admin, HttpMethod.Delete,
                $"/api/provider-environment-group-roles/{assignmentId}")).StatusCode);

        int ownId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            ownId = (await db.Users.SingleAsync(x => x.Username == "admin.identity")).Id;
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await SendAsync(admin, HttpMethod.Delete, $"/api/users/{ownId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(admin, HttpMethod.Delete, $"/api/users/{userId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(admin, HttpMethod.Delete, $"/api/roles/{roleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(admin, HttpMethod.Delete, $"/api/groups/{groupId}")).StatusCode);
    }

    [Fact]
    public async Task Legacy_dry_run_maps_reference_and_catalog_fixture()
    {
        using var admin = await ClientForAsync("dryrun.admin", systemAdmin: true);
        var fixture = LegacyFixture.Load(FixturePath());
        fixture.Validate();
        var mapped = fixture.Map();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        db.EnvironmentDefinitions.AddRange(mapped.Environments);
        db.Providers.AddRange(mapped.Providers);
        await db.SaveChangesAsync();
        db.ProviderEnvironments.AddRange(mapped.ProviderEnvironments);
        db.Applications.AddRange(mapped.Applications);
        await db.SaveChangesAsync();
        db.ApplicationVersions.AddRange(mapped.ApplicationVersions);
        db.Projects.AddRange(mapped.Projects);
        await db.SaveChangesAsync();
        db.ApplicationVersionApis.AddRange(mapped.ApplicationVersionApis);
        await db.SaveChangesAsync();
        db.ApplicationApiQueryParams.AddRange(mapped.ApplicationApiQueryParams);
        db.Set<ApplicationProject>().AddRange(mapped.ApplicationProjects);
        await db.SaveChangesAsync();

        Assert.Equal("Provider Demo A",
            (await db.Providers.SingleAsync(x => x.Id == 9101)).Name);
        Assert.Equal("PROD",
            (await db.EnvironmentDefinitions.SingleAsync(x => x.Id == 9002)).Name);
        Assert.Equal("1.0.0",
            (await db.ApplicationVersions.SingleAsync(x => x.Id == 9401)).VersionNumber);
        Assert.Equal("GET",
            (await db.ApplicationVersionApis.SingleAsync(x => x.Id == 9501)).RequestTypeEnum);
        Assert.Equal("DELETE",
            (await db.ApplicationVersionApis.SingleAsync(x => x.Id == 9502)).RequestTypeEnum);
        Assert.Equal("Bool",
            (await db.ApplicationApiQueryParams.SingleAsync(x => x.Id == 9601)).ParamType);

        var providers = await ReadAsync(await admin.GetAsync("/api/providers?q=Provider Demo"));
        Assert.Equal(2, providers.GetProperty("total").GetInt32());

        var environments = await ReadAsync(
            await admin.GetAsync("/api/provider-environments?q=Provider Demo&pageSize=200"));
        Assert.Equal(2, environments.GetProperty("total").GetInt32());
        Assert.Contains(environments.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "Provider Demo A / PRE");

        var apis = (await ReadAsync(await admin.GetAsync("/api/application-versions/9401/apis")))
            .GetProperty("items");
        Assert.Equal(1, apis.GetArrayLength());
        Assert.Equal(1, apis[0].GetProperty("queryParamCount").GetInt32());

        var versions = (await ReadAsync(await admin.GetAsync("/api/applications/9301/versions")))
            .GetProperty("items");
        Assert.Equal(2, versions.GetArrayLength());

        var linked = await ReadAsync(await admin.GetAsync("/api/projects/9701/applications"));
        Assert.Equal(1, linked.GetArrayLength());

        var blocked = await SendAsync(admin, HttpMethod.Delete, "/api/providers/9101");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
    }

    [Fact]
    public async Task Topology_graph_and_diagram_round_trip()
    {
        using var admin = await ClientForAsync("topo.admin", systemAdmin: true);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var seeded = await SeedEnvironmentAsync(suffix);
        var secondServerId = await AddServerAsync(seeded.ProviderEnvironmentId, "second-" + suffix);
        var catalog = await SeedCatalogAsync(secondServerId, suffix);
        var connectionId = await SeedProxyAsync(seeded.ServerId, secondServerId, catalog.DeployId);

        var topology = await ReadAsync(await admin.GetAsync($"/api/topology?providerId={seeded.ProviderId}"));
        var providers = topology.GetProperty("providers");
        Assert.Equal(1, providers.GetArrayLength());
        var servers = providers[0].GetProperty("environments")[0].GetProperty("servers");
        Assert.Equal(2, servers.GetArrayLength());
        var connections = topology.GetProperty("proxyConnections");
        Assert.Equal(1, connections.GetArrayLength());
        Assert.Equal("/api", connections[0].GetProperty("routes")[0].GetProperty("route").GetString());

        var diagram = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/diagrams", new
        {
            name = "Diagramma " + suffix,
            description = "Test",
            isDefault = true
        }));
        var diagramId = diagram.GetProperty("id").GetInt32();

        var nodes = await SendAsync(admin, HttpMethod.Put, $"/api/diagrams/{diagramId}/nodes", new object[]
        {
            new { nodeType = "Server", entityId = seeded.ServerId, x = 10, y = 20, collapsed = false },
            new
            {
                nodeType = "ServerApplication",
                entityId = catalog.DeployId,
                x = 300,
                y = 40,
                width = 180,
                height = 60,
                collapsed = false
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, nodes.StatusCode);

        var edges = await SendAsync(admin, HttpMethod.Put, $"/api/diagrams/{diagramId}/edges", new object[]
        {
            new
            {
                proxyConnectionId = connectionId,
                targetNodeType = "ServerApplication",
                targetEntityId = catalog.DeployId,
                sourceHandle = "s-left",
                targetHandle = "t-top",
                bendX = 10,
                bendY = -5
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, edges.StatusCode);

        var graph = await ReadAsync(await admin.GetAsync($"/api/diagrams/{diagramId}/graph"));
        Assert.Equal(2, graph.GetProperty("nodes").GetArrayLength());
        var savedEdge = graph.GetProperty("edges")[0];
        Assert.Equal(connectionId, savedEdge.GetProperty("proxyConnectionId").GetInt32());
        Assert.Equal("s-left", savedEdge.GetProperty("sourceHandle").GetString());
        Assert.Equal(10, savedEdge.GetProperty("bendX").GetDouble());

        var secondDiagram = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/diagrams", new
        {
            name = "Diagramma default " + suffix,
            isDefault = true
        }));
        var defaultDiagram = await ReadAsync(await admin.GetAsync("/api/diagrams/default"));
        Assert.Equal(secondDiagram.GetProperty("id").GetInt32(), defaultDiagram.GetProperty("id").GetInt32());
        var first = await ReadAsync(await admin.GetAsync($"/api/diagrams/{diagramId}"));
        Assert.False(first.GetProperty("isDefault").GetBoolean());

        var deleted = await SendAsync(admin, HttpMethod.Delete, $"/api/diagrams/{diagramId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Topology_delete_requires_environment_admin()
    {
        using var admin = await ClientForAsync("topo.delete.admin", systemAdmin: true);
        using var envAdmin = await ClientForAsync("topo.delete.env");
        using var viewer = await ClientForAsync("topo.delete.viewer");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var seeded = await SeedEnvironmentAsync(suffix);

        var preview = await ReadAsync(await viewer.GetAsync(
            $"/api/topology/nodes/Server/{seeded.ServerId}/delete-preview"));
        Assert.Contains(preview.GetProperty("servers").EnumerateArray(),
            item => item.GetString() == "Topo Server " + suffix);

        var denied = await SendAsync(admin, HttpMethod.Delete,
            $"/api/topology/nodes/Server/{seeded.ServerId}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await AssignEnvironmentRoleAsync(admin, seeded.ProviderEnvironmentId,
            "topo.delete.env", "ServerAdmin");

        var deleted = await SendAsync(envAdmin, HttpMethod.Delete,
            $"/api/topology/nodes/Server/{seeded.ServerId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        Assert.False(await db.Servers.AnyAsync(x => x.Id == seeded.ServerId));
        Assert.True(await db.Providers.AnyAsync(x => x.Id == seeded.ProviderId));
        Assert.True(await db.EnvironmentDefinitions.AnyAsync(x => x.Id == seeded.EnvironmentId));
    }

    [Fact]
    public async Task Environment_scoped_diagram_requires_environment_admin()
    {
        using var admin = await ClientForAsync("diagram.admin", systemAdmin: true);
        using var envAdmin = await ClientForAsync("diagram.env");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seeded = await SeedEnvironmentAsync(suffix);

        var deniedSystem = await SendAsync(admin, HttpMethod.Post, "/api/diagrams", new
        {
            name = "Diagramma env " + suffix,
            scopeItems = new[] { new { scopeType = "ProviderEnvironment", entityId = seeded.EnvironmentId } }
        });
        Assert.Equal(HttpStatusCode.Forbidden, deniedSystem.StatusCode);

        await AssignEnvironmentRoleAsync(admin, seeded.ProviderEnvironmentId,
            "diagram.env", "ServerAdmin");

        var created = await SendAsync(envAdmin, HttpMethod.Post, "/api/diagrams", new
        {
            name = "Diagramma env " + suffix,
            scopeItems = new[] { new { scopeType = "ProviderEnvironment", entityId = seeded.EnvironmentId } }
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var diagramId = (await ReadAsync(created)).GetProperty("id").GetInt32();

        var nodes = await SendAsync(envAdmin, HttpMethod.Put, $"/api/diagrams/{diagramId}/nodes", new object[]
        {
            new { nodeType = "Server", entityId = seeded.ServerId, x = 1, y = 2, collapsed = false }
        });
        Assert.Equal(HttpStatusCode.NoContent, nodes.StatusCode);

        var deniedProvider = await SendAsync(envAdmin, HttpMethod.Post, "/api/diagrams", new
        {
            name = "Diagramma provider " + suffix,
            scopeItems = new[] { new { scopeType = "Provider", entityId = seeded.ProviderId } }
        });
        Assert.Equal(HttpStatusCode.Forbidden, deniedProvider.StatusCode);

        var deleted = await SendAsync(envAdmin, HttpMethod.Delete, $"/api/diagrams/{diagramId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Proxy_crud_requires_admin_on_both_environments()
    {
        using var admin = await ClientForAsync("proxy.admin", systemAdmin: true);
        using var envAdmin = await ClientForAsync("proxy.env");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var first = await SeedEnvironmentAsync("a-" + suffix);
        var second = await SeedEnvironmentAsync("b-" + suffix);

        var systemAttempt = await SendAsync(admin, HttpMethod.Post, "/api/proxy-connections", new
        {
            sourceServerId = first.ServerId,
            targetServerId = second.ServerId,
            direction = "Outgoing"
        });
        Assert.Equal(HttpStatusCode.Forbidden, systemAttempt.StatusCode);

        await AssignEnvironmentRoleAsync(admin, first.ProviderEnvironmentId, "proxy.env", "ServerAdmin");
        var partial = await SendAsync(envAdmin, HttpMethod.Post, "/api/proxy-connections", new
        {
            sourceServerId = first.ServerId,
            targetServerId = second.ServerId,
            direction = "Outgoing"
        });
        Assert.Equal(HttpStatusCode.Forbidden, partial.StatusCode);

        await AssignEnvironmentRoleAsync(admin, second.ProviderEnvironmentId, "proxy.env", "ServerAdmin");
        var created = await SendAsync(envAdmin, HttpMethod.Post, "/api/proxy-connections", new
        {
            sourceServerId = first.ServerId,
            targetServerId = second.ServerId,
            direction = "Outgoing"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var connectionId = (await ReadAsync(created)).GetProperty("id").GetInt32();

        var route = await SendAsync(envAdmin, HttpMethod.Post, $"/api/proxy-connections/{connectionId}/routes",
            new { route = "/api", mappedTo = "/mapped" });
        Assert.Equal(HttpStatusCode.OK, route.StatusCode);

        var duplicate = await SendAsync(envAdmin, HttpMethod.Post, $"/api/proxy-connections/{connectionId}/routes",
            new { route = "/api" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var self = await SendAsync(envAdmin, HttpMethod.Post, "/api/proxy-connections", new
        {
            sourceServerId = first.ServerId,
            targetServerId = first.ServerId,
            direction = "Outgoing"
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, self.StatusCode);

        var deleted = await SendAsync(envAdmin, HttpMethod.Delete, $"/api/proxy-connections/{connectionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public void Legacy_fixture_rejects_orphan_foreign_keys()
    {
        var fixture = LegacyFixture.Load(FixturePath());
        var broken = fixture with
        {
            ProviderEnvironments = [new LegacyProviderEnvironment(9999, 424242, 9001)]
        };

        var error = Assert.Throws<InvalidOperationException>(broken.Validate);
        Assert.Contains("senza provider", error.Message);
    }

    private async Task<SeededEnvironment> SeedEnvironmentAsync(string suffix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var provider = new Provider { Name = "Topo Provider " + suffix };
        var environment = new EnvironmentDefinition
        {
            Name = "Topo Env " + suffix,
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
            Name = "Topo Server " + suffix,
            IpAddress = "10.20.0.1",
            Hostname = "topo.local"
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();

        return new SeededEnvironment(provider.Id, environment.Id, providerEnvironment.Id, server.Id);
    }

    private async Task<int> AddServerAsync(int providerEnvironmentId, string suffix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var server = new Server
        {
            ProviderEnvironmentId = providerEnvironmentId,
            Name = "Topo Server " + suffix,
            IpAddress = "10.20.0.2",
            Hostname = "topo-2.local"
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private async Task<SeededCatalog> SeedCatalogAsync(int serverId, string suffix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var application = new Application
        {
            Name = "Topo App " + suffix,
            Description = "",
            BasePath = "/topo",
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

        var project = new Project { Name = "Topo Project " + suffix, Description = "Test" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        db.Set<ApplicationProject>().Add(new ApplicationProject
        {
            ApplicationsId = application.Id,
            ProjectsId = project.Id
        });
        await db.SaveChangesAsync();

        return new SeededCatalog(application.Id, version.Id, deploy.Id, project.Id);
    }

    private async Task<int> SeedProxyAsync(int sourceServerId, int targetServerId, int targetApplicationId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var connection = new ProxyConnection
        {
            SourceServerId = sourceServerId,
            TargetServerId = targetServerId,
            Direction = "Outgoing"
        };
        db.ProxyConnections.Add(connection);
        await db.SaveChangesAsync();

        db.ProxyRoutes.Add(new ProxyRoute
        {
            ProxyConnectionId = connection.Id,
            Route = "/api",
            MappedTo = "/mapped",
            TargetServerApplicationId = targetApplicationId
        });
        await db.SaveChangesAsync();
        return connection.Id;
    }

    private sealed record SeededEnvironment(
        int ProviderId,
        int EnvironmentId,
        int ProviderEnvironmentId,
        int ServerId);

    private async Task<WebSocket> ConnectConsoleAsync(
        string cookie, int serverId, string? resume)
    {
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers["Cookie"] = cookie;
            request.Headers["Origin"] = "http://localhost:3000";
        };
        var query = resume is null ? "" : $"?resume={Uri.EscapeDataString(resume)}";
        return await client.ConnectAsync(
            new Uri($"ws://localhost/api/socket/servers/{serverId}/console{query}"),
            CancellationToken.None);
    }

    private static async Task<string> ReadSessionIdAsync(WebSocket socket)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer, cts.Token);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        using var document = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<string> ReadBinaryAsync(WebSocket socket)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[64 * 1024];
        var received = new List<byte>();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                break;
            received.AddRange(buffer.AsSpan(0, result.Count).ToArray());
            if (result.EndOfMessage)
                break;
        }

        return Encoding.UTF8.GetString(received.ToArray());
    }

    private sealed class FakeSsh : ISshConnector
    {
        public SshCommandResult Result { get; set; } = new(0, "");
        public List<string> Scripts { get; } = [];
        public List<FakeShell> Shells { get; } = [];

        public Task<ISshSession> ConnectAsync(Server server, CancellationToken ct) =>
            Task.FromResult<ISshSession>(new FakeSession(this));

        private sealed class FakeSession(FakeSsh owner) : ISshSession
        {
            public Task<SshCommandResult> RunAsync(
                string script, TimeSpan timeout, CancellationToken ct)
            {
                owner.Scripts.Add(script);
                return Task.FromResult(owner.Result);
            }

            public Task<IShellSession> OpenShellAsync(
                uint columns, uint rows, CancellationToken ct)
            {
                var shell = new FakeShell();
                owner.Shells.Add(shell);
                return Task.FromResult<IShellSession>(shell);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeShell : IShellSession
    {
        private readonly Pipe _output = new();
        private readonly Pipe _input = new();

        public FakeShell()
        {
            Stream = new DuplexStream(_output.Reader.AsStream(), _input.Writer.AsStream());
        }

        public Stream Stream { get; }

        public void Emit(string text) =>
            _output.Writer.WriteAsync(Encoding.UTF8.GetBytes(text))
                .AsTask().GetAwaiter().GetResult();

        public void Resize(uint columns, uint rows)
        {
        }

        public async ValueTask DisposeAsync()
        {
            await _output.Writer.CompleteAsync();
            await _input.Writer.CompleteAsync();
            await _output.Reader.CompleteAsync();
            await _input.Reader.CompleteAsync();
        }
    }

    private sealed class DuplexStream(Stream reader, Stream writer) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => writer.Flush();
        public override Task FlushAsync(CancellationToken ct) => writer.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) =>
            reader.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken ct = default) =>
            reader.ReadAsync(buffer, ct);
        public override void Write(byte[] buffer, int offset, int count) =>
            writer.Write(buffer, offset, count);
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
            writer.WriteAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed record SeededCatalog(
        int ApplicationId,
        int VersionId,
        int DeployId,
        int ProjectId);

    private static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "Legacy", "Fixtures", "legacy-reference-catalog.json");

    private async Task<int> CreateProviderEnvironmentAsync(HttpClient admin)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var provider = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/providers",
            new { name = "Provider " + suffix }));
        var environment = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/environments",
            new { name = "Ambiente " + suffix, description = "" }));
        var providerEnvironment = await ReadAsync(await SendAsync(admin, HttpMethod.Post,
            "/api/provider-environments", new
            {
                providerId = provider.GetProperty("id").GetInt32(),
                environmentId = environment.GetProperty("id").GetInt32()
            }));
        return providerEnvironment.GetProperty("id").GetInt32();
    }

    private async Task<int> CreateApplicationVersionAsync(HttpClient admin)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var application = await ReadAsync(await SendAsync(admin, HttpMethod.Post, "/api/applications", new
        {
            name = "App " + suffix,
            description = "",
            basePath = "/app",
            listeningOnPort = 8080
        }));
        var version = await ReadAsync(await SendAsync(admin, HttpMethod.Post,
            $"/api/applications/{application.GetProperty("id").GetInt32()}/versions",
            new { versionNumber = "1.0.0" }));
        return version.GetProperty("id").GetInt32();
    }

    private async Task AssignEnvironmentRoleAsync(
        HttpClient admin, int environmentId, string username, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var user = await db.Users.SingleAsync(x => x.Username == username);
        var group = new Group { Name = "Gruppo " + Guid.NewGuid().ToString("N")[..8] };
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        db.GroupUsers.Add(new GroupUser { GroupsId = group.Id, UsersId = user.Id });
        var dbRole = await db.Roles.SingleAsync(x => x.Name == role);
        await db.SaveChangesAsync();

        var response = await SendAsync(admin, HttpMethod.Post,
            $"/api/provider-environments/{environmentId}/group-roles",
            new { groupId = group.Id, roleId = dbRole.Id });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpClient> ClientForAsync(string username, bool systemAdmin = false)
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
            var assigned = await db.RoleUsers.AnyAsync(
                x => x.RolesId == role.Id && x.UsersId == user.Id);
            if (!assigned)
            {
                db.RoleUsers.Add(new RoleUser { RolesId = role.Id, UsersId = user.Id });
                await db.SaveChangesAsync();
            }
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
        object? body = null,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-CSRF-Token", "csrf");
        request.Headers.Add("Origin", "http://localhost:3000");
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
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

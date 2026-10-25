using System.Net;
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

public sealed class InventoryApiTests : IAsyncLifetime
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

        var apis = await ReadAsync(await admin.GetAsync($"/api/application-versions/{versionId}/apis"));
        Assert.Equal(1, apis.GetArrayLength());
        Assert.Equal(1, apis[0].GetProperty("queryParamCount").GetInt32());

        var invalidParam = await SendAsync(admin, HttpMethod.Post,
            $"/api/application-version-apis/{apiId}/query-params",
            new { name = "x", paramType = "Unknown" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidParam.StatusCode);

        var deleteVersion = await SendAsync(admin, HttpMethod.Delete, $"/api/application-versions/{versionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteVersion.StatusCode);

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

        var blockedDelete = await SendAsync(envAdmin, HttpMethod.Delete, $"/api/servers/{serverId}");
        Assert.Equal(HttpStatusCode.Conflict, blockedDelete.StatusCode);

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
    public async Task Import_dry_run_loads_reference_and_catalog_fixture()
    {
        using var admin = await ClientForAsync("dryrun.admin", systemAdmin: true);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var provider = new Provider { Name = "DryRun Provider" };
        var environment = new EnvironmentDefinition { Name = "DryRun Env", Description = "" };
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

        db.Servers.AddRange(
            new Server
            {
                ProviderEnvironmentId = providerEnvironment.Id,
                Name = "DryRun Server 1",
                IpAddress = "10.10.0.1",
                Hostname = "dryrun-1.local"
            },
            new Server
            {
                ProviderEnvironmentId = providerEnvironment.Id,
                Name = "DryRun Server 2",
                IpAddress = "10.10.0.2",
                Hostname = "dryrun-2.local"
            });

        var application = new Application
        {
            Name = "DryRun App",
            Description = "",
            BasePath = "/dryrun",
            ListeningOnPort = 8080
        };
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var version = new ApplicationVersion
        {
            ApplicationId = application.Id,
            VersionNumber = "9.9.9"
        };
        db.ApplicationVersions.Add(version);
        await db.SaveChangesAsync();

        var api = new ApplicationVersionApi
        {
            ApplicationVersionId = version.Id,
            Url = "/dry-run",
            RequestTypeEnum = "GET",
            Description = ""
        };
        db.ApplicationVersionApis.Add(api);
        await db.SaveChangesAsync();

        db.ApplicationApiQueryParams.Add(new ApplicationApiQueryParam
        {
            ApplicationApiId = api.Id,
            Name = "flag",
            ParamType = "Bool"
        });

        var project = new Project { Name = "DryRun Project", Description = "Fixture" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        db.Set<ApplicationProject>().Add(new ApplicationProject
        {
            ApplicationsId = application.Id,
            ProjectsId = project.Id
        });
        await db.SaveChangesAsync();

        var providers = await ReadAsync(await admin.GetAsync("/api/providers?q=DryRun"));
        Assert.Equal(1, providers.GetProperty("total").GetInt32());

        var servers = await ReadAsync(await admin.GetAsync("/api/servers?q=DryRun"));
        Assert.Equal(2, servers.GetProperty("total").GetInt32());

        var apis = await ReadAsync(await admin.GetAsync($"/api/application-versions/{version.Id}/apis"));
        Assert.Equal(1, apis[0].GetProperty("queryParamCount").GetInt32());

        var linked = await ReadAsync(await admin.GetAsync($"/api/projects/{project.Id}/applications"));
        Assert.Equal(1, linked.GetArrayLength());

        var blocked = await SendAsync(admin, HttpMethod.Delete, $"/api/providers/{provider.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
    }

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

        var sessions = _factory.Services.GetRequiredService<ISessionStore>();
        var sessionId = sessions.Create(new SessionData(
            username, "Test", "User", null,
            DateTimeOffset.UtcNow.AddHours(1), null, "csrf",
            systemAdmin ? ["SystemAdmin"] : []));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Session(false)}={sessionId}");
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-CSRF-Token", "csrf");
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

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

public sealed class ConnectionPolicyApiTests : IAsyncLifetime
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
    public async Task Policy_defaults_to_auto_and_is_readable_by_users()
    {
        using var session = await SessionClientAsync("policy.user");

        var response = await session.GetAsync("/api/connection-policy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await ReadAsync(response);
        Assert.Equal("Auto", payload.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Policy_update_requires_system_admin_and_audits_the_change()
    {
        using var userSession = await SessionClientAsync("policy.user");
        var denied = await SendAsync(userSession, HttpMethod.Put,
            "/api/connection-policy", new { mode = "Client" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var adminSession = await SessionClientAsync("policy.admin");
        await AddSystemAdminRoleAsync("policy.admin");

        var updated = await SendAsync(adminSession, HttpMethod.Put,
            "/api/connection-policy", new { mode = "Server" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Server", (await ReadAsync(updated)).GetProperty("mode").GetString());

        var read = await ReadAsync(await adminSession.GetAsync("/api/connection-policy"));
        Assert.Equal("Server", read.GetProperty("mode").GetString());
        Assert.Equal("policy.admin", read.GetProperty("updatedBy").GetString());

        var reset = await SendAsync(adminSession, HttpMethod.Put,
            "/api/connection-policy", new { mode = "" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal("Auto", (await ReadAsync(reset)).GetProperty("mode").GetString());

        var invalid = await SendAsync(adminSession, HttpMethod.Put,
            "/api/connection-policy", new { mode = "Maybe" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var policy = await db.ConnectionPolicies.AsNoTracking().SingleAsync();
            Assert.Equal("Auto", policy.Mode);
            var audits = await db.AuditEvents
                .Where(x => x.Action == "connection-policy.update")
                .OrderBy(x => x.Id)
                .ToListAsync();
            Assert.Equal(2, audits.Count);
            Assert.All(audits, audit =>
            {
                Assert.Equal("AdminChange", audit.Kind);
                Assert.Equal("Ok", audit.Outcome);
                Assert.Equal("policy.admin", audit.ActorUsername);
            });
        }
    }

    [Fact]
    public async Task Policy_read_accepts_the_desktop_tunnel_token()
    {
        using var session = await SessionClientAsync("policy.desktop");
        var created = await SendAsync(session, HttpMethod.Post, "/api/api-tokens",
            new { name = "policy-desktop", scopes = "tunnels:operate" });
        var token = (await ReadAsync(created)).GetProperty("token").GetString()!;

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/connection-policy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Auto", (await ReadAsync(response))
            .GetProperty("mode").GetString());
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
}

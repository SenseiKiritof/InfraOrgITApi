using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.Features.Auth;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MySql;

namespace InfraOrgIt.IntegrationTests;

public sealed class AuthApiTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("infraorgit")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private readonly FakeOidcHandler _oidcHandler = new();

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
                services.RemoveAll<IOidcIdentityValidator>();
                services.AddSingleton<IOidcIdentityValidator>(new FakeIdentityValidator());
                services.RemoveAll<OidcClient>();
                services.AddHttpClient<OidcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _oidcHandler);
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
    public async Task Session_without_cookie_is_unauthorized()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("unauthorized", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Session_cookie_returns_user_and_csrf()
    {
        var sessionId = await CreateSessionAsync("session.user", "SystemAdmin");
        using var client = ClientWithSession(sessionId);

        var response = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("session.user", json.RootElement.GetProperty("user").GetProperty("username").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("csrfToken").GetString()));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Expired_session_refreshes_with_token_endpoint()
    {
        var sessionId = await CreateSessionAsync(
            "refresh.user",
            "SystemAdmin",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            "old-refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(10));

        using var client = ClientWithSession(sessionId);
        var response = await client.GetAsync("/api/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.GetProperty("expiresIn").GetInt32() > 0);
        Assert.Equal("refresh.user", json.RootElement.GetProperty("user").GetProperty("username").GetString());
    }

    [Fact]
    public async Task Menu_is_filtered_by_role()
    {
        var adminId = await CreateSessionAsync("menu.admin", "SystemAdmin");
        var viewerId = await CreateSessionAsync("menu.viewer");

        using var admin = ClientWithSession(adminId);
        using var viewer = ClientWithSession(viewerId);

        var adminNames = await MenuNamesAsync(admin);
        var viewerNames = await MenuNamesAsync(viewer);

        Assert.Contains("Users", adminNames);
        Assert.Contains("Servers", adminNames);
        Assert.DoesNotContain("Users", viewerNames);
        Assert.Contains("Servers", viewerNames);
    }

    [Fact]
    public async Task Logout_requires_csrf_and_revokes_session()
    {
        var sessionId = await CreateSessionAsync("logout.user", "SystemAdmin");
        using var client = ClientWithSession(sessionId);

        var withoutCsrf = await client.PostAsync("/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, withoutCsrf.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        request.Headers.Add("X-CSRF-Token", "csrf");
        var logout = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var session = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    [Fact]
    public async Task Mutations_reject_foreign_origin()
    {
        var sessionId = await CreateSessionAsync("origin.user", "SystemAdmin");
        using var client = ClientWithSession(sessionId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        request.Headers.Add("X-CSRF-Token", "csrf");
        request.Headers.Add("Origin", "http://evil.invalid");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<string> CreateSessionAsync(
        string username,
        string? role = null,
        DateTimeOffset? accessExpiresAt = null,
        string? refreshToken = null,
        DateTimeOffset? refreshExpiresAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var user = new User
        {
            Username = username,
            Name = "Test",
            Surname = "User"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        if (role is not null)
        {
            var dbRole = await db.Roles.SingleAsync(x => x.Name == role);
            db.RoleUsers.Add(new RoleUser { RolesId = dbRole.Id, UsersId = user.Id });
            await db.SaveChangesAsync();
        }

        var sessions = _factory.Services.GetRequiredService<ISessionStore>();
        return sessions.Create(new SessionData(
            username,
            "Test",
            "User",
            refreshToken,
            accessExpiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
            refreshExpiresAt,
            "csrf",
            role is null ? [] : [role]));
    }

    private HttpClient ClientWithSession(string sessionId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Session(false)}={sessionId}");
        return client;
    }

    private static async Task<List<string>> MenuNamesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/menus");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("name").GetString() ?? "")
            .ToList();
    }

    private sealed class FakeIdentityValidator : IOidcIdentityValidator
    {
        public Task<OidcIdentity> ValidateAsync(string idToken, CancellationToken ct) =>
            Task.FromResult(new OidcIdentity("refresh.user", "Test", "User"));
    }

    private sealed class FakeOidcHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = JsonSerializer.Serialize(new
            {
                access_token = "unused-access-token",
                refresh_token = "new-refresh-token",
                id_token = "header.payload.signature",
                expires_in = 300,
                refresh_expires_in = 1800
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}

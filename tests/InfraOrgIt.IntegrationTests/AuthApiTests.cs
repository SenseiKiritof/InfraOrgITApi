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
    private readonly string _keysPath = Path.Combine(
        Path.GetTempPath(), $"ds-infr-keys-{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();

        _factory = CreateFactory();

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _mysql.DisposeAsync();
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:MySql", _mysql.GetConnectionString());
            builder.UseSetting("Auth:SecureCookies", "false");
            builder.UseSetting("Auth:BootstrapAdminUsername", "");
            builder.UseSetting("DataProtection:KeysPath", _keysPath);
            builder.UseSetting("SessionStore:MaxSessionsPerUser", "3");
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
                services.RemoveAll<DesktopOidcClient>();
                services.AddHttpClient<DesktopOidcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _oidcHandler);
            });
        });

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
        Assert.Equal("system", json.RootElement.GetProperty("theme").GetString());
        Assert.Equal("csrf", json.RootElement.GetProperty("csrfToken").GetString());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Desktop_session_redeems_the_public_client_refresh_token()
    {
        _oidcHandler.Forms.Clear();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/desktop-session", new { refreshToken = "device-refresh-token" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var form = Assert.Single(_oidcHandler.Forms);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("device-refresh-token", form["refresh_token"]);
        Assert.Equal("infraorgit-mcp-desktop", form["client_id"]);
        Assert.False(form.ContainsKey("client_secret"));

        var sessionId = SessionIdFrom(response);
        using var scope = _factory.Services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<ISessionStore>().Get(sessionId);
        Assert.NotNull(session);
        Assert.Equal("refresh.user", session!.Username);
        Assert.True(session.Desktop);
        Assert.Equal("new-refresh-token", session.RefreshToken);
        Assert.InRange(
            session.RefreshExpiresAt!.Value,
            DateTimeOffset.UtcNow.AddDays(6),
            DateTimeOffset.UtcNow.AddDays(8));

        using var authenticated = ClientWithSession(sessionId);
        var me = await authenticated.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var json = await JsonDocument.ParseAsync(await me.Content.ReadAsStreamAsync());
        Assert.Equal("refresh.user", json.RootElement.GetProperty("user").GetProperty("username").GetString());
        Assert.Equal("system", json.RootElement.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task Desktop_session_renews_the_existing_cookie_session()
    {
        _oidcHandler.Forms.Clear();
        using var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync(
            "/auth/desktop-session", new { refreshToken = "device-refresh-token" });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var sessionId = SessionIdFrom(first);

        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var before = await db.Sessions.CountAsync(x => x.Username == "refresh.user");
        var initial = sessions.Get(sessionId);
        Assert.NotNull(initial);
        sessions.Update(sessionId, initial! with
        {
            RefreshExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        });

        client.DefaultRequestHeaders.Add(
            "Cookie", $"{SessionCookie.Session(false)}={sessionId}");
        var renewed = await client.PostAsJsonAsync(
            "/auth/desktop-session", new { refreshToken = "device-refresh-token" });

        Assert.Equal(HttpStatusCode.NoContent, renewed.StatusCode);
        Assert.Equal(sessionId, SessionIdFrom(renewed));
        Assert.Equal(
            before,
            await db.Sessions.CountAsync(x => x.Username == "refresh.user"));

        using var verify = _factory.Services.CreateScope();
        var renewedStore = verify.ServiceProvider.GetRequiredService<ISessionStore>();
        var session = renewedStore.Get(sessionId);
        Assert.NotNull(session);
        Assert.True(session!.Desktop);
        Assert.Equal("new-refresh-token", session.RefreshToken);
        Assert.InRange(
            session.RefreshExpiresAt!.Value,
            DateTimeOffset.UtcNow.AddDays(6),
            DateTimeOffset.UtcNow.AddDays(8));
    }

    [Fact]
    public async Task Desktop_session_rejects_invalid_refresh_token()
    {
        _oidcHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.BadRequest);
        try
        {
            using var client = _factory.CreateClient();
            var response = await client.PostAsJsonAsync(
                "/auth/desktop-session", new { refreshToken = "revoked-token" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            Assert.Equal("desktop_session_invalid", json.RootElement.GetProperty("code").GetString());
        }
        finally
        {
            _oidcHandler.Responder = null;
        }
    }

    [Fact]
    public async Task Desktop_session_returns_bad_gateway_when_keycloak_is_unreachable()
    {
        _oidcHandler.Responder = _ => throw new HttpRequestException("offline");
        try
        {
            using var client = _factory.CreateClient();
            var response = await client.PostAsJsonAsync(
                "/auth/desktop-session", new { refreshToken = "device-refresh-token" });

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }
        finally
        {
            _oidcHandler.Responder = null;
        }
    }

    [Fact]
    public async Task Desktop_session_is_rate_limited_per_ip()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        HttpResponseMessage response = null!;
        for (var i = 0; i < 31; i++)
            response = await client.PostAsJsonAsync(
                "/auth/desktop-session", new { refreshToken = "" });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Theme_update_requires_session_and_csrf_and_persists()
    {
        var sessionId = await CreateSessionAsync("theme.user");
        using var client = ClientWithSession(sessionId);

        using var anonymous = _factory.CreateClient();
        var noSession = await anonymous.PatchAsJsonAsync(
            "/api/session/theme", new { theme = "dark" });
        Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);

        var noCsrf = await client.PatchAsJsonAsync("/api/session/theme", new { theme = "dark" });
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/session/theme")
        {
            Content = JsonContent.Create(new { theme = "dark" })
        };
        request.Headers.Add("X-CSRF-Token", "csrf");
        request.Headers.Add("Origin", "http://localhost:3000");
        var updated = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        Assert.Equal("dark", await db.Users
            .Where(x => x.Username == "theme.user")
            .Select(x => x.Theme)
            .SingleAsync());

        var me = await client.GetAsync("/api/session");
        using var json = await JsonDocument.ParseAsync(await me.Content.ReadAsStreamAsync());
        Assert.Equal("dark", json.RootElement.GetProperty("theme").GetString());
        Assert.Equal("theme.user", json.RootElement.GetProperty("user").GetProperty("username").GetString());
        Assert.Equal("Test", json.RootElement.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal("csrf", json.RootElement.GetProperty("csrfToken").GetString());
    }

    [Fact]
    public async Task Theme_update_rejects_unknown_value()
    {
        var sessionId = await CreateSessionAsync("theme.invalid");
        using var client = ClientWithSession(sessionId);

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/session/theme")
        {
            Content = JsonContent.Create(new { theme = "blue" })
        };
        request.Headers.Add("X-CSRF-Token", "csrf");
        request.Headers.Add("Origin", "http://localhost:3000");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("validation_failed", json.RootElement.GetProperty("code").GetString());
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
    public async Task Expired_desktop_session_refreshes_with_the_public_client()
    {
        _oidcHandler.Forms.Clear();
        var refreshExpiry = DateTimeOffset.UtcNow.AddDays(10);
        var sessionId = await CreateSessionAsync(
            "desktop.refresh",
            accessExpiresAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            refreshToken: "desktop-refresh-token",
            refreshExpiresAt: refreshExpiry,
            desktop: true);

        using var client = ClientWithSession(sessionId);
        var response = await client.GetAsync("/api/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var form = Assert.Single(_oidcHandler.Forms);
        Assert.Equal("infraorgit-mcp-desktop", form["client_id"]);
        Assert.False(form.ContainsKey("client_secret"));

        using var scope = _factory.Services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<ISessionStore>().Get(sessionId);
        Assert.NotNull(session);
        Assert.Equal("new-refresh-token", session!.RefreshToken);
        Assert.True(session.Desktop);
        Assert.InRange(session.RefreshExpiresAt!.Value, refreshExpiry.AddMinutes(-1), refreshExpiry);
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
        request.Headers.Add("Origin", "http://localhost:3000");
        var logout = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var session = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    [Fact]
    public async Task Callback_rate_limit_returns_429()
    {
        using var client = _factory.CreateClient();

        HttpResponseMessage response = null!;
        for (var i = 0; i < 31; i++)
            response = await client.GetAsync("/auth/callback");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Responses_have_security_headers()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Mutations_reject_missing_origin()
    {
        var sessionId = await CreateSessionAsync("no-origin.user", "SystemAdmin");
        using var client = ClientWithSession(sessionId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        request.Headers.Add("X-CSRF-Token", "csrf");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("origin_failed", json.RootElement.GetProperty("code").GetString());
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

    [Fact]
    public async Task Correlation_id_is_echoed()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Correlation-ID", "test-correlation");

        var response = await client.SendAsync(request);

        Assert.Equal("test-correlation",
            response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task Session_is_visible_from_another_api_instance()
    {
        var sessionId = await CreateSessionAsync("shared.user", "ServerOperator");

        using var second = CreateFactory();
        using var client = second.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Session(false)}={sessionId}");

        var response = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Session_store_limits_active_sessions_per_user()
    {
        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();

        var ids = Enumerable.Range(0, 4)
            .Select(_ => sessions.Create(Data("limit.user")))
            .ToList();

        Assert.Null(sessions.Get(ids[0]));
        Assert.NotNull(sessions.Get(ids[3]));
    }

    [Fact]
    public async Task Remote_rate_limit_is_per_user_not_per_ip()
    {
        var first = await CreateSessionAsync("rate.one", "SystemAdmin");
        var second = await CreateSessionAsync("rate.two", "SystemAdmin");
        using var one = ClientWithSession(first);
        using var two = ClientWithSession(second);

        static async Task<HttpStatusCode> RunAsync(HttpClient client)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/api/server-applications/999/actions/1/run");
            request.Headers.Add("X-CSRF-Token", "csrf");
            request.Headers.Add("Origin", "http://localhost:3000");
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        for (var i = 0; i < 30; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, await RunAsync(one));

        Assert.Equal(HttpStatusCode.TooManyRequests, await RunAsync(one));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await RunAsync(two));
    }

    [Fact]
    public async Task Concurrent_logins_do_not_exceed_the_session_limit()
    {
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            using var scope = _factory.Services.CreateScope();
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
            sessions.Create(Data("race.user"));
        }));

        await Task.WhenAll(tasks);

        using var check = _factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        Assert.Equal(3, await db.Sessions.CountAsync(x => x.Username == "race.user"));
    }

    private static SessionData Data(string username) => new(
        username, "Test", "User", null,
        DateTimeOffset.UtcNow.AddHours(1), null, "csrf", []);

    private async Task<string> CreateSessionAsync(
        string username,
        string? role = null,
        DateTimeOffset? accessExpiresAt = null,
        string? refreshToken = null,
        DateTimeOffset? refreshExpiresAt = null,
        bool desktop = false)
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

        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        return sessions.Create(new SessionData(
            username,
            "Test",
            "User",
            refreshToken,
            accessExpiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
            refreshExpiresAt,
            "csrf",
            role is null ? [] : [role],
            desktop));
    }

    private static string SessionIdFrom(HttpResponseMessage response)
    {
        var name = SessionCookie.Session(false);
        var header = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(name + "=", StringComparison.Ordinal));
        return header.Split(';')[0][(name.Length + 1)..];
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
        public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }
        public List<Dictionary<string, string>> Forms { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                var raw = await request.Content.ReadAsStringAsync(cancellationToken);
                Forms.Add(raw.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(pair => pair.Split('=', 2))
                    .ToDictionary(
                        parts => Uri.UnescapeDataString(parts[0]),
                        parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : ""));
            }

            if (Responder is not null)
                return Responder(request);

            var body = JsonSerializer.Serialize(new
            {
                access_token = "unused-access-token",
                refresh_token = "new-refresh-token",
                id_token = "header.payload.signature",
                expires_in = 300,
                refresh_expires_in = 1800
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}

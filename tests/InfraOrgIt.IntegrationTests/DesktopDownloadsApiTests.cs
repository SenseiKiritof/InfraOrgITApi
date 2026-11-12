using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

public sealed class DesktopDownloadsApiTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("infraorgit")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly FakeFileTransfer _files = new();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        _factory = BuildFactory();

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
    public async Task Capabilities_require_authentication_and_report_signed_url()
    {
        var suffix = Unique();
        var username = "desk.caps." + suffix;
        await SeedUserAsync(username);

        using var anonymous = _factory.CreateClient();
        var unauthenticated = await anonymous.GetAsync(
            "/api/desktop/downloads/capabilities");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var session = await SessionClientAsync(_factory, username);
        var fromCookie = await ReadAsync(await session.GetAsync(
            "/api/desktop/downloads/capabilities"));
        Assert.True(fromCookie.GetProperty("signedUrl").GetBoolean());

        var token = await CreateApiTokenAsync(username);
        using var bearer = _factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        var fromBearer = await ReadAsync(await bearer.GetAsync(
            "/api/desktop/downloads/capabilities"));
        Assert.True(fromBearer.GetProperty("signedUrl").GetBoolean());
    }

    [Fact]
    public async Task Ticket_creation_requires_csrf_rbac_and_normalizes_path()
    {
        var suffix = Unique();
        var seeded = await SeedServerAsync(suffix);
        var username = "desk.ticket." + suffix;
        var outsider = "desk.outside." + suffix;
        await SeedUserAsync(username);
        await SeedUserAsync(outsider);
        await GrantEnvironmentRoleAsync(username, seeded.ProviderEnvironmentId, "ServerAdmin");
        using var client = await SessionClientAsync(_factory, username);
        using var other = await SessionClientAsync(_factory, outsider);

        var missingCsrf = await client.PostAsJsonAsync(
            "/api/desktop/downloads/tickets",
            new { serverId = seeded.ServerId, path = "/notes.txt" });
        Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
        Assert.Equal("csrf_failed", await ReadCodeAsync(missingCsrf));

        var root = await SendAsync(client, HttpMethod.Post,
            "/api/desktop/downloads/tickets",
            new { serverId = seeded.ServerId, path = "/" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, root.StatusCode);

        var denied = await SendAsync(other, HttpMethod.Post,
            "/api/desktop/downloads/tickets",
            new { serverId = seeded.ServerId, path = "/notes.txt" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var token = await CreateApiTokenAsync(username);
        using var bearer = _factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        var bearerAttempt = await SendAsync(bearer, HttpMethod.Post,
            "/api/desktop/downloads/tickets",
            new { serverId = seeded.ServerId, path = "/notes.txt" });
        Assert.Equal(HttpStatusCode.Unauthorized, bearerAttempt.StatusCode);

        var created = await SendAsync(client, HttpMethod.Post,
            "/api/desktop/downloads/tickets",
            new { serverId = seeded.ServerId, path = "/safe/../secret.txt" });
        var body = await ReadAsync(created);
        var url = body.GetProperty("url").GetString()!;
        Assert.StartsWith("http://localhost/api/desktop/downloads/", url);
        Assert.True(body.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow.AddSeconds(90));

        var rawToken = url.Split('/').Last();
        Assert.StartsWith("dt_", rawToken);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var ticket = await db.DownloadTickets.AsNoTracking()
                .Where(x => x.ServerId == seeded.ServerId)
                .SingleAsync();
            Assert.Equal("/secret.txt", ticket.Path);
            Assert.Equal(seeded.ServerId, ticket.ServerId);
            Assert.Equal(64, ticket.TokenHash.Length);
            Assert.Matches("^[0-9a-f]{64}$", ticket.TokenHash);
            Assert.Equal(DownloadTicketTokens.Hash(rawToken), ticket.TokenHash);
            Assert.NotEqual(rawToken, ticket.TokenHash);
            Assert.Null(ticket.ConsumedAt);

            var deniedAudit = await db.AuditEvents.AsNoTracking()
                .Where(x => x.Kind == "FileTransfer" &&
                            x.Action == "file.download.ticket" &&
                            x.Outcome == "Denied" &&
                            x.ServerId == seeded.ServerId)
                .SingleAsync();
            Assert.Equal(outsider, deniedAudit.ActorUsername);
            Assert.Equal("forbidden", deniedAudit.ErrorCode);
            Assert.Equal(seeded.ServerId, deniedAudit.ServerId);
        }
    }

    [Fact]
    public async Task Ticket_download_is_one_time_and_uses_stored_path()
    {
        _files.Reset();
        _files.Content = _ => Encoding.UTF8.GetBytes("payload from server");

        var suffix = Unique();
        var seeded = await SeedServerAsync(suffix);
        var username = "desk.download." + suffix;
        await SeedUserAsync(username);
        await GrantEnvironmentRoleAsync(username, seeded.ProviderEnvironmentId, "ServerAdmin");
        using var client = await SessionClientAsync(_factory, username);

        var created = await SendAsync(client, HttpMethod.Post,
            "/api/desktop/downloads/tickets",
            new { serverId = seeded.ServerId, path = "/notes.txt" });
        var url = (await ReadAsync(created)).GetProperty("url").GetString()!;

        using var anonymous = _factory.CreateClient();
        var download = await anonymous.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("text/plain", download.Content.Headers.ContentType?.MediaType);
        var disposition = download.Content.Headers.GetValues("Content-Disposition").Single();
        Assert.Contains("attachment", disposition);
        Assert.Contains("notes.txt", disposition);
        Assert.Equal("payload from server", await download.Content.ReadAsStringAsync());
        Assert.Equal("/notes.txt", _files.LastPath);

        var second = await anonymous.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal("download_ticket_invalid", await ReadCodeAsync(second));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var ticket = await db.DownloadTickets.AsNoTracking()
            .Where(x => x.ServerId == seeded.ServerId)
            .SingleAsync();
        Assert.NotNull(ticket.ConsumedAt);
        Assert.Contains(await db.AuditEvents.AsNoTracking()
                .Where(x => x.Kind == "FileTransfer" && x.Action == "file.download")
                .ToListAsync(),
            x => x.Outcome == "Ok" &&
                 x.ServerId == seeded.ServerId &&
                 x.ActorUsername == username);
        Assert.Contains(await db.AuditEvents.AsNoTracking()
                .Where(x => x.Kind == "FileTransfer" && x.Action == "file.download")
                .ToListAsync(),
            x => x.Outcome == "Denied" && x.ErrorCode == "ticket_consumed");
    }

    [Fact]
    public async Task Download_denies_after_role_revocation()
    {
        var suffix = Unique();
        var seeded = await SeedServerAsync(suffix);
        var username = "desk.revoke." + suffix;
        await SeedUserAsync(username);
        await GrantEnvironmentRoleAsync(username, seeded.ProviderEnvironmentId, "ServerAdmin");
        using var client = await SessionClientAsync(_factory, username);

        var created = await SendAsync(client, HttpMethod.Post,
            "/api/desktop/downloads/tickets",
            new { serverId = seeded.ServerId, path = "/notes.txt" });
        var url = (await ReadAsync(created)).GetProperty("url").GetString()!;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            await db.ProviderEnvironmentGroupRoles
                .Where(x => x.ProviderEnvironmentId == seeded.ProviderEnvironmentId)
                .ExecuteDeleteAsync();
        }

        using var anonymous = _factory.CreateClient();
        var download = await anonymous.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal("download_ticket_invalid", await ReadCodeAsync(download));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            Assert.Contains(await db.AuditEvents.AsNoTracking()
                    .Where(x => x.Kind == "FileTransfer" &&
                                x.Action == "file.download" &&
                                x.Outcome == "Denied")
                    .ToListAsync(),
                x => x.ServerId == seeded.ServerId && x.ErrorCode == "forbidden");
        }
    }

    [Fact]
    public async Task Expired_and_consumed_tickets_are_rejected()
    {
        var suffix = Unique();
        var seeded = await SeedServerAsync(suffix);
        var username = "desk.expired." + suffix;
        var userId = await SeedUserAsync(username);
        var expiredRaw = "dt_expired_" + suffix;
        var consumedRaw = "dt_consumed_" + suffix;
        var now = DateTime.UtcNow;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            db.DownloadTickets.AddRange(
                new DownloadTicket
                {
                    TokenHash = DownloadTicketTokens.Hash(expiredRaw),
                    UserId = userId,
                    ServerId = seeded.ServerId,
                    Path = "/notes.txt",
                    ExpiresAt = now.AddMinutes(-1)
                },
                new DownloadTicket
                {
                    TokenHash = DownloadTicketTokens.Hash(consumedRaw),
                    UserId = userId,
                    ServerId = seeded.ServerId,
                    Path = "/notes.txt",
                    ExpiresAt = now.AddMinutes(10),
                    ConsumedAt = now.AddMinutes(-1)
                });
            await db.SaveChangesAsync();
        }

        using var anonymous = _factory.CreateClient();
        var expired = await anonymous.GetAsync(
            $"/api/desktop/downloads/{expiredRaw}");
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
        Assert.Equal("download_ticket_invalid", await ReadCodeAsync(expired));

        var consumed = await anonymous.GetAsync(
            $"/api/desktop/downloads/{consumedRaw}");
        Assert.Equal(HttpStatusCode.NotFound, consumed.StatusCode);
        Assert.Equal("download_ticket_invalid", await ReadCodeAsync(consumed));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var denials = await db.AuditEvents.AsNoTracking()
                .Where(x => x.Kind == "FileTransfer" &&
                            x.Action == "file.download" &&
                            x.Outcome == "Denied")
                .ToListAsync();
            Assert.Contains(denials, x => x.ErrorCode == "ticket_expired");
            Assert.Contains(denials, x => x.ErrorCode == "ticket_consumed");
        }
    }

    [Fact]
    public async Task Disabled_desktop_downloads_report_no_url_and_reject_tickets()
    {
        using var disabled = BuildFactory(enabled: false);
        var suffix = Unique();
        var username = "desk.disabled." + suffix;
        await SeedUserAsync(username);
        using var client = await SessionClientAsync(disabled, username);

        var capabilities = await ReadAsync(await client.GetAsync(
            "/api/desktop/downloads/capabilities"));
        Assert.False(capabilities.GetProperty("signedUrl").GetBoolean());

        var response = await SendAsync(client, HttpMethod.Post,
            "/api/desktop/downloads/tickets",
            new { serverId = 1, path = "/notes.txt" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("desktop_downloads_disabled", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task Ticket_issued_before_disabling_cannot_be_downloaded()
    {
        var suffix = Unique();
        var seeded = await SeedServerAsync(suffix);
        var username = "desk.switch." + suffix;
        await SeedUserAsync(username);
        await GrantEnvironmentRoleAsync(username, seeded.ProviderEnvironmentId, "ServerAdmin");
        using var client = await SessionClientAsync(_factory, username);

        var created = await SendAsync(client, HttpMethod.Post,
            "/api/desktop/downloads/tickets",
            new { serverId = seeded.ServerId, path = "/notes.txt" });
        var url = (await ReadAsync(created)).GetProperty("url").GetString()!;

        using var disabled = BuildFactory(enabled: false);
        using var anonymous = disabled.CreateClient();
        var download = await anonymous.GetAsync(url);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, download.StatusCode);
        Assert.Equal("desktop_downloads_disabled", await ReadCodeAsync(download));
        Assert.Equal("application/problem+json", download.Content.Headers.ContentType?.MediaType);
        Assert.Null(download.Content.Headers.ContentDisposition);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var ticket = await db.DownloadTickets.AsNoTracking()
            .Where(x => x.ServerId == seeded.ServerId)
            .SingleAsync();
        Assert.Null(ticket.ConsumedAt);
        Assert.Contains(await db.AuditEvents.AsNoTracking()
                .Where(x => x.Kind == "FileTransfer" &&
                            x.Action == "file.download" &&
                            x.Outcome == "Denied")
                .ToListAsync(),
            x => x.ServerId == seeded.ServerId &&
                 x.ErrorCode == "desktop_downloads_disabled");
    }

    [Fact]
    public async Task Ticket_url_uses_public_base_url_when_configured()
    {
        using var custom = BuildFactory(publicBaseUrl: "https://files.example.test/base/");
        var suffix = Unique();
        var seeded = await SeedServerAsync(suffix);
        var username = "desk.baseurl." + suffix;
        await SeedUserAsync(username);
        await GrantEnvironmentRoleAsync(username, seeded.ProviderEnvironmentId, "ServerAdmin");
        using var client = await SessionClientAsync(custom, username);

        var response = await SendAsync(client, HttpMethod.Post,
            "/api/desktop/downloads/tickets",
            new { serverId = seeded.ServerId, path = "/notes.txt" });
        var url = (await ReadAsync(response)).GetProperty("url").GetString()!;
        Assert.StartsWith("https://files.example.test/base/api/desktop/downloads/dt_", url);
    }

    [Fact]
    public async Task Slow_download_does_not_block_ticket_creation()
    {
        _files.Reset();
        _files.BlockDownloads = true;

        var suffix = Unique();
        var seeded = await SeedServerAsync(suffix);
        var username = "desk.slow." + suffix;
        await SeedUserAsync(username);
        await GrantEnvironmentRoleAsync(username, seeded.ProviderEnvironmentId, "ServerAdmin");
        using var client = await SessionClientAsync(_factory, username);

        var created = await SendAsync(client, HttpMethod.Post,
            "/api/desktop/downloads/tickets",
            new { serverId = seeded.ServerId, path = "/first.txt" });
        var url = (await ReadAsync(created)).GetProperty("url").GetString()!;

        using var downloadClient = _factory.CreateClient();
        var downloadTask = downloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        await _files.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            var secondTicket = SendAsync(client, HttpMethod.Post,
                "/api/desktop/downloads/tickets",
                new { serverId = seeded.ServerId, path = "/second.txt" });
            var completed = await Task.WhenAny(
                secondTicket, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(secondTicket, completed);
            Assert.Equal(HttpStatusCode.OK, (await secondTicket).StatusCode);
        }
        finally
        {
            _files.Release.TrySetResult();
            _files.BlockDownloads = false;
        }

        using var download = await downloadTask;
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("payload from server", await download.Content.ReadAsStringAsync());
    }

    private WebApplicationFactory<Program> BuildFactory(
        bool enabled = true,
        string? publicBaseUrl = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
            builder.UseSetting("DesktopDownloads:Enabled", enabled ? "true" : "false");
            if (!string.IsNullOrWhiteSpace(publicBaseUrl))
                builder.UseSetting("DesktopDownloads:PublicBaseUrl", publicBaseUrl);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISshConnector>();
                services.AddSingleton<ISshConnector>(new FakeSsh());
                services.RemoveAll<IFileTransferConnector>();
                services.AddSingleton<IFileTransferConnector>(_files);
            });
        });

    private async Task<SeededServer> SeedServerAsync(string suffix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var provider = new Provider { Name = "Desktop Provider " + suffix };
        var environment = new EnvironmentDefinition
        {
            Name = "Desktop Env " + suffix,
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
            Name = "Desktop Server " + suffix,
            IpAddress = "10.40.0.1",
            Hostname = "desktop-" + suffix + ".local",
            FileTransferProtocol = "Sftp",
            FileTransferPort = 22
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();

        return new SeededServer(server.Id, providerEnvironment.Id);
    }

    private async Task<int> SeedUserAsync(string username, bool systemAdmin = false)
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

        return user.Id;
    }

    private async Task GrantEnvironmentRoleAsync(
        string username,
        int providerEnvironmentId,
        string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var user = await db.Users.SingleAsync(x => x.Username == username);
        var group = new Group { Name = "Desktop Group " + Guid.NewGuid().ToString("N")[..8] };
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        db.GroupUsers.Add(new GroupUser { GroupsId = group.Id, UsersId = user.Id });
        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        db.ProviderEnvironmentGroupRoles.Add(new ProviderEnvironmentGroupRole
        {
            ProviderEnvironmentId = providerEnvironmentId,
            GroupId = group.Id,
            RoleId = role.Id
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> CreateApiTokenAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var userId = await db.Users.Where(x => x.Username == username)
            .Select(x => x.Id)
            .SingleAsync();
        var tokens = scope.ServiceProvider.GetRequiredService<ApiTokenService>();
        var created = await tokens.CreateAsync(
            userId, "desktop-test", ["mcp:read"], null, CancellationToken.None);
        return created.Token;
    }

    private static Task<HttpClient> SessionClientAsync(
        WebApplicationFactory<Program> factory,
        string username,
        bool systemAdmin = false)
    {
        using var scope = factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var sessionId = sessions.Create(new SessionData(
            username, "Test", "User", null,
            DateTimeOffset.UtcNow.AddHours(1), null, "csrf",
            systemAdmin ? ["SystemAdmin"] : []));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            "Cookie", $"{SessionCookie.Session(false)}={sessionId}");
        return Task.FromResult(client);
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

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private sealed record SeededServer(int ServerId, int ProviderEnvironmentId);

    private sealed class FakeFileTransfer : IFileTransferConnector
    {
        public Func<string, byte[]> Content { get; set; } =
            _ => Encoding.UTF8.GetBytes("payload from server");
        public bool BlockDownloads { get; set; }
        public string? LastPath { get; private set; }
        public TaskCompletionSource DownloadStarted { get; private set; } = NewSignal();
        public TaskCompletionSource Release { get; private set; } = NewSignal();

        public void Reset()
        {
            Content = _ => Encoding.UTF8.GetBytes("payload from server");
            BlockDownloads = false;
            LastPath = null;
            DownloadStarted = NewSignal();
            Release = NewSignal();
        }

        public Task<IFileTransferSession> ConnectAsync(
            Server server, CancellationToken ct) =>
            Task.FromResult<IFileTransferSession>(new Session(this));

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class Session(FakeFileTransfer owner) : IFileTransferSession
        {
            public Task<IReadOnlyList<RemoteFileEntry>> ListAsync(
                string path, CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<RemoteFileEntry>>([]);

            public async Task DownloadAsync(
                string path, Stream destination, CancellationToken ct)
            {
                owner.LastPath = path;
                owner.DownloadStarted.TrySetResult();
                if (owner.BlockDownloads)
                    await owner.Release.Task.WaitAsync(ct);
                await destination.WriteAsync(owner.Content(path), ct);
            }

            public Task UploadAsync(
                string path, Stream source, CancellationToken ct) =>
                Task.CompletedTask;

            public Task CreateDirectoryAsync(string path, CancellationToken ct) =>
                Task.CompletedTask;

            public Task RenameAsync(
                string path, string newPath, bool isDirectory, CancellationToken ct) =>
                Task.CompletedTask;

            public Task DeleteAsync(
                string path, bool isDirectory, CancellationToken ct) =>
                Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSsh : ISshConnector
    {
        public Task<ISshSession> ConnectAsync(Server server, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IFileTransferSession> OpenSftpAsync(
            Server server, ServerCredential credential, int port, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}

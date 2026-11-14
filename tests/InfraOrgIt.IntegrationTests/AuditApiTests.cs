using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.Features.Audit;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace InfraOrgIt.IntegrationTests;

public sealed class AuditApiTests : IAsyncLifetime
{

    private string _connectionString = "";
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await TestMySql.CreateDatabaseAsync("AuditApiTests");

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
    public async Task Audit_events_are_system_admin_only_and_filtered()
    {
        using var outsider = await SessionClientAsync("audit.outsider");
        var denied = await outsider.GetAsync("/api/audit-events");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var seeded = await SeedAuditEventsAsync();
        using var admin = await SessionClientAsync("audit.admin", systemAdmin: true);

        var filtered = await admin.GetFromJsonAsync<JsonElement>(
            "/api/audit-events?kind=McpToolCall&outcome=Denied&q=alice&serverId=42&page=1&pageSize=10");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal(1, filtered.GetProperty("page").GetInt32());
        Assert.Equal(10, filtered.GetProperty("pageSize").GetInt32());
        var item = filtered.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(seeded.DeniedId, item.GetProperty("id").GetInt32());
        Assert.Equal("McpToolCall", item.GetProperty("kind").GetString());
        Assert.Equal("alice", item.GetProperty("actorUsername").GetString());
        Assert.Equal("token-1", item.GetProperty("clientId").GetString());
        Assert.Equal("list_executions", item.GetProperty("action").GetString());
        Assert.Equal(42, item.GetProperty("serverId").GetInt32());
        Assert.Equal("Denied", item.GetProperty("outcome").GetString());
        Assert.Equal("forbidden", item.GetProperty("errorCode").GetString());
        Assert.True(item.TryGetProperty("occurredAt", out _));
        Assert.False(item.TryGetProperty("detailJson", out _));

        var byClient = await admin.GetFromJsonAsync<JsonElement>(
            "/api/audit-events?q=token-1");
        Assert.Equal(1, byClient.GetProperty("total").GetInt32());

        var firstPage = await admin.GetFromJsonAsync<JsonElement>(
            "/api/audit-events?kind=McpToolCall&page=1&pageSize=2");
        Assert.Equal(3, firstPage.GetProperty("total").GetInt32());
        Assert.Equal(
            [seeded.DeniedId, seeded.OkId],
            firstPage.GetProperty("items").EnumerateArray()
                .Select(x => x.GetProperty("id").GetInt32()).ToArray());

        var secondPage = await admin.GetFromJsonAsync<JsonElement>(
            "/api/audit-events?kind=McpToolCall&page=2&pageSize=2");
        Assert.Equal(
            [seeded.OldId],
            secondPage.GetProperty("items").EnumerateArray()
                .Select(x => x.GetProperty("id").GetInt32()).ToArray());
    }

    [Fact]
    public async Task Audit_retention_deletes_only_rows_older_than_the_retention()
    {
        int oldId;
        int recentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var old = new AuditEvent
            {
                OccurredAt = new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                Kind = "McpToolCall",
                ActorUsername = "old",
                Action = "list_servers",
                Outcome = "Ok"
            };
            var recent = new AuditEvent
            {
                OccurredAt = DateTime.UtcNow,
                Kind = "McpToolCall",
                ActorUsername = "recent",
                Action = "list_servers",
                Outcome = "Ok"
            };
            db.AuditEvents.AddRange(old, recent);
            await db.SaveChangesAsync();
            oldId = old.Id;
            recentId = recent.Id;
        }

        var service = _factory.Services.GetServices<IHostedService>()
            .OfType<AuditRetentionService>()
            .Single();
        var deleted = await service.PurgeAsync(CancellationToken.None);
        Assert.Equal(1, deleted);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            Assert.False(await db.AuditEvents.AnyAsync(x => x.Id == oldId));
            Assert.True(await db.AuditEvents.AnyAsync(x => x.Id == recentId));
        }
    }

    [Fact]
    public async Task Audit_chain_verifies_and_detects_tampering()
    {
        using var outsider = await SessionClientAsync("chain.outsider");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await outsider.GetAsync("/api/audit-events/verify")).StatusCode);

        int legacyId;
        int tamperedId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var legacy = new AuditEvent
            {
                OccurredAt = new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                Kind = "McpToolCall",
                ActorUsername = "legacy",
                Action = "legacy.action",
                Outcome = "Ok"
            };
            db.AuditEvents.Add(legacy);
            await db.SaveChangesAsync();
            legacyId = legacy.Id;

            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            for (var i = 0; i < 3; i++)
                await audit.RecordAsync(new AuditEntry(
                    "McpToolCall", $"chain.action.{i}", "Ok",
                    ActorUsername: "chain"));

            var hashed = await db.AuditEvents.AsNoTracking()
                .Where(x => x.Hash != null)
                .OrderBy(x => x.Id)
                .ToListAsync();
            Assert.Equal(3, hashed.Count);
            Assert.Equal(AuditChain.Genesis, hashed[0].PrevHash);
            Assert.Equal(hashed[0].Hash, hashed[1].PrevHash);
            Assert.Equal(hashed[1].Hash, hashed[2].PrevHash);
            Assert.All(hashed, row =>
            {
                Assert.NotNull(row.PrevHash);
                Assert.NotNull(row.Hash);
                Assert.Equal(64, row.Hash!.Length);
            });
            tamperedId = hashed[1].Id;
        }

        using var admin = await SessionClientAsync("chain.admin", systemAdmin: true);
        var verified = await admin.GetFromJsonAsync<JsonElement>(
            "/api/audit-events/verify");
        Assert.True(verified.GetProperty("valid").GetBoolean());
        Assert.Equal(4, verified.GetProperty("checked").GetInt32());
        Assert.Equal(JsonValueKind.Null,
            verified.GetProperty("firstBrokenId").ValueKind);
        Assert.Equal("Righe legacy senza hash ignorate: 1.",
            verified.GetProperty("message").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            await db.AuditEvents
                .Where(x => x.Id == tamperedId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(x => x.Action, "chain.action.tampered"));
        }

        var tampered = await admin.GetFromJsonAsync<JsonElement>(
            "/api/audit-events/verify");
        Assert.False(tampered.GetProperty("valid").GetBoolean());
        Assert.Equal(3, tampered.GetProperty("checked").GetInt32());
        Assert.Equal(tamperedId,
            tampered.GetProperty("firstBrokenId").GetInt32());
        Assert.Contains($"riga {tamperedId}",
            tampered.GetProperty("message").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            Assert.True(await db.AuditEvents.AnyAsync(x => x.Id == legacyId));
        }
    }

    private async Task<SeededAudit> SeedAuditEventsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();

        var old = new AuditEvent
        {
            OccurredAt = new DateTime(2025, 12, 1, 10, 0, 0, DateTimeKind.Utc),
            Kind = "McpToolCall",
            ActorUsername = "alice",
            Action = "list_servers",
            Outcome = "Ok"
        };
        var ok = new AuditEvent
        {
            OccurredAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            Kind = "McpToolCall",
            ActorUsername = "alice",
            Action = "list_providers",
            Outcome = "Ok"
        };
        var denied = new AuditEvent
        {
            OccurredAt = new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc),
            Kind = "McpToolCall",
            ActorUsername = "alice",
            ClientId = "token-1",
            Action = "list_executions",
            ServerId = 42,
            Outcome = "Denied",
            ErrorCode = "forbidden"
        };
        var tunnel = new AuditEvent
        {
            OccurredAt = new DateTime(2026, 1, 3, 10, 0, 0, DateTimeKind.Utc),
            Kind = "TunnelStart",
            ActorUsername = "bob",
            Action = "start",
            ServerId = 42,
            Outcome = "Ok"
        };

        db.AuditEvents.AddRange(old, ok, denied, tunnel);
        await db.SaveChangesAsync();
        return new SeededAudit(old.Id, ok.Id, denied.Id);
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

    private sealed record SeededAudit(int OldId, int OkId, int DeniedId);
}

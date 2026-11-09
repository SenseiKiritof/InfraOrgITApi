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
        }

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
        return new SeededServer(server.Id, server.Name);
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

    private sealed record SeededServer(int Id, string Name);
}

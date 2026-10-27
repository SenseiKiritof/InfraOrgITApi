using System.Security.Claims;
using System.Text.RegularExpressions;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Remote;

public static partial class ReachabilityEndpoints
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [GeneratedRegex(@"^[A-Za-z0-9.:\-]{1,200}$")]
    private static partial Regex ValidHost();

    public static void Map(WebApplication app)
    {
        var global = app.MapGroup("/api/reachability-tests").RequireAuthorization();
        global.MapGet("", ListGlobalAsync);
        global.MapPost("", CreateGlobalAsync);
        global.MapPut("/{id:int}", UpdateAsync);
        global.MapDelete("/{id:int}", DeleteAsync);

        var servers = app.MapGroup("/api/servers").RequireAuthorization();
        servers.MapGet("/{id:int}/reachability-tests", ListForServerAsync);
        servers.MapPost("/{id:int}/reachability-tests", CreateForServerAsync);
        servers.MapPost("/{id:int}/reachability-tests/run", RunForServerAsync)
            .RequireRateLimiting("reachability");

        var environments = app.MapGroup("/api/provider-environments").RequireAuthorization();
        environments.MapPost("/{id:int}/reachability-tests/run", RunForEnvironmentAsync)
            .RequireRateLimiting("reachability");
    }

    private static async Task<IResult> ListGlobalAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var items = await db.ReachabilityTests.AsNoTracking()
            .Where(x => x.ServerId == null)
            .OrderBy(x => x.Hostname)
            .ToListAsync(ct);
        return Results.Ok(items.Select(ToDto).ToList());
    }

    private static async Task<IResult> CreateGlobalAsync(
        ReachabilityTestInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        return await CreateAsync(input, null, db, ct);
    }

    private static async Task<IResult> CreateForServerAsync(
        int id,
        ReachabilityTestInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking()
                         .SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Server", id);
        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);
        return await CreateAsync(input, id, db, ct);
    }

    private static async Task<IResult> CreateAsync(
        ReachabilityTestInput input,
        int? serverId,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = new ReachabilityTest
        {
            ServerId = serverId,
            Hostname = ValidateHost(input.Hostname),
            Port = Guard.Port(input.Port, "Port"),
            Description = Guard.Text(input.Description, "Description", 200, required: false)
        };
        db.ReachabilityTests.Add(entity);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(entity));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ReachabilityTestInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.ReachabilityTests.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Test", id);
        await RequireAdminForAsync(user, access, db, entity.ServerId, ct);

        entity.Hostname = ValidateHost(input.Hostname);
        entity.Port = Guard.Port(input.Port, "Port");
        entity.Description = Guard.Text(input.Description, "Description", 200, required: false);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.ReachabilityTests.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Test", id);
        await RequireAdminForAsync(user, access, db, entity.ServerId, ct);

        db.ReachabilityTests.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListForServerAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking()
                         .SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Server", id);
        await access.RequireOperatorAsync(user, [server.ProviderEnvironmentId], ct);

        var items = await db.ReachabilityTests.AsNoTracking()
            .Where(x => x.ServerId == id)
            .OrderBy(x => x.Hostname)
            .ToListAsync(ct);
        return Results.Ok(items.Select(ToDto).ToList());
    }

    private static async Task<IResult> RunForServerAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        ISshConnector ssh,
        IOptions<ServerAccessOptions> options,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var server = await LoadServerAsync(db, id, ct);
        await access.RequireOperatorAsync(user, [server.ProviderEnvironmentId], ct);
        var results = await RunOnServersAsync(user, [server], ssh, options.Value, db, ct);
        return Results.Ok(results);
    }

    private static async Task<IResult> RunForEnvironmentAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        ISshConnector ssh,
        IOptions<ServerAccessOptions> options,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        if (!await db.ProviderEnvironments.AnyAsync(x => x.Id == id, ct))
            throw Guard.NotFound("Ambiente provider", id);

        await access.RequireOperatorAsync(user, [id], ct);
        var servers = await db.Servers
            .Include(x => x.ProviderEnvironment)
            .Include(x => x.Credential)
            .Include(x => x.ReachabilityTests)
            .Where(x => x.ProviderEnvironmentId == id)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        var results = await RunOnServersAsync(user, servers, ssh, options.Value, db, ct);
        return Results.Ok(results);
    }

    private static async Task<List<ReachabilityResultDto>> RunOnServersAsync(
        ClaimsPrincipal user,
        IReadOnlyCollection<Server> servers,
        ISshConnector ssh,
        ServerAccessOptions options,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var globals = await db.ReachabilityTests.AsNoTracking()
            .Where(x => x.ServerId == null)
            .OrderBy(x => x.Hostname)
            .ToListAsync(ct);

        var username = user.Identity?.Name ?? "";
        var results = new List<ReachabilityResultDto>();

        foreach (var server in servers)
        {
            var tests = globals.Concat(server.ReachabilityTests).ToList();
            if (tests.Count == 0)
                continue;

            var startedAt = DateTime.UtcNow;
            var rows = new List<ReachabilityResultDto>();
            try
            {
                await using var session = await ssh.ConnectAsync(server, ct);
                await db.SaveChangesAsync(ct);

                foreach (var test in tests)
                {
                    var result = await session.RunAsync(
                        Script(test), TestTimeout, ct);
                    rows.Add(new ReachabilityResultDto(
                        server.Id,
                        server.Name,
                        test.Hostname,
                        test.Port,
                        result.ExitCode == 0,
                        result.ExitCode == 0 ? null : result.Output.Trim()));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                rows.Add(new ReachabilityResultDto(
                    server.Id, server.Name, null, null, false, exception.Message));
            }

            results.AddRange(rows);
            var summary = string.Join("\n", rows.Select(row =>
                $"{row.Hostname}:{row.Port} {(row.Reachable ? "OK" : "KO")} {row.Error}".TrimEnd()));
            var execution = CommandAudit.Add(
                db, username, server, "Reachability", "Test di raggiungibilità",
                "", startedAt, new SshCommandResult(rows.All(x => x.Reachable) ? 0 : 1, summary),
                options);
            await db.SaveChangesAsync(ct);
        }

        return results.OrderBy(x => x.ServerName).ThenBy(x => x.Hostname).ToList();
    }

    private static async Task RequireAdminForAsync(
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        int? serverId,
        CancellationToken ct)
    {
        if (serverId is null)
        {
            EnvironmentAccessService.RequireSystemAdmin(user);
            return;
        }

        var environmentId = await db.Servers
            .Where(x => x.Id == serverId)
            .Select(x => (int?)x.ProviderEnvironmentId)
            .SingleOrDefaultAsync(ct)
            ?? throw Guard.NotFound("Server", serverId.Value);
        await access.RequireAdminAsync(user, [environmentId], ct);
    }

    private static async Task<Server> LoadServerAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.Servers
            .Include(x => x.ProviderEnvironment)
            .Include(x => x.Credential)
            .Include(x => x.ReachabilityTests)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw Guard.NotFound("Server", id);

    private static string ValidateHost(string? hostname)
    {
        var value = Guard.Text(hostname, "Hostname", 200);
        if (!ValidHost().IsMatch(value))
            throw Guard.Unprocessable("Hostname non valido: lettere, cifre, punto, trattino e due punti.");
        return value;
    }

    private static string Script(ReachabilityTest test) =>
        $"timeout 5 bash -c '</dev/tcp/{test.Hostname}/{test.Port}'";

    private static ReachabilityTestDto ToDto(ReachabilityTest test) => new(
        test.Id, test.ServerId, test.Hostname, test.Port, test.Description);
}

public sealed record ReachabilityTestInput(string? Hostname, int Port, string? Description);

public sealed record ReachabilityTestDto(
    int Id,
    int? ServerId,
    string Hostname,
    int Port,
    string Description);

public sealed record ReachabilityResultDto(
    int ServerId,
    string ServerName,
    string? Hostname,
    int? Port,
    bool Reachable,
    string? Error);

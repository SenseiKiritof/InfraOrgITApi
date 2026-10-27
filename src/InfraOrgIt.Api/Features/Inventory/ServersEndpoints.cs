using System.Linq.Expressions;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Inventory;

public static class ServersEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/servers").RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
        group.MapPut("/{id:int}/provider-environment/{providerEnvironmentId:int}", MoveAsync);
        group.MapGet("/{id:int}/applications", ListApplicationsAsync);
        group.MapPost("/{id:int}/applications", CreateApplicationAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var environmentId = PageRequest.IntQuery(http, "environmentId");

        var query = db.Servers.AsNoTracking();
        if (environmentId is not null)
            query = query.Where(x => x.ProviderEnvironmentId == environmentId);

        if (page.Q is not null)
            query = query.Where(x => x.Name.Contains(page.Q) ||
                                     x.IpAddress.Contains(page.Q) ||
                                     x.Hostname.Contains(page.Q) ||
                                     x.ProviderEnvironment!.Provider!.Name.Contains(page.Q) ||
                                     x.ProviderEnvironment!.EnvironmentDefinition!.Name.Contains(page.Q) ||
                                     (x.Credential != null && x.Credential.Name.Contains(page.Q)) ||
                                     (x.JumpServer != null && x.JumpServer.Name.Contains(page.Q)));

        query = page.Sort switch
        {
            "name" => query.OrderBy(x => x.Name),
            "-name" => query.OrderByDescending(x => x.Name),
            _ => query.OrderBy(x => x.Id)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(ToDto)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ServerDto>(items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(int id, InfraOrgItDbContext db, CancellationToken ct)
    {
        var dto = await db.Servers.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Server", id) : Results.Ok(dto);
    }

    private static async Task<IResult> CreateAsync(
        ServerInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        await access.RequireAdminAsync(user, [input.ProviderEnvironmentId], ct);

        var entity = new Server
        {
            ProviderEnvironmentId = input.ProviderEnvironmentId,
            Name = Guard.Text(input.Name, "Name", 200),
            IpAddress = Guard.Text(input.IpAddress, "IpAddress", 200),
            Hostname = Guard.Text(input.Hostname, "Hostname", 200),
            SshPort = input.SshPort == 0 ? 22 : Guard.Port(input.SshPort, "SshPort"),
            JumpServerId = input.JumpServerId
        };

        await EnsureEnvironmentAsync(db, entity.ProviderEnvironmentId, ct);
        await EnsureJumpServerAsync(db, input.JumpServerId, null, ct);
        db.Servers.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await LoadAsync(db, entity.Id, ct));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ServerInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.Servers.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Server", id);

        await EnsureCanWriteAsync(entity, input, user, access, ct);
        await EnsureEnvironmentAsync(db, input.ProviderEnvironmentId, ct);
        await EnsureJumpServerAsync(db, input.JumpServerId, entity.Id, ct);

        entity.ProviderEnvironmentId = input.ProviderEnvironmentId;
        entity.Name = Guard.Text(input.Name, "Name", 200);
        entity.IpAddress = Guard.Text(input.IpAddress, "IpAddress", 200);
        entity.Hostname = Guard.Text(input.Hostname, "Hostname", 200);
        entity.SshPort = input.SshPort == 0 ? 22 : Guard.Port(input.SshPort, "SshPort");
        entity.JumpServerId = input.JumpServerId;

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
        var entity = await db.Servers.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Server", id);

        await access.RequireAdminAsync(user, [entity.ProviderEnvironmentId], ct);

        var applications = await db.ServerApplications.CountAsync(x => x.ServerId == id, ct);
        if (applications > 0)
            throw Guard.Conflict($"Server con {applications} applicazioni deployate: rimuoverle prima.");

        var dependents = await db.Servers.CountAsync(x => x.JumpServerId == id, ct);
        if (dependents > 0)
            throw Guard.Conflict($"Server usato come proxy da {dependents} server: rimuovere il collegamento prima.");

        db.Servers.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> MoveAsync(
        int id,
        int providerEnvironmentId,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.Servers.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Server", id);

        if (entity.ProviderEnvironmentId == providerEnvironmentId)
            return Results.NoContent();

        if (entity.CredentialId is not null)
        {
            EnvironmentAccessService.RequireSystemAdmin(user);
        }
        else
        {
            await access.RequireAdminAsync(
                user, [entity.ProviderEnvironmentId, providerEnvironmentId], ct);
        }

        await EnsureEnvironmentAsync(db, providerEnvironmentId, ct);
        entity.ProviderEnvironmentId = providerEnvironmentId;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListApplicationsAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var exists = await db.Servers.AnyAsync(x => x.Id == id, ct);
        if (!exists)
            throw Guard.NotFound("Server", id);

        var items = await db.ServerApplications.AsNoTracking()
            .Where(x => x.ServerId == id)
            .OrderBy(x => x.Id)
            .Select(ServerApplicationsEndpoints.ToDto)
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateApplicationAsync(
        int id,
        ServerApplicationDeployInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Server", id);

        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);
        await ServerApplicationsEndpoints.EnsureVersionAsync(db, input.ApplicationVersionId, ct);

        var entity = new ServerApplication
        {
            ServerId = id,
            ApplicationVersionId = input.ApplicationVersionId,
            ExposedPort = Guard.Port(input.ExposedPort, "ExposedPort")
        };
        db.ServerApplications.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await ServerApplicationsEndpoints.LoadAsync(db, entity.Id, ct));
    }

    private static async Task EnsureCanWriteAsync(
        Server entity,
        ServerInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        CancellationToken ct)
    {
        var changesIdentity = entity.ProviderEnvironmentId != input.ProviderEnvironmentId ||
                              entity.IpAddress != input.IpAddress?.Trim() ||
                              entity.Hostname != input.Hostname?.Trim();

        if (entity.CredentialId is not null && changesIdentity)
        {
            EnvironmentAccessService.RequireSystemAdmin(user);
            return;
        }

        await access.RequireAdminAsync(
            user, [entity.ProviderEnvironmentId, input.ProviderEnvironmentId], ct);
    }

    private static async Task EnsureJumpServerAsync(
        InfraOrgItDbContext db,
        int? jumpServerId,
        int? selfId,
        CancellationToken ct)
    {
        var current = jumpServerId;
        var seen = new HashSet<int>();

        while (current is int id)
        {
            if (id == selfId || !seen.Add(id))
                throw Guard.Unprocessable(
                    "La catena dei server proxy è ciclica.");

            var next = await db.Servers
                .Where(x => x.Id == id)
                .Select(x => new { x.JumpServerId })
                .SingleOrDefaultAsync(ct)
                ?? throw Guard.NotFound("Server proxy", id);

            current = next.JumpServerId;
        }
    }

    private static async Task EnsureEnvironmentAsync(
        InfraOrgItDbContext db, int providerEnvironmentId, CancellationToken ct)
    {
        if (!await db.ProviderEnvironments.AnyAsync(x => x.Id == providerEnvironmentId, ct))
            throw Guard.NotFound("Ambiente provider", providerEnvironmentId);
    }

    private static async Task<ServerDto> LoadAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.Servers.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleAsync(ct);

    private static readonly Expression<Func<Server, ServerDto>> ToDto = x => new ServerDto(
        x.Id,
        x.ProviderEnvironmentId,
        x.ProviderEnvironment!.Provider!.Name + " / " + x.ProviderEnvironment!.EnvironmentDefinition!.Name,
        x.Name,
        x.IpAddress,
        x.Hostname,
        x.SshPort,
        x.CredentialId,
        x.Credential != null ? x.Credential.Name : null,
        x.HostKeyFingerprint != null,
        x.JumpServerId,
        x.JumpServer != null ? x.JumpServer.Name : null,
        x.JumpServer != null ? x.JumpServer.IpAddress : null,
        x.CreatedAt,
        x.UpdatedAt,
        x.Version);
}

public sealed record ServerInput(
    int ProviderEnvironmentId,
    string? Name,
    string? IpAddress,
    string? Hostname,
    int SshPort = 22,
    int? JumpServerId = null);

public sealed record ServerApplicationDeployInput(int ApplicationVersionId, int ExposedPort);

public sealed record ServerDto(
    int Id,
    int ProviderEnvironmentId,
    string ProviderEnvironmentLabel,
    string Name,
    string IpAddress,
    string Hostname,
    int SshPort,
    int? CredentialId,
    string? CredentialName,
    bool HasHostKey,
    int? JumpServerId,
    string? JumpServerName,
    string? JumpServerIpAddress,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

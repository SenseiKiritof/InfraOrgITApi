using System.Linq.Expressions;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Inventory;

public static class ServerApplicationsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/server-applications").RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var serverId = PageRequest.IntQuery(http, "serverId");
        var applicationId = PageRequest.IntQuery(http, "applicationId");

        var query = db.ServerApplications.AsNoTracking();
        if (serverId is not null)
            query = query.Where(x => x.ServerId == serverId);
        if (applicationId is not null)
            query = query.Where(x => x.ApplicationVersion!.ApplicationId == applicationId);

        if (page.Q is not null)
            query = query.Where(x => x.Server!.Name.Contains(page.Q) ||
                                     x.ApplicationVersion!.Application!.Name.Contains(page.Q));

        query = page.Sort switch
        {
            "server" => query.OrderBy(x => x.Server!.Name).ThenBy(x => x.Id),
            "-server" => query.OrderByDescending(x => x.Server!.Name).ThenBy(x => x.Id),
            "application" => query.OrderBy(x => x.ApplicationVersion!.Application!.Name).ThenBy(x => x.Id),
            "-application" => query.OrderByDescending(x => x.ApplicationVersion!.Application!.Name).ThenBy(x => x.Id),
            _ => query.OrderBy(x => x.Id)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(ToDto)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ServerApplicationDto>(items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(int id, InfraOrgItDbContext db, CancellationToken ct)
    {
        var dto = await db.ServerApplications.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Applicazione deployata", id) : Results.Ok(dto);
    }

    private static async Task<IResult> CreateAsync(
        ServerApplicationInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking()
                         .SingleOrDefaultAsync(x => x.Id == input.ServerId, ct)
                     ?? throw Guard.NotFound("Server", input.ServerId);

        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);
        await EnsureVersionAsync(db, input.ApplicationVersionId, ct);
        await EnsureProxyRouteAsync(db, input.ProxyRouteId, ct);

        var entity = new ServerApplication
        {
            ServerId = input.ServerId,
            ApplicationVersionId = input.ApplicationVersionId,
            ExposedPort = Guard.Port(input.ExposedPort, "ExposedPort"),
            ProxyRouteId = input.ProxyRouteId
        };
        db.ServerApplications.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await LoadAsync(db, entity.Id, ct));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ServerApplicationInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.ServerApplications.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Applicazione deployata", id);

        var originalServer = await db.Servers.AsNoTracking()
                                 .SingleAsync(x => x.Id == entity.ServerId, ct);
        var targetServer = await db.Servers.AsNoTracking()
                               .SingleOrDefaultAsync(x => x.Id == input.ServerId, ct)
                           ?? throw Guard.NotFound("Server", input.ServerId);

        await access.RequireAdminAsync(
            user, [originalServer.ProviderEnvironmentId, targetServer.ProviderEnvironmentId], ct);
        await EnsureVersionAsync(db, input.ApplicationVersionId, ct);
        await EnsureProxyRouteAsync(db, input.ProxyRouteId, ct);

        entity.ServerId = input.ServerId;
        entity.ApplicationVersionId = input.ApplicationVersionId;
        entity.ExposedPort = Guard.Port(input.ExposedPort, "ExposedPort");
        entity.ProxyRouteId = input.ProxyRouteId;
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
        var entity = await db.ServerApplications.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Applicazione deployata", id);

        var server = await db.Servers.AsNoTracking().SingleAsync(x => x.Id == entity.ServerId, ct);
        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);

        var routes = await db.ProxyRoutes
            .Where(x => x.TargetServerApplicationId == id)
            .ToListAsync(ct);
        foreach (var route in routes)
            route.TargetServerApplicationId = null;

        db.ServerApplications.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task EnsureProxyRouteAsync(
        InfraOrgItDbContext db, int? proxyRouteId, CancellationToken ct)
    {
        if (proxyRouteId is not null &&
            !await db.ProxyRoutes.AnyAsync(x => x.Id == proxyRouteId, ct))
            throw Guard.NotFound("Route proxy", proxyRouteId.Value);
    }

    internal static async Task EnsureVersionAsync(
        InfraOrgItDbContext db, int applicationVersionId, CancellationToken ct)
    {
        if (!await db.ApplicationVersions.AnyAsync(x => x.Id == applicationVersionId, ct))
            throw Guard.NotFound("Versione applicazione", applicationVersionId);
    }

    internal static async Task<ServerApplicationDto> LoadAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.ServerApplications.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleAsync(ct);

    internal static readonly Expression<Func<ServerApplication, ServerApplicationDto>> ToDto =
        x => new ServerApplicationDto(
            x.Id,
            x.ServerId,
            x.Server!.Name,
            x.ApplicationVersionId,
            x.ApplicationVersion!.ApplicationId,
            x.ApplicationVersion!.Application!.Name,
            x.ApplicationVersion!.VersionNumber,
            x.ExposedPort,
            x.ProxyRouteId,
            x.CreatedAt,
            x.UpdatedAt,
            x.Version);
}

public sealed record ServerApplicationInput(
    int ServerId,
    int ApplicationVersionId,
    int ExposedPort,
    int? ProxyRouteId = null);

public sealed record ServerApplicationDto(
    int Id,
    int ServerId,
    string ServerName,
    int ApplicationVersionId,
    int ApplicationId,
    string ApplicationName,
    string VersionNumber,
    int ExposedPort,
    int? ProxyRouteId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

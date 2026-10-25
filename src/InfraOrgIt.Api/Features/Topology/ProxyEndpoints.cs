using System.Linq.Expressions;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Topology;

public static class ProxyEndpoints
{
    public static void Map(WebApplication app)
    {
        var connections = app.MapGroup("/api/proxy-connections").RequireAuthorization();
        connections.MapGet("", ListConnectionsAsync);
        connections.MapGet("/{id:int}", GetConnectionAsync);
        connections.MapPost("", CreateConnectionAsync);
        connections.MapPut("/{id:int}", UpdateConnectionAsync);
        connections.MapDelete("/{id:int}", DeleteConnectionAsync);
        connections.MapGet("/{id:int}/routes", ListConnectionRoutesAsync);
        connections.MapPost("/{id:int}/routes", CreateConnectionRouteAsync);

        var routes = app.MapGroup("/api/proxy-routes").RequireAuthorization();
        routes.MapGet("", ListRoutesAsync);
        routes.MapGet("/{id:int}", GetRouteAsync);
        routes.MapPost("", CreateRouteAsync);
        routes.MapPut("/{id:int}", UpdateRouteAsync);
        routes.MapDelete("/{id:int}", DeleteRouteAsync);
    }

    private static async Task<IResult> ListConnectionsAsync(
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var sourceId = PageRequest.IntQuery(http, "sourceServerId");
        var targetId = PageRequest.IntQuery(http, "targetServerId");

        var query = db.ProxyConnections.AsNoTracking();
        if (sourceId is not null)
            query = query.Where(x => x.SourceServerId == sourceId);
        if (targetId is not null)
            query = query.Where(x => x.TargetServerId == targetId);
        if (page.Q is not null)
            query = query.Where(x => x.SourceServer!.Name.Contains(page.Q) ||
                                     x.TargetServer!.Name.Contains(page.Q));

        query = page.Sort switch
        {
            "source" => query.OrderBy(x => x.SourceServer!.Name).ThenBy(x => x.Id),
            "-source" => query.OrderByDescending(x => x.SourceServer!.Name).ThenBy(x => x.Id),
            _ => query.OrderBy(x => x.Id)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(ProxyConnectionProjection)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ProxyConnectionDto>(
            items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetConnectionAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var dto = await db.ProxyConnections.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ProxyConnectionProjection)
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Connessione proxy", id) : Results.Ok(dto);
    }

    private static async Task<IResult> CreateConnectionAsync(
        ProxyConnectionInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var direction = Guard.Enum<ProxyDirection>(input.Direction, "Direction");
        await EnsureServersExistAsync(db, input.SourceServerId, input.TargetServerId, ct);
        if (input.SourceServerId == input.TargetServerId)
            throw Guard.Unprocessable("Un server non può essere connesso a se stesso.");

        await RequireAdminAsync(user, access, db,
            input.SourceServerId, input.TargetServerId, null, ct);

        var entity = new ProxyConnection
        {
            SourceServerId = input.SourceServerId,
            TargetServerId = input.TargetServerId,
            Direction = direction
        };
        db.ProxyConnections.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await LoadConnectionAsync(db, entity.Id, ct));
    }

    private static async Task<IResult> UpdateConnectionAsync(
        int id,
        ProxyConnectionInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.ProxyConnections.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Connessione proxy", id);

        var direction = Guard.Enum<ProxyDirection>(input.Direction, "Direction");
        await EnsureServersExistAsync(db, input.SourceServerId, input.TargetServerId, ct);
        if (input.SourceServerId == input.TargetServerId)
            throw Guard.Unprocessable("Un server non può essere connesso a se stesso.");

        var envIds = await EnvironmentIdsAsync(db,
            [entity.SourceServerId, entity.TargetServerId, input.SourceServerId, input.TargetServerId], ct);
        await access.RequireAdminAsync(user, envIds, ct);

        entity.SourceServerId = input.SourceServerId;
        entity.TargetServerId = input.TargetServerId;
        entity.Direction = direction;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteConnectionAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.ProxyConnections
                         .Include(x => x.Routes)
                         .SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Connessione proxy", id);

        await RequireAdminAsync(user, access, db,
            entity.SourceServerId, entity.TargetServerId, null, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var forwarding = await db.ProxyRoutes
            .Where(x => x.TargetProxyConnectionId == id && x.ProxyConnectionId != id)
            .ToListAsync(ct);
        foreach (var route in forwarding)
            route.TargetProxyConnectionId = null;

        var routeIds = entity.Routes.Select(x => x.Id).ToList();
        var deploys = await db.ServerApplications
            .Where(x => x.ProxyRouteId != null && routeIds.Contains(x.ProxyRouteId.Value))
            .ToListAsync(ct);
        foreach (var deploy in deploys)
            deploy.ProxyRouteId = null;

        await db.SaveChangesAsync(ct);

        db.ProxyRoutes.RemoveRange(entity.Routes);
        db.ProxyConnections.Remove(entity);
        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListConnectionRoutesAsync(
        int id,
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        if (!await db.ProxyConnections.AnyAsync(x => x.Id == id, ct))
            throw Guard.NotFound("Connessione proxy", id);

        var page = PageRequest.From(http);
        var query = db.ProxyRoutes.AsNoTracking().Where(x => x.ProxyConnectionId == id);
        if (page.Q is not null)
            query = query.Where(x => x.Route.Contains(page.Q));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(ProxyRouteProjection)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ProxyRouteDto>(items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> CreateConnectionRouteAsync(
        int id,
        ProxyRouteInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var connection = await db.ProxyConnections.AsNoTracking()
                             .SingleOrDefaultAsync(x => x.Id == id, ct)
                         ?? throw Guard.NotFound("Connessione proxy", id);

        return await CreateRouteCoreAsync(
            connection, input, user, access, db, ct);
    }

    private static async Task<IResult> ListRoutesAsync(
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var connectionId = PageRequest.IntQuery(http, "proxyConnectionId");

        var query = db.ProxyRoutes.AsNoTracking();
        if (connectionId is not null)
            query = query.Where(x => x.ProxyConnectionId == connectionId);
        if (page.Q is not null)
            query = query.Where(x => x.Route.Contains(page.Q));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(ProxyRouteProjection)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ProxyRouteDto>(items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetRouteAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var dto = await db.ProxyRoutes.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ProxyRouteProjection)
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Route proxy", id) : Results.Ok(dto);
    }

    private static async Task<IResult> CreateRouteAsync(
        ProxyRouteInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        if (input.ProxyConnectionId is null)
            throw Guard.Unprocessable("ProxyConnectionId è obbligatorio.");

        var connection = await db.ProxyConnections.AsNoTracking()
                             .SingleOrDefaultAsync(x => x.Id == input.ProxyConnectionId.Value, ct)
                         ?? throw Guard.NotFound("Connessione proxy", input.ProxyConnectionId.Value);

        return await CreateRouteCoreAsync(connection, input, user, access, db, ct);
    }

    private static async Task<IResult> CreateRouteCoreAsync(
        ProxyConnection connection,
        ProxyRouteInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        await RequireRouteAdminAsync(user, access, db, connection, input, ct);
        await ValidateRouteTargetsAsync(db, connection.Id, input, ct);

        var entity = new ProxyRoute
        {
            ProxyConnectionId = connection.Id,
            Route = Guard.Text(input.Route, "Route", 200),
            MappedTo = Guard.OptionalText(input.MappedTo, "MappedTo", 50),
            TargetServerApplicationId = input.TargetServerApplicationId,
            TargetProxyConnectionId = input.TargetProxyConnectionId
        };

        var duplicate = await db.ProxyRoutes.AnyAsync(
            x => x.ProxyConnectionId == connection.Id && x.Route == entity.Route, ct);
        if (duplicate)
            throw Guard.Conflict($"La route {entity.Route} esiste già su questa connessione.");

        db.ProxyRoutes.Add(entity);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadRouteAsync(db, entity.Id, ct));
    }

    private static async Task<IResult> UpdateRouteAsync(
        int id,
        ProxyRouteInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.ProxyRoutes.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Route proxy", id);

        var connectionId = input.ProxyConnectionId ?? entity.ProxyConnectionId;
        var connection = await db.ProxyConnections.AsNoTracking()
                             .SingleOrDefaultAsync(x => x.Id == connectionId, ct)
                         ?? throw Guard.NotFound("Connessione proxy", connectionId);

        await RequireRouteAdminAsync(user, access, db, connection, input, ct);
        if (connectionId != entity.ProxyConnectionId)
        {
            var previous = await db.ProxyConnections.AsNoTracking()
                               .SingleAsync(x => x.Id == entity.ProxyConnectionId, ct);
            await RequireRouteAdminAsync(user, access, db, previous,
                new ProxyRouteInput(null, null, null,
                    entity.TargetServerApplicationId, entity.TargetProxyConnectionId), ct);
        }

        await ValidateRouteTargetsAsync(db, connectionId, input, ct);

        var route = Guard.Text(input.Route, "Route", 200);
        var duplicate = await db.ProxyRoutes.AnyAsync(
            x => x.Id != id && x.ProxyConnectionId == connectionId && x.Route == route, ct);
        if (duplicate)
            throw Guard.Conflict($"La route {route} esiste già su questa connessione.");

        entity.ProxyConnectionId = connectionId;
        entity.Route = route;
        entity.MappedTo = Guard.OptionalText(input.MappedTo, "MappedTo", 50);
        entity.TargetServerApplicationId = input.TargetServerApplicationId;
        entity.TargetProxyConnectionId = input.TargetProxyConnectionId;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteRouteAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.ProxyRoutes.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Route proxy", id);

        var connection = await db.ProxyConnections.AsNoTracking()
                             .SingleAsync(x => x.Id == entity.ProxyConnectionId, ct);
        await RequireRouteAdminAsync(user, access, db, connection,
            new ProxyRouteInput(null, null, null,
                entity.TargetServerApplicationId, entity.TargetProxyConnectionId), ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var deploys = await db.ServerApplications
            .Where(x => x.ProxyRouteId == id)
            .ToListAsync(ct);
        foreach (var deploy in deploys)
            deploy.ProxyRouteId = null;
        await db.SaveChangesAsync(ct);

        db.ProxyRoutes.Remove(entity);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task RequireRouteAdminAsync(
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        ProxyConnection connection,
        ProxyRouteInput input,
        CancellationToken ct)
    {
        var sourceIds = new List<int> { connection.SourceServerId, connection.TargetServerId };

        int? targetServerId = null;
        if (input.TargetServerApplicationId is not null)
        {
            targetServerId = await db.ServerApplications
                .Where(x => x.Id == input.TargetServerApplicationId)
                .Select(x => (int?)x.ServerId)
                .SingleOrDefaultAsync(ct)
                ?? throw Guard.NotFound("Applicazione deployata", input.TargetServerApplicationId.Value);
        }

        if (input.TargetProxyConnectionId is not null)
        {
            var target = await db.ProxyConnections.AsNoTracking()
                             .SingleOrDefaultAsync(x => x.Id == input.TargetProxyConnectionId, ct)
                         ?? throw Guard.NotFound("Connessione proxy", input.TargetProxyConnectionId.Value);
            sourceIds.Add(target.SourceServerId);
            sourceIds.Add(target.TargetServerId);
        }

        if (targetServerId is not null)
            sourceIds.Add(targetServerId.Value);

        await RequireAdminAsync(user, access, db, sourceIds[0], sourceIds[1],
            sourceIds.Count > 2 ? sourceIds.Skip(2).ToArray() : null, ct);
    }

    private static async Task RequireAdminAsync(
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        int sourceServerId,
        int targetServerId,
        int[]? extraServerIds,
        CancellationToken ct)
    {
        var serverIds = new List<int> { sourceServerId, targetServerId };
        if (extraServerIds is not null)
            serverIds.AddRange(extraServerIds);

        var envIds = await EnvironmentIdsAsync(db, serverIds, ct);
        if (envIds.Count == 0)
            throw Guard.NotFound("Server", sourceServerId);
        await access.RequireAdminAsync(user, envIds, ct);
    }

    private static async Task<List<int>> EnvironmentIdsAsync(
        InfraOrgItDbContext db,
        IEnumerable<int> serverIds,
        CancellationToken ct) =>
        await db.Servers.AsNoTracking()
            .Where(x => serverIds.Contains(x.Id))
            .Select(x => x.ProviderEnvironmentId)
            .Distinct()
            .ToListAsync(ct);

    private static async Task EnsureServersExistAsync(
        InfraOrgItDbContext db, int sourceId, int targetId, CancellationToken ct)
    {
        var ids = new[] { sourceId, targetId };
        var count = await db.Servers.CountAsync(x => ids.Contains(x.Id), ct);
        if (count != ids.Distinct().Count())
        {
            if (!await db.Servers.AnyAsync(x => x.Id == sourceId, ct))
                throw Guard.NotFound("Server", sourceId);
            throw Guard.NotFound("Server", targetId);
        }
    }

    private static async Task ValidateRouteTargetsAsync(
        InfraOrgItDbContext db, int connectionId, ProxyRouteInput input, CancellationToken ct)
    {
        if (input.TargetServerApplicationId is not null && input.TargetProxyConnectionId is not null)
            throw Guard.Unprocessable("La route può avere un solo target.");

        if (input.TargetServerApplicationId is not null &&
            !await db.ServerApplications.AnyAsync(x => x.Id == input.TargetServerApplicationId, ct))
            throw Guard.NotFound("Applicazione deployata", input.TargetServerApplicationId.Value);

        if (input.TargetProxyConnectionId is not null)
        {
            if (input.TargetProxyConnectionId == connectionId)
                throw Guard.Unprocessable("Una route non può puntare alla propria connessione.");
            if (!await db.ProxyConnections.AnyAsync(x => x.Id == input.TargetProxyConnectionId, ct))
                throw Guard.NotFound("Connessione proxy", input.TargetProxyConnectionId.Value);
        }
    }

    private static async Task<ProxyConnectionDto> LoadConnectionAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.ProxyConnections.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ProxyConnectionProjection)
            .SingleAsync(ct);

    private static async Task<ProxyRouteDto> LoadRouteAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.ProxyRoutes.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ProxyRouteProjection)
            .SingleAsync(ct);

    private static readonly Expression<Func<ProxyConnection, ProxyConnectionDto>> ProxyConnectionProjection =
        x => new ProxyConnectionDto(
            x.Id,
            x.SourceServerId,
            x.SourceServer!.Name,
            x.TargetServerId,
            x.TargetServer!.Name,
            x.Direction,
            x.CreatedAt,
            x.Routes.Count,
            x.UpdatedAt,
            x.Version);

    private static readonly Expression<Func<ProxyRoute, ProxyRouteDto>> ProxyRouteProjection =
        x => new ProxyRouteDto(
            x.Id,
            x.ProxyConnectionId,
            x.Route,
            x.MappedTo,
            x.TargetServerApplicationId,
            x.TargetProxyConnectionId,
            x.TargetServerApplication != null
                ? x.TargetServerApplication.Server!.Name + " · " +
                  x.TargetServerApplication.ApplicationVersion!.Application!.Name + " " +
                  x.TargetServerApplication.ApplicationVersion.VersionNumber
                : x.TargetProxyConnection != null
                    ? x.TargetProxyConnection.SourceServer!.Name + " → " +
                      x.TargetProxyConnection.TargetServer!.Name
                    : null,
            x.CreatedAt,
            x.UpdatedAt,
            x.Version);
}

public enum ProxyDirection
{
    Outgoing,
    Incoming
}

public sealed record ProxyConnectionInput(int SourceServerId, int TargetServerId, string? Direction);

public sealed record ProxyConnectionDto(
    int Id,
    int SourceServerId,
    string? SourceServerName,
    int TargetServerId,
    string? TargetServerName,
    string Direction,
    DateTime CreatedAt,
    int RouteCount,
    DateTime UpdatedAt,
    int Version);

public sealed record ProxyRouteInput(
    int? ProxyConnectionId,
    string? Route,
    string? MappedTo,
    int? TargetServerApplicationId,
    int? TargetProxyConnectionId);

public sealed record ProxyRouteDto(
    int Id,
    int ProxyConnectionId,
    string Route,
    string? MappedTo,
    int? TargetServerApplicationId,
    int? TargetProxyConnectionId,
    string? TargetLabel,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

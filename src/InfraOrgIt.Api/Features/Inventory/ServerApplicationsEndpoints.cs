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

        var details = ValidateDeployment(
            input.Runtime,
            input.ContainerImage,
            input.ContainerName,
            input.ComposeDirectory,
            input.HostPort,
            input.ExposedPort,
            input.HaRole);

        var entity = new ServerApplication
        {
            ServerId = input.ServerId,
            ApplicationVersionId = input.ApplicationVersionId,
            ExposedPort = details.ExposedPort,
            ProxyRouteId = input.ProxyRouteId
        };
        ApplyDetails(entity, details);
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

        var details = ValidateDeployment(
            input.Runtime,
            input.ContainerImage,
            input.ContainerName,
            input.ComposeDirectory,
            input.HostPort,
            input.ExposedPort,
            input.HaRole);

        entity.ServerId = input.ServerId;
        entity.ApplicationVersionId = input.ApplicationVersionId;
        entity.ExposedPort = details.ExposedPort;
        entity.ProxyRouteId = input.ProxyRouteId;
        ApplyDetails(entity, details);
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

    internal static DeploymentDetails ValidateDeployment(
        string? runtime,
        string? containerImage,
        string? containerName,
        string? composeDirectory,
        int? hostPort,
        int exposedPort,
        string? haRole)
    {
        var parsedRuntime = Guard.OptionalEnum<DeploymentRuntime>(runtime, "Runtime");
        var image = Guard.OptionalText(containerImage, "ContainerImage", 300);
        var name = Guard.OptionalText(containerName, "ContainerName", 200);
        var directory = Guard.OptionalText(composeDirectory, "ComposeDirectory", 300);

        if (parsedRuntime != nameof(DeploymentRuntime.Container) &&
            (image is not null || name is not null || directory is not null))
            throw Guard.Unprocessable(
                "ContainerImage, ContainerName e ComposeDirectory richiedono Runtime = Container.");

        var port = Guard.Port(exposedPort, "ExposedPort");
        var host = hostPort is null ? (int?)null : Guard.Port(hostPort.Value, "HostPort");
        if (host == port)
            host = null;

        return new DeploymentDetails(
            port,
            parsedRuntime,
            image,
            name,
            directory,
            host,
            Guard.OptionalEnum<DeploymentHaRole>(haRole, "HaRole"));
    }

    internal static void ApplyDetails(ServerApplication entity, DeploymentDetails details)
    {
        entity.Runtime = details.Runtime;
        entity.ContainerImage = details.ContainerImage;
        entity.ContainerName = details.ContainerName;
        entity.ComposeDirectory = details.ComposeDirectory;
        entity.HostPort = details.HostPort;
        entity.HaRole = details.HaRole;
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
            x.Version,
            x.Runtime,
            x.ContainerImage,
            x.ContainerName,
            x.ComposeDirectory,
            x.HostPort,
            x.HaRole);
}

internal sealed record DeploymentDetails(
    int ExposedPort,
    string? Runtime,
    string? ContainerImage,
    string? ContainerName,
    string? ComposeDirectory,
    int? HostPort,
    string? HaRole);

public enum DeploymentRuntime
{
    Container,
    Native
}

public enum DeploymentHaRole
{
    Primary,
    Secondary
}

public sealed record ServerApplicationInput(
    int ServerId,
    int ApplicationVersionId,
    int ExposedPort,
    int? ProxyRouteId = null,
    string? Runtime = null,
    string? ContainerImage = null,
    string? ContainerName = null,
    string? ComposeDirectory = null,
    int? HostPort = null,
    string? HaRole = null);

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
    int Version,
    string? Runtime,
    string? ContainerImage,
    string? ContainerName,
    string? ComposeDirectory,
    int? HostPort,
    string? HaRole);

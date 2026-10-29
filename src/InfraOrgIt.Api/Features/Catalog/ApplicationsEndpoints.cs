using System.Linq.Expressions;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Catalog;

public static class ApplicationsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/applications").RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
        group.MapGet("/{id:int}/versions", ListVersionsAsync);
        group.MapPost("/{id:int}/versions", CreateVersionAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var query = db.Applications.AsNoTracking();

        if (page.Q is not null)
            query = query.Where(x => x.Name.Contains(page.Q) ||
                                     x.Description.Contains(page.Q) ||
                                     x.BasePath.Contains(page.Q));

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

        return Results.Ok(new PagedResult<ApplicationDto>(items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(int id, InfraOrgItDbContext db, CancellationToken ct)
    {
        var dto = await db.Applications.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Applicazione", id) : Results.Ok(dto);
    }

    private static async Task<IResult> CreateAsync(
        ApplicationInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);

        var entity = new Application
        {
            Name = Guard.Text(input.Name, "Name", 200),
            Description = Guard.Text(input.Description, "Description", 200, required: false),
            BasePath = Guard.Text(input.BasePath, "BasePath", 50),
            ListeningOnPort = Guard.Port(input.ListeningOnPort, "ListeningOnPort"),
            Type = ApplicationType(input.Type)
        };

        await EnsureUniqueAsync(db, entity.Name, null, ct);
        db.Applications.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await LoadAsync(db, entity.Id, ct));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ApplicationInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.Applications.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Applicazione", id);

        var name = Guard.Text(input.Name, "Name", 200);
        await EnsureUniqueAsync(db, name, id, ct);

        entity.Name = name;
        entity.Description = Guard.Text(input.Description, "Description", 200, required: false);
        entity.BasePath = Guard.Text(input.BasePath, "BasePath", 50);
        entity.ListeningOnPort = Guard.Port(input.ListeningOnPort, "ListeningOnPort");
        entity.Type = ApplicationType(input.Type);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.Applications.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Applicazione", id);

        var versions = await db.ApplicationVersions.CountAsync(x => x.ApplicationId == id, ct);
        if (versions > 0)
            throw Guard.Conflict($"Applicazione con {versions} versioni: rimuoverle prima.");

        db.Applications.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListVersionsAsync(
        int id,
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        if (!await db.Applications.AnyAsync(x => x.Id == id, ct))
            throw Guard.NotFound("Applicazione", id);

        var page = PageRequest.From(http);
        var query = db.ApplicationVersions.AsNoTracking().Where(x => x.ApplicationId == id);
        if (page.Q is not null)
            query = query.Where(x => x.VersionNumber.Contains(page.Q));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.VersionNumber)
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(ApplicationVersionsEndpoints.ToDto)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ApplicationVersionDto>(
            items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> CreateVersionAsync(
        int id,
        ApplicationVersionInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        if (!await db.Applications.AnyAsync(x => x.Id == id, ct))
            throw Guard.NotFound("Applicazione", id);

        var versionNumber = Guard.Text(input.VersionNumber, "VersionNumber", 25);
        await ApplicationVersionsEndpoints.EnsureUniqueAsync(db, id, versionNumber, null, ct);

        var entity = new ApplicationVersion
        {
            ApplicationId = id,
            VersionNumber = versionNumber
        };
        db.ApplicationVersions.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await ApplicationVersionsEndpoints.LoadAsync(db, entity.Id, ct));
    }

    private static readonly string[] ApplicationTypes =
    [
        "Backend", "Frontend", "Sftp", "RabbitMq", "Nginx", "Elastic", "Kibana", "Kafka"
    ];

    private static string ApplicationType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Backend";

        var match = ApplicationTypes.FirstOrDefault(
            type => string.Equals(type, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return match
               ?? throw Guard.Unprocessable(
                   $"Il campo Type ammette: {string.Join(", ", ApplicationTypes)}.");
    }

    internal static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, string name, int? id, CancellationToken ct)
    {
        var exists = await db.Applications.AnyAsync(
            x => x.Name == name && (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict($"Esiste già un'applicazione chiamata {name}.");
    }

    internal static async Task<ApplicationDto> LoadAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.Applications.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleAsync(ct);

    internal static readonly Expression<Func<Application, ApplicationDto>> ToDto = x => new ApplicationDto(
        x.Id,
        x.Name,
        x.Description,
        x.BasePath,
        x.ListeningOnPort,
        x.Type,
        x.Versions.Count,
        x.CreatedAt,
        x.UpdatedAt,
        x.Version);
}

public sealed record ApplicationInput(
    string? Name,
    string? Description,
    string? BasePath,
    int ListeningOnPort,
    string? Type = null);

public sealed record ApplicationDto(
    int Id,
    string Name,
    string Description,
    string BasePath,
    int ListeningOnPort,
    string Type,
    int VersionCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

public sealed record ApplicationSummaryDto(
    int Id,
    string Name,
    string Description,
    string BasePath,
    int ListeningOnPort);

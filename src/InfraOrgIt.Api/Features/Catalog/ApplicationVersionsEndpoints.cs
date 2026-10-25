using System.Linq.Expressions;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Catalog;

public static class ApplicationVersionsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/application-versions").RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
        group.MapGet("/{id:int}/apis", ListApisAsync);
        group.MapPost("/{id:int}/apis", CreateApiAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var applicationId = PageRequest.IntQuery(http, "applicationId");

        var query = db.ApplicationVersions.AsNoTracking();
        if (applicationId is not null)
            query = query.Where(x => x.ApplicationId == applicationId);
        if (page.Q is not null)
            query = query.Where(x => x.VersionNumber.Contains(page.Q) ||
                                     x.Application!.Name.Contains(page.Q));

        query = page.Sort switch
        {
            "version" => query.OrderBy(x => x.VersionNumber),
            "-version" => query.OrderByDescending(x => x.VersionNumber),
            _ => query.OrderBy(x => x.Id)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(ToDto)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ApplicationVersionDto>(items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(int id, InfraOrgItDbContext db, CancellationToken ct)
    {
        var dto = await db.ApplicationVersions.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Versione applicazione", id) : Results.Ok(dto);
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ApplicationVersionInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.ApplicationVersions.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Versione applicazione", id);

        if (!await db.Applications.AnyAsync(x => x.Id == input.ApplicationId, ct))
            throw Guard.NotFound("Applicazione", input.ApplicationId);

        var versionNumber = Guard.Text(input.VersionNumber, "VersionNumber", 25);
        await EnsureUniqueAsync(db, input.ApplicationId, versionNumber, id, ct);

        entity.ApplicationId = input.ApplicationId;
        entity.VersionNumber = versionNumber;
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
        var entity = await db.ApplicationVersions.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Versione applicazione", id);

        var deployed = await db.ServerApplications.CountAsync(x => x.ApplicationVersionId == id, ct);
        if (deployed > 0)
            throw Guard.Conflict($"Versione deployata su {deployed} server: rimuovere i deploy prima.");

        var apiIds = await db.ApplicationVersionApis
            .Where(x => x.ApplicationVersionId == id)
            .Select(x => x.Id)
            .ToListAsync(ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (apiIds.Count > 0)
        {
            await db.ApplicationApiQueryParams
                .Where(x => apiIds.Contains(x.ApplicationApiId))
                .ExecuteDeleteAsync(ct);
            await db.ApplicationVersionApis
                .Where(x => apiIds.Contains(x.Id))
                .ExecuteDeleteAsync(ct);
        }

        db.ApplicationVersions.Remove(entity);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListApisAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        if (!await db.ApplicationVersions.AnyAsync(x => x.Id == id, ct))
            throw Guard.NotFound("Versione applicazione", id);

        var items = await db.ApplicationVersionApis.AsNoTracking()
            .Where(x => x.ApplicationVersionId == id)
            .OrderBy(x => x.Id)
            .Select(ApplicationVersionApisEndpoints.ToDto)
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateApiAsync(
        int id,
        ApplicationVersionApiInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        if (!await db.ApplicationVersions.AnyAsync(x => x.Id == id, ct))
            throw Guard.NotFound("Versione applicazione", id);

        var entity = new ApplicationVersionApi
        {
            ApplicationVersionId = id,
            Url = Guard.Text(input.Url, "Url", 200),
            RequestTypeEnum = Guard.Enum<RequestType>(input.RequestType, "RequestType"),
            Description = Guard.Text(input.Description, "Description", 200, required: false),
            Body = Guard.OptionalText(input.Body, "Body", 100_000)
        };
        db.ApplicationVersionApis.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await ApplicationVersionApisEndpoints.LoadAsync(db, entity.Id, ct));
    }

    internal static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db,
        int applicationId,
        string versionNumber,
        int? id,
        CancellationToken ct)
    {
        var exists = await db.ApplicationVersions.AnyAsync(
            x => x.ApplicationId == applicationId &&
                 x.VersionNumber == versionNumber &&
                 (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict($"La versione {versionNumber} esiste già per questa applicazione.");
    }

    internal static async Task<ApplicationVersionDto> LoadAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.ApplicationVersions.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleAsync(ct);

    internal static readonly Expression<Func<ApplicationVersion, ApplicationVersionDto>> ToDto =
        x => new ApplicationVersionDto(
            x.Id,
            x.ApplicationId,
            x.Application!.Name,
            x.VersionNumber,
            x.Apis.Count,
            x.CreatedAt,
            x.UpdatedAt,
            x.Version);
}

public enum RequestType
{
    GET,
    POST,
    PUT,
    PATCH,
    DELETE
}

public sealed record ApplicationVersionInput(int ApplicationId, string? VersionNumber);

public sealed record ApplicationVersionDto(
    int Id,
    int ApplicationId,
    string ApplicationName,
    string VersionNumber,
    int ApiCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

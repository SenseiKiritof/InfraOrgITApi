using System.Linq.Expressions;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Catalog;

public static class ApplicationVersionApisEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/application-version-apis").RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
        group.MapGet("/{id:int}/query-params", ListQueryParamsAsync);
        group.MapPost("/{id:int}/query-params", CreateQueryParamAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var applicationVersionId = PageRequest.IntQuery(http, "applicationVersionId");

        var query = db.ApplicationVersionApis.AsNoTracking();
        if (applicationVersionId is not null)
            query = query.Where(x => x.ApplicationVersionId == applicationVersionId);
        if (page.Q is not null)
            query = query.Where(x => x.Url.Contains(page.Q) || x.Description.Contains(page.Q));

        query = page.Sort switch
        {
            "url" => query.OrderBy(x => x.Url),
            "-url" => query.OrderByDescending(x => x.Url),
            _ => query.OrderBy(x => x.Id)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(ToDto)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ApplicationVersionApiDto>(
            items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(int id, InfraOrgItDbContext db, CancellationToken ct)
    {
        var dto = await db.ApplicationVersionApis.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("API applicazione", id) : Results.Ok(dto);
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ApplicationVersionApiInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.ApplicationVersionApis.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("API applicazione", id);

        if (!await db.ApplicationVersions.AnyAsync(x => x.Id == input.ApplicationVersionId, ct))
            throw Guard.NotFound("Versione applicazione", input.ApplicationVersionId);

        entity.ApplicationVersionId = input.ApplicationVersionId;
        entity.Url = Guard.Text(input.Url, "Url", 200);
        entity.RequestTypeEnum = Guard.Enum<RequestType>(input.RequestType, "RequestType");
        entity.Description = Guard.Text(input.Description, "Description", 200, required: false);
        entity.Body = Guard.OptionalText(input.Body, "Body", 100_000);
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
        var entity = await db.ApplicationVersionApis.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("API applicazione", id);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.ApplicationApiQueryParams
            .Where(x => x.ApplicationApiId == id)
            .ExecuteDeleteAsync(ct);

        db.ApplicationVersionApis.Remove(entity);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListQueryParamsAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        if (!await db.ApplicationVersionApis.AnyAsync(x => x.Id == id, ct))
            throw Guard.NotFound("API applicazione", id);

        var items = await db.ApplicationApiQueryParams.AsNoTracking()
            .Where(x => x.ApplicationApiId == id)
            .OrderBy(x => x.Id)
            .Select(ApplicationApiQueryParamsEndpoints.ToDto)
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateQueryParamAsync(
        int id,
        ApplicationApiQueryParamInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        if (!await db.ApplicationVersionApis.AnyAsync(x => x.Id == id, ct))
            throw Guard.NotFound("API applicazione", id);

        var entity = new ApplicationApiQueryParam
        {
            ApplicationApiId = id,
            Name = Guard.Text(input.Name, "Name", 200),
            ParamType = Guard.Enum<QueryParamType>(input.ParamType, "ParamType")
        };
        db.ApplicationApiQueryParams.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await ApplicationApiQueryParamsEndpoints.LoadAsync(db, entity.Id, ct));
    }

    internal static async Task<ApplicationVersionApiDto> LoadAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.ApplicationVersionApis.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleAsync(ct);

    internal static readonly Expression<Func<ApplicationVersionApi, ApplicationVersionApiDto>> ToDto =
        x => new ApplicationVersionApiDto(
            x.Id,
            x.ApplicationVersionId,
            x.Url,
            x.RequestTypeEnum,
            x.Description,
            x.Body,
            x.QueryParams.Count,
            x.CreatedAt,
            x.UpdatedAt,
            x.Version);
}

public sealed record ApplicationVersionApiInput(
    int ApplicationVersionId,
    string? Url,
    string? RequestType,
    string? Description,
    string? Body);

public sealed record ApplicationVersionApiDto(
    int Id,
    int ApplicationVersionId,
    string Url,
    string RequestType,
    string Description,
    string? Body,
    int QueryParamCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

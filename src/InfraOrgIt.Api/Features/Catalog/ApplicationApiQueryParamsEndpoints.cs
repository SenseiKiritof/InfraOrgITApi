using System.Linq.Expressions;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Catalog;

public static class ApplicationApiQueryParamsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/application-api-query-params").RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var applicationApiId = PageRequest.IntQuery(http, "applicationApiId");

        var query = db.ApplicationApiQueryParams.AsNoTracking();
        if (applicationApiId is not null)
            query = query.Where(x => x.ApplicationApiId == applicationApiId);
        if (page.Q is not null)
            query = query.Where(x => x.Name.Contains(page.Q));

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

        return Results.Ok(new PagedResult<ApplicationApiQueryParamDto>(
            items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(int id, InfraOrgItDbContext db, CancellationToken ct)
    {
        var dto = await db.ApplicationApiQueryParams.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Query param", id) : Results.Ok(dto);
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ApplicationApiQueryParamInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.ApplicationApiQueryParams.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Query param", id);

        if (!await db.ApplicationVersionApis.AnyAsync(x => x.Id == input.ApplicationApiId, ct))
            throw Guard.NotFound("API applicazione", input.ApplicationApiId);

        entity.ApplicationApiId = input.ApplicationApiId;
        entity.Name = Guard.Text(input.Name, "Name", 200);
        entity.ParamType = Guard.Enum<QueryParamType>(input.ParamType, "ParamType");
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
        var entity = await db.ApplicationApiQueryParams.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Query param", id);

        db.ApplicationApiQueryParams.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    internal static async Task<ApplicationApiQueryParamDto> LoadAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.ApplicationApiQueryParams.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleAsync(ct);

    internal static readonly Expression<Func<ApplicationApiQueryParam, ApplicationApiQueryParamDto>> ToDto =
        x => new ApplicationApiQueryParamDto(
            x.Id,
            x.ApplicationApiId,
            x.Name,
            x.ParamType,
            x.CreatedAt,
            x.UpdatedAt,
            x.Version);
}

public enum QueryParamType
{
    Int,
    String,
    Bool,
    DateTime,
    Double,
    Decimal
}

public sealed record ApplicationApiQueryParamInput(int ApplicationApiId, string? Name, string? ParamType);

public sealed record ApplicationApiQueryParamDto(
    int Id,
    int ApplicationApiId,
    string Name,
    string ParamType,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

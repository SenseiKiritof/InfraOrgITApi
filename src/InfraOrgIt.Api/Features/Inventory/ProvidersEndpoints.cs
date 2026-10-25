using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Inventory;

public static class ProvidersEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/providers").RequireAuthorization();

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
        var query = db.Providers.AsNoTracking();

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
            .Select(x => new ProviderDto(
                x.Id, x.Name, x.ProviderEnvironments.Count, x.CreatedAt, x.UpdatedAt, x.Version))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ProviderDto>(items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(int id, InfraOrgItDbContext db, CancellationToken ct)
    {
        var dto = await db.Providers.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ProviderDto(
                x.Id, x.Name, x.ProviderEnvironments.Count, x.CreatedAt, x.UpdatedAt, x.Version))
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Provider", id) : Results.Ok(dto);
    }

    private static async Task<IResult> CreateAsync(
        ProviderInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var name = Guard.Text(input.Name, "Name", 200);
        await EnsureUniqueAsync(db, name, null, ct);

        var entity = new Provider { Name = name };
        db.Providers.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new ProviderDto(
            entity.Id, entity.Name, 0, entity.CreatedAt, entity.UpdatedAt, entity.Version));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ProviderInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.Providers.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Provider", id);

        var name = Guard.Text(input.Name, "Name", 200);
        await EnsureUniqueAsync(db, name, id, ct);

        entity.Name = name;
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
        var entity = await db.Providers.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Provider", id);

        var environments = await db.ProviderEnvironments.CountAsync(x => x.ProviderId == id, ct);
        if (environments > 0)
            throw Guard.Conflict($"Provider usato da {environments} ambienti: rimuoverli prima.");

        db.Providers.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, string name, int? id, CancellationToken ct)
    {
        var exists = await db.Providers.AnyAsync(x => x.Name == name && (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict($"Esiste già un provider chiamato {name}.");
    }
}

public sealed record ProviderInput(string? Name);

public sealed record ProviderDto(
    int Id, string Name, int EnvironmentCount, DateTime CreatedAt, DateTime UpdatedAt, int Version);

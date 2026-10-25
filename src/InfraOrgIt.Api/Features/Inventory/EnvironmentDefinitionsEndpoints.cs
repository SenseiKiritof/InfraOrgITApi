using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Inventory;

public static class EnvironmentDefinitionsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/environments").RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var query = db.EnvironmentDefinitions.AsNoTracking();

        if (page.Q is not null)
            query = query.Where(x => x.Name.Contains(page.Q) || x.Description.Contains(page.Q));

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
            .Select(x => new EnvironmentDto(
                x.Id, x.Name, x.Description, x.ProviderEnvironments.Count,
                x.CreatedAt, x.UpdatedAt, x.Version))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<EnvironmentDto>(items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(int id, InfraOrgItDbContext db, CancellationToken ct)
    {
        var dto = await db.EnvironmentDefinitions.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new EnvironmentDto(
                x.Id, x.Name, x.Description, x.ProviderEnvironments.Count,
                x.CreatedAt, x.UpdatedAt, x.Version))
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Ambiente", id) : Results.Ok(dto);
    }

    private static async Task<IResult> CreateAsync(
        EnvironmentInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var name = Guard.Text(input.Name, "Name", 100);
        var description = Guard.Text(input.Description, "Description", 250, required: false);
        await EnsureUniqueAsync(db, name, null, ct);

        var entity = new EnvironmentDefinition { Name = name, Description = description };
        db.EnvironmentDefinitions.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new EnvironmentDto(
            entity.Id, entity.Name, entity.Description, 0,
            entity.CreatedAt, entity.UpdatedAt, entity.Version));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        EnvironmentInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.EnvironmentDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Ambiente", id);

        var name = Guard.Text(input.Name, "Name", 100);
        var description = Guard.Text(input.Description, "Description", 250, required: false);
        await EnsureUniqueAsync(db, name, id, ct);

        entity.Name = name;
        entity.Description = description;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, string name, int? id, CancellationToken ct)
    {
        var exists = await db.EnvironmentDefinitions
            .AnyAsync(x => x.Name == name && (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict($"Esiste già un ambiente chiamato {name}.");
    }
}

public sealed record EnvironmentInput(string? Name, string? Description);

public sealed record EnvironmentDto(
    int Id, string Name, string Description, int ProviderCount,
    DateTime CreatedAt, DateTime UpdatedAt, int Version);

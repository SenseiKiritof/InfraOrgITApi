using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Admin;

public static class GroupsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/groups").RequireAuthorization();
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
        var query = db.Groups.AsNoTracking();

        if (page.Q is not null)
            query = query.Where(x => x.Name.Contains(page.Q));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.Name)
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(x => new GroupDto(
                x.Id, x.Name, x.Users.Count, x.CreatedAt, x.UpdatedAt, x.Version))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<GroupDto>(items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var dto = await db.Groups.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new GroupDto(
                x.Id, x.Name, x.Users.Count, x.CreatedAt, x.UpdatedAt, x.Version))
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Gruppo", id) : Results.Ok(dto);
    }

    private static async Task<IResult> CreateAsync(
        GroupInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var name = Guard.Text(input.Name, "Name", 200);
        await EnsureUniqueAsync(db, name, null, ct);

        var entity = new Group { Name = name };
        db.Groups.Add(entity);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new GroupDto(
            entity.Id, entity.Name, 0, entity.CreatedAt, entity.UpdatedAt, entity.Version));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        GroupInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.Groups.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Gruppo", id);

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
        var entity = await db.Groups.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Gruppo", id);

        var assignments = await db.ProviderEnvironmentGroupRoles
            .CountAsync(x => x.GroupId == id, ct);
        if (assignments > 0)
            throw Guard.Conflict($"Gruppo usato in {assignments} assegnazioni di accesso.");

        db.Groups.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, string name, int? id, CancellationToken ct)
    {
        var exists = await db.Groups.AnyAsync(
            x => x.Name == name && (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict($"Esiste già un gruppo chiamato {name}.");
    }
}

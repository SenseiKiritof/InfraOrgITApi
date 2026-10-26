using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Admin;

public static class MenusAdminEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/menus").RequireAuthorization();
        group.MapGet("/all", ListAllAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> ListAllAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var menus = await db.Menus.AsNoTracking()
            .Include(x => x.Roles)
            .OrderBy(x => x.ParentId)
            .ThenBy(x => x.Order)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
        return Results.Ok(menus.Select(ToDto).ToList());
    }

    private static async Task<IResult> GetAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var menu = await db.Menus.AsNoTracking()
                       .Include(x => x.Roles)
                       .SingleOrDefaultAsync(x => x.Id == id, ct)
                   ?? throw Guard.NotFound("Menu", id);
        return Results.Ok(ToDto(menu));
    }

    private static async Task<IResult> CreateAsync(
        MenuInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await BuildAsync(input, null, db, ct);
        db.Menus.Add(entity);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(entity));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        MenuInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.Menus.Include(x => x.Roles)
                         .SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Menu", id);

        var validated = await BuildAsync(input, id, db, ct);
        entity.Name = validated.Name;
        entity.Path = validated.Path;
        entity.IsActive = validated.IsActive;
        entity.HideBackButton = validated.HideBackButton;
        entity.IsVisible = validated.IsVisible;
        entity.Order = validated.Order;
        entity.ParentId = validated.ParentId;
        entity.Roles.Clear();
        foreach (var role in validated.Roles)
            entity.Roles.Add(role);

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
        var entity = await db.Menus.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Menu", id);

        if (await db.Menus.AnyAsync(x => x.ParentId == id, ct))
            throw Guard.Conflict("Menu con sottovoci: eliminare prima le voci figlie.");

        db.Menus.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<Menu> BuildAsync(
        MenuInput input,
        int? id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var parentId = input.ParentId;
        if (parentId is not null)
        {
            if (parentId == id)
                throw Guard.Unprocessable("Un menu non può essere padre di se stesso.");

            var current = parentId;
            while (current is not null)
            {
                if (current == id)
                    throw Guard.Unprocessable("Gerarchia menu ciclica.");

                var parent = await db.Menus.AsNoTracking()
                    .Where(x => x.Id == current)
                    .Select(x => new { x.Id, x.ParentId })
                    .SingleOrDefaultAsync(ct)
                    ?? throw Guard.NotFound("Menu padre", current.Value);
                current = parent.ParentId;
            }
        }

        var roleIds = (input.RoleIds ?? []).Distinct().ToList();
        var roles = roleIds.Count == 0
            ? []
            : await db.Roles.Where(x => roleIds.Contains(x.Id)).ToListAsync(ct);
        if (roles.Count != roleIds.Count)
            throw Guard.Unprocessable("Uno o più ruoli non esistono.");

        return new Menu
        {
            Name = Guard.Text(input.Name, "Name", 50),
            Path = Guard.Text(input.Path, "Path", 200),
            IsActive = input.IsActive,
            HideBackButton = input.HideBackButton,
            IsVisible = input.IsVisible,
            Order = input.Order,
            ParentId = parentId,
            Roles = roles
        };
    }

    private static MenuAdminDto ToDto(Menu menu) => new(
        menu.Id,
        menu.Name,
        menu.Path,
        menu.IsActive,
        menu.HideBackButton,
        menu.IsVisible,
        menu.Order,
        menu.ParentId,
        menu.Roles.Select(x => x.Id).OrderBy(x => x).ToList(),
        menu.CreatedAt,
        menu.UpdatedAt,
        menu.Version);
}

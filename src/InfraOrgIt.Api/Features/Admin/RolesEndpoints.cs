using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Admin;

public static class RolesEndpoints
{
    private static readonly HashSet<string> BuiltInRoles =
        new(StringComparer.OrdinalIgnoreCase) { "SystemAdmin", "ServerAdmin", "ServerOperator" };

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/roles").RequireAuthorization();
        group.MapGet("", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        string? lang,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var scope = http.Request.Query["scopeEnum"].ToString();
        var query = db.Roles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(scope))
            query = query.Where(x => x.ScopeEnum == scope);
        if (page.Q is not null)
            query = query.Where(x => x.Name.Contains(page.Q) ||
                                     x.Label.Contains(page.Q) ||
                                     x.ScopeEnum.Contains(page.Q));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.Name)
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(x => new RoleDto(
                x.Id, x.Name, x.Label, x.ScopeEnum,
                x.Users.Count,
                db.MenuRoles.Count(m => m.RolesId == x.Id),
                db.ProviderEnvironmentGroupRoles.Count(a => a.RoleId == x.Id),
                x.CreatedAt, x.UpdatedAt, x.Version))
            .ToListAsync(ct);

        items = await WithTranslationsAsync(db, Normalize(lang), items, ct);
        return Results.Ok(new PagedResult<RoleDto>(items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(
        int id,
        string? lang,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var dto = await db.Roles.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new RoleDto(
                x.Id, x.Name, x.Label, x.ScopeEnum,
                x.Users.Count,
                db.MenuRoles.Count(m => m.RolesId == x.Id),
                db.ProviderEnvironmentGroupRoles.Count(a => a.RoleId == x.Id),
                x.CreatedAt, x.UpdatedAt, x.Version))
            .SingleOrDefaultAsync(ct);
        if (dto is null)
            throw Guard.NotFound("Ruolo", id);

        var localized = await WithTranslationsAsync(db, Normalize(lang), [dto], ct);
        return Results.Ok(localized[0]);
    }

    private static async Task<IResult> CreateAsync(
        RoleInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var name = Guard.Text(input.Name, "Name", 100);
        var scopeEnum = Guard.Enum<RoleScope>(input.ScopeEnum, "ScopeEnum");
        await EnsureUniqueAsync(db, name, null, ct);

        var entity = new Role
        {
            Name = name,
            Label = Guard.Text(input.Label, "Label", 100),
            ScopeEnum = scopeEnum
        };
        db.Roles.Add(entity);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new RoleDto(
            entity.Id, entity.Name, entity.Label, entity.ScopeEnum,
            0, 0, 0, entity.CreatedAt, entity.UpdatedAt, entity.Version));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        RoleInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.Roles.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Ruolo", id);

        var name = Guard.Text(input.Name, "Name", 100);
        var scopeEnum = Guard.Enum<RoleScope>(input.ScopeEnum, "ScopeEnum");
        await EnsureUniqueAsync(db, name, id, ct);

        if (BuiltInRoles.Contains(entity.Name) &&
            (!string.Equals(entity.Name, name, StringComparison.Ordinal) ||
             entity.ScopeEnum != scopeEnum))
            throw Guard.Conflict("Nome e scope dei ruoli di sistema non sono modificabili; cambia solo l'etichetta.");

        if (entity.ScopeEnum != scopeEnum &&
            await db.ProviderEnvironmentGroupRoles.AnyAsync(x => x.RoleId == id, ct))
            throw Guard.Conflict("Ruolo assegnato a un ambiente: rimuovere le assegnazioni prima di cambiarne lo scope.");

        entity.Name = name;
        entity.Label = Guard.Text(input.Label, "Label", 100);
        entity.ScopeEnum = scopeEnum;
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
        var entity = await db.Roles.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Ruolo", id);

        if (BuiltInRoles.Contains(entity.Name))
            throw Guard.Conflict("I ruoli di sistema non si eliminano.");

        var users = await db.RoleUsers.CountAsync(x => x.RolesId == id, ct);
        var menus = await db.MenuRoles.CountAsync(x => x.RolesId == id, ct);
        var assignments = await db.ProviderEnvironmentGroupRoles.CountAsync(x => x.RoleId == id, ct);
        if (users + menus + assignments > 0)
            throw Guard.Conflict(
                $"Ruolo usato da {users} utenti, {menus} menu e {assignments} assegnazioni.");

        db.Roles.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static string Normalize(string? lang) =>
        string.IsNullOrWhiteSpace(lang) ? "it" : lang.Trim().ToLowerInvariant();

    private static async Task<List<RoleDto>> WithTranslationsAsync(
        InfraOrgItDbContext db,
        string language,
        List<RoleDto> items,
        CancellationToken ct)
    {
        if (language == "it" || items.Count == 0)
            return items;

        var ids = items.Select(x => x.Id).ToList();
        var labels = await db.Translations.AsNoTracking()
            .Where(x => x.EntityType == "Role" && x.Field == "Label" &&
                        x.Language == language && ids.Contains(x.EntityId))
            .ToDictionaryAsync(x => x.EntityId, x => x.Value, ct);

        return items
            .Select(x => labels.TryGetValue(x.Id, out var label) ? x with { Label = label } : x)
            .ToList();
    }

    private static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, string name, int? id, CancellationToken ct)
    {
        var exists = await db.Roles.AnyAsync(
            x => x.Name == name && (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict($"Esiste già un ruolo chiamato {name}.");
    }
}

public enum RoleScope
{
    System,
    Environment
}

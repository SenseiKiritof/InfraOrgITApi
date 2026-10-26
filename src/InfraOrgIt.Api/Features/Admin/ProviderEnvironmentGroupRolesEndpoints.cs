using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Admin;

public static class ProviderEnvironmentGroupRolesEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/provider-environment-group-roles").RequireAuthorization();
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
        var environmentId = PageRequest.IntQuery(http, "providerEnvironmentId");
        var groupId = PageRequest.IntQuery(http, "groupId");
        var roleId = PageRequest.IntQuery(http, "roleId");

        var query = db.ProviderEnvironmentGroupRoles.AsNoTracking();
        if (environmentId is not null)
            query = query.Where(x => x.ProviderEnvironmentId == environmentId);
        if (groupId is not null)
            query = query.Where(x => x.GroupId == groupId);
        if (roleId is not null)
            query = query.Where(x => x.RoleId == roleId);

        if (page.Q is not null)
            query = query.Where(x => x.Group!.Name.Contains(page.Q) ||
                                     x.Role!.Name.Contains(page.Q) ||
                                     x.ProviderEnvironment!.Provider!.Name.Contains(page.Q) ||
                                     x.ProviderEnvironment.EnvironmentDefinition!.Name.Contains(page.Q));

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(x => new
            {
                x.Id,
                x.ProviderEnvironmentId,
                ProviderName = x.ProviderEnvironment!.Provider!.Name,
                EnvironmentName = x.ProviderEnvironment.EnvironmentDefinition!.Name,
                x.GroupId,
                GroupName = x.Group!.Name,
                x.RoleId,
                RoleName = x.Role!.Name,
                x.CreatedAt,
                x.UpdatedAt,
                x.Version
            })
            .ToListAsync(ct);

        var items = rows.Select(x => new AdminGroupRoleDto(
            x.Id,
            x.ProviderEnvironmentId,
            $"{x.ProviderName} / {x.EnvironmentName}",
            x.GroupId,
            x.GroupName,
            x.RoleId,
            x.RoleName,
            x.CreatedAt,
            x.UpdatedAt,
            x.Version)).ToList();

        return Results.Ok(new PagedResult<AdminGroupRoleDto>(
            items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var dto = await db.ProviderEnvironmentGroupRoles.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new AdminGroupRoleDto(
                x.Id,
                x.ProviderEnvironmentId,
                x.ProviderEnvironment!.Provider!.Name + " / " +
                x.ProviderEnvironment.EnvironmentDefinition!.Name,
                x.GroupId,
                x.Group!.Name,
                x.RoleId,
                x.Role!.Name,
                x.CreatedAt,
                x.UpdatedAt,
                x.Version))
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Assegnazione", id) : Results.Ok(dto);
    }

    private static async Task<IResult> CreateAsync(
        AdminGroupRoleInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        await ResolveAsync(db, input, ct);
        await EnsureUniqueAsync(db, input, null, ct);

        var entity = new ProviderEnvironmentGroupRole
        {
            ProviderEnvironmentId = input.ProviderEnvironmentId,
            GroupId = input.GroupId,
            RoleId = input.RoleId
        };
        db.ProviderEnvironmentGroupRoles.Add(entity);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadAsync(db, entity.Id, ct));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        AdminGroupRoleInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.ProviderEnvironmentGroupRoles
                         .SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Assegnazione", id);

        await ResolveAsync(db, input, ct);
        await EnsureUniqueAsync(db, input, id, ct);

        entity.ProviderEnvironmentId = input.ProviderEnvironmentId;
        entity.GroupId = input.GroupId;
        entity.RoleId = input.RoleId;
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
        var entity = await db.ProviderEnvironmentGroupRoles
                         .SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Assegnazione", id);

        db.ProviderEnvironmentGroupRoles.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task ResolveAsync(
        InfraOrgItDbContext db, AdminGroupRoleInput input, CancellationToken ct)
    {
        if (!await db.ProviderEnvironments.AnyAsync(x => x.Id == input.ProviderEnvironmentId, ct))
            throw Guard.NotFound("Ambiente provider", input.ProviderEnvironmentId);
        if (!await db.Groups.AnyAsync(x => x.Id == input.GroupId, ct))
            throw Guard.NotFound("Gruppo", input.GroupId);

        var role = await db.Roles.AsNoTracking()
                       .SingleOrDefaultAsync(x => x.Id == input.RoleId, ct)
                   ?? throw Guard.NotFound("Ruolo", input.RoleId);
        if (role.ScopeEnum != "Environment")
            throw Guard.Unprocessable($"Il ruolo {role.Name} non è valido per un ambiente.");
    }

    private static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, AdminGroupRoleInput input, int? id, CancellationToken ct)
    {
        var exists = await db.ProviderEnvironmentGroupRoles.AnyAsync(
            x => x.ProviderEnvironmentId == input.ProviderEnvironmentId &&
                 x.GroupId == input.GroupId &&
                 x.RoleId == input.RoleId &&
                 (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict("Assegnazione già presente.");
    }

    private static async Task<AdminGroupRoleDto> LoadAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.ProviderEnvironmentGroupRoles.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new AdminGroupRoleDto(
                x.Id,
                x.ProviderEnvironmentId,
                x.ProviderEnvironment!.Provider!.Name + " / " +
                x.ProviderEnvironment.EnvironmentDefinition!.Name,
                x.GroupId,
                x.Group!.Name,
                x.RoleId,
                x.Role!.Name,
                x.CreatedAt,
                x.UpdatedAt,
                x.Version))
            .SingleAsync(ct);
}

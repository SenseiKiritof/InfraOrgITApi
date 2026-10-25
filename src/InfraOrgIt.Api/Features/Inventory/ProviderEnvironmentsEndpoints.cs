using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Inventory;

public static class ProviderEnvironmentsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/provider-environments").RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);

        group.MapGet("/{id:int}/group-roles", ListGroupRolesAsync);
        group.MapPost("/{id:int}/group-roles", AddGroupRoleAsync);
        group.MapDelete("/{id:int}/group-roles/{groupRoleId:int}", RemoveGroupRoleAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var providerId = PageRequest.IntQuery(http, "providerId");
        var environmentId = PageRequest.IntQuery(http, "environmentId");

        var query = db.ProviderEnvironments.AsNoTracking();
        if (providerId is not null)
            query = query.Where(x => x.ProviderId == providerId);
        if (environmentId is not null)
            query = query.Where(x => x.EnvironmentDefinitionId == environmentId);

        if (page.Q is not null)
            query = query.Where(x => x.Provider!.Name.Contains(page.Q) ||
                                     x.EnvironmentDefinition!.Name.Contains(page.Q));

        query = page.Sort switch
        {
            "provider" => query.OrderBy(x => x.Provider!.Name).ThenBy(x => x.Id),
            "-provider" => query.OrderByDescending(x => x.Provider!.Name).ThenBy(x => x.Id),
            "environment" => query.OrderBy(x => x.EnvironmentDefinition!.Name).ThenBy(x => x.Id),
            "-environment" => query.OrderByDescending(x => x.EnvironmentDefinition!.Name).ThenBy(x => x.Id),
            _ => query.OrderBy(x => x.Id)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(x => new ProviderEnvironmentDto(
                x.Id,
                x.ProviderId,
                x.Provider!.Name,
                x.EnvironmentDefinitionId,
                x.EnvironmentDefinition!.Name,
                x.Provider!.Name + " / " + x.EnvironmentDefinition!.Name,
                x.Servers.Count,
                x.CreatedAt,
                x.UpdatedAt,
                x.Version))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ProviderEnvironmentDto>(
            items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(int id, InfraOrgItDbContext db, CancellationToken ct)
    {
        var dto = await db.ProviderEnvironments.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ProviderEnvironmentDto(
                x.Id,
                x.ProviderId,
                x.Provider!.Name,
                x.EnvironmentDefinitionId,
                x.EnvironmentDefinition!.Name,
                x.Provider!.Name + " / " + x.EnvironmentDefinition!.Name,
                x.Servers.Count,
                x.CreatedAt,
                x.UpdatedAt,
                x.Version))
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Ambiente provider", id) : Results.Ok(dto);
    }

    private static async Task<IResult> CreateAsync(
        ProviderEnvironmentInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        await EnsureReferencesAsync(db, input, ct);
        await EnsureUniqueAsync(db, input, null, ct);

        var entity = new ProviderEnvironment
        {
            ProviderId = input.ProviderId,
            EnvironmentDefinitionId = input.EnvironmentId
        };
        db.ProviderEnvironments.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await LoadAsync(db, entity.Id, ct));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ProviderEnvironmentInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.ProviderEnvironments.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Ambiente provider", id);

        await EnsureReferencesAsync(db, input, ct);
        await EnsureUniqueAsync(db, input, id, ct);

        entity.ProviderId = input.ProviderId;
        entity.EnvironmentDefinitionId = input.EnvironmentId;
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
        var entity = await db.ProviderEnvironments.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Ambiente provider", id);

        var servers = await db.Servers.CountAsync(x => x.ProviderEnvironmentId == id, ct);
        if (servers > 0)
            throw Guard.Conflict($"Ambiente usato da {servers} server: rimuoverli prima.");

        db.ProviderEnvironments.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListGroupRolesAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var exists = await db.ProviderEnvironments.AnyAsync(x => x.Id == id, ct);
        if (!exists)
            throw Guard.NotFound("Ambiente provider", id);

        var items = await db.ProviderEnvironmentGroupRoles.AsNoTracking()
            .Where(x => x.ProviderEnvironmentId == id)
            .OrderBy(x => x.Id)
            .Select(x => new ProviderEnvironmentGroupRoleDto(
                x.Id, x.ProviderEnvironmentId, x.GroupId, x.Group!.Name, x.RoleId, x.Role!.Name))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> AddGroupRoleAsync(
        int id,
        ProviderEnvironmentGroupRoleInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var environment = await db.ProviderEnvironments.AnyAsync(x => x.Id == id, ct);
        if (!environment)
            throw Guard.NotFound("Ambiente provider", id);

        var groupName = await db.Groups.Where(x => x.Id == input.GroupId)
            .Select(x => x.Name).SingleOrDefaultAsync(ct);
        if (groupName is null)
            throw Guard.NotFound("Gruppo", input.GroupId);

        var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == input.RoleId, ct)
                   ?? throw Guard.NotFound("Ruolo", input.RoleId);
        if (role.ScopeEnum != "Environment")
            throw Guard.Unprocessable($"Il ruolo {role.Name} non è valido per un ambiente.");

        var duplicate = await db.ProviderEnvironmentGroupRoles.AnyAsync(
            x => x.ProviderEnvironmentId == id && x.GroupId == input.GroupId && x.RoleId == input.RoleId, ct);
        if (duplicate)
            throw Guard.Conflict("Assegnazione già presente.");

        var entity = new ProviderEnvironmentGroupRole
        {
            ProviderEnvironmentId = id,
            GroupId = input.GroupId,
            RoleId = input.RoleId
        };
        db.ProviderEnvironmentGroupRoles.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new ProviderEnvironmentGroupRoleDto(
            entity.Id, id, input.GroupId, groupName, input.RoleId, role.Name));
    }

    private static async Task<IResult> RemoveGroupRoleAsync(
        int id,
        int groupRoleId,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.ProviderEnvironmentGroupRoles
                         .SingleOrDefaultAsync(x => x.Id == groupRoleId && x.ProviderEnvironmentId == id, ct)
                     ?? throw Guard.NotFound("Assegnazione", groupRoleId);

        db.ProviderEnvironmentGroupRoles.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<ProviderEnvironmentDto> LoadAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.ProviderEnvironments.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ProviderEnvironmentDto(
                x.Id,
                x.ProviderId,
                x.Provider!.Name,
                x.EnvironmentDefinitionId,
                x.EnvironmentDefinition!.Name,
                x.Provider!.Name + " / " + x.EnvironmentDefinition!.Name,
                x.Servers.Count,
                x.CreatedAt,
                x.UpdatedAt,
                x.Version))
            .SingleAsync(ct);

    private static async Task EnsureReferencesAsync(
        InfraOrgItDbContext db, ProviderEnvironmentInput input, CancellationToken ct)
    {
        if (!await db.Providers.AnyAsync(x => x.Id == input.ProviderId, ct))
            throw Guard.NotFound("Provider", input.ProviderId);
        if (!await db.EnvironmentDefinitions.AnyAsync(x => x.Id == input.EnvironmentId, ct))
            throw Guard.NotFound("Ambiente", input.EnvironmentId);
    }

    private static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, ProviderEnvironmentInput input, int? id, CancellationToken ct)
    {
        var exists = await db.ProviderEnvironments.AnyAsync(
            x => x.ProviderId == input.ProviderId &&
                 x.EnvironmentDefinitionId == input.EnvironmentId &&
                 (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict("La coppia provider/ambiente esiste già.");
    }
}

public sealed record ProviderEnvironmentInput(int ProviderId, int EnvironmentId);

public sealed record ProviderEnvironmentDto(
    int Id,
    int ProviderId,
    string ProviderName,
    int EnvironmentId,
    string EnvironmentName,
    string Label,
    int ServerCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

public sealed record ProviderEnvironmentGroupRoleInput(int GroupId, int RoleId);

public sealed record ProviderEnvironmentGroupRoleDto(
    int Id, int ProviderEnvironmentId, int GroupId, string GroupName, int RoleId, string RoleName);

using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Admin;

public static class UsersEndpoints
{
    private const string SystemAdmin = "SystemAdmin";

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization();
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
        IQueryable<User> query = db.Users.AsNoTracking().Include(x => x.Groups).Include(x => x.Roles);

        if (page.Q is not null)
            query = query.Where(x => x.Username.Contains(page.Q) ||
                                     x.Name.Contains(page.Q) ||
                                     x.Surname.Contains(page.Q));

        query = page.Sort switch
        {
            "username" => query.OrderBy(x => x.Username),
            "-username" => query.OrderByDescending(x => x.Username),
            _ => query.OrderBy(x => x.Id)
        };

        var total = await query.CountAsync(ct);
        var users = await query.Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<UserDto>(
            users.Select(ToDto).ToList(), total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
                       .Include(x => x.Groups)
                       .Include(x => x.Roles)
                       .SingleOrDefaultAsync(x => x.Id == id, ct)
                   ?? throw Guard.NotFound("Utente", id);
        return Results.Ok(ToDto(user));
    }

    private static async Task<IResult> CreateAsync(
        UserInput input,
        ClaimsPrincipal principal,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(principal);
        var username = Guard.Text(input.Username, "Username", 100);
        await EnsureUniqueAsync(db, username, null, ct);

        var groups = await ResolveGroupsAsync(db, input.GroupIds, ct);
        var roles = await ResolveRolesAsync(db, input.RoleIds, ct);

        var user = new User
        {
            Username = username,
            Name = Guard.Text(input.Name, "Name", 100),
            Surname = Guard.Text(input.Surname, "Surname", 100)
        };
        user.Groups.AddRange(groups);
        user.Roles.AddRange(roles);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return Results.Ok(ToDto(user));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        UserInput input,
        ClaimsPrincipal principal,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(principal);
        var user = await db.Users.Include(x => x.Groups).Include(x => x.Roles)
                       .SingleOrDefaultAsync(x => x.Id == id, ct)
                   ?? throw Guard.NotFound("Utente", id);

        var username = Guard.Text(input.Username, "Username", 100);
        await EnsureUniqueAsync(db, username, id, ct);
        var groups = await ResolveGroupsAsync(db, input.GroupIds, ct);
        var roles = await ResolveRolesAsync(db, input.RoleIds, ct);

        var keepsSystemAdmin = roles.Any(x => x.Name == SystemAdmin);
        if (user.Roles.Any(x => x.Name == SystemAdmin) && !keepsSystemAdmin)
            await EnsureAnotherSystemAdminAsync(db, id, ct);

        user.Username = username;
        user.Name = Guard.Text(input.Name, "Name", 100);
        user.Surname = Guard.Text(input.Surname, "Surname", 100);
        user.Groups.Clear();
        user.Groups.AddRange(groups);
        user.Roles.Clear();
        user.Roles.AddRange(roles);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal principal,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(principal);
        var user = await db.Users.Include(x => x.Roles)
                       .SingleOrDefaultAsync(x => x.Id == id, ct)
                   ?? throw Guard.NotFound("Utente", id);

        if (string.Equals(user.Username, principal.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            throw Guard.Conflict("Non puoi eliminare il tuo utente.");
        if (user.Roles.Any(x => x.Name == SystemAdmin))
            await EnsureAnotherSystemAdminAsync(db, id, ct);

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task EnsureAnotherSystemAdminAsync(
        InfraOrgItDbContext db, int excludedUserId, CancellationToken ct)
    {
        var exists = await db.Users.AnyAsync(
            x => x.Id != excludedUserId && x.Roles.Any(role => role.Name == SystemAdmin), ct);
        if (!exists)
            throw Guard.Conflict("Deve restare almeno un SystemAdmin.");
    }

    private static async Task<List<Group>> ResolveGroupsAsync(
        InfraOrgItDbContext db, List<int>? ids, CancellationToken ct)
    {
        var distinct = (ids ?? []).Distinct().ToList();
        if (distinct.Count == 0)
            return [];
        var groups = await db.Groups.Where(x => distinct.Contains(x.Id)).ToListAsync(ct);
        if (groups.Count != distinct.Count)
            throw Guard.Unprocessable("Uno o più gruppi non esistono.");
        return groups;
    }

    private static async Task<List<Role>> ResolveRolesAsync(
        InfraOrgItDbContext db, List<int>? ids, CancellationToken ct)
    {
        var distinct = (ids ?? []).Distinct().ToList();
        if (distinct.Count == 0)
            return [];
        var roles = await db.Roles.Where(x => distinct.Contains(x.Id)).ToListAsync(ct);
        if (roles.Count != distinct.Count)
            throw Guard.Unprocessable("Uno o più ruoli non esistono.");
        return roles;
    }

    private static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, string username, int? id, CancellationToken ct)
    {
        var exists = await db.Users.AnyAsync(
            x => x.Username == username && (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict($"Username {username} già esistente.");
    }

    private static UserDto ToDto(User user) => new(
        user.Id,
        user.Username,
        user.Name,
        user.Surname,
        user.Groups.OrderBy(x => x.Name).Select(x => new UserRefGroupDto(x.Id, x.Name)).ToList(),
        user.Roles.OrderBy(x => x.Name)
            .Select(x => new UserRefRoleDto(x.Id, x.Name, x.Label, x.ScopeEnum))
            .ToList(),
        user.CreatedAt,
        user.UpdatedAt,
        user.Version);
}

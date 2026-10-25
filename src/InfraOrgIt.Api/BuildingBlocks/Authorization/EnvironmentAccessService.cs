using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.BuildingBlocks.Authorization;

public sealed record EnvironmentAccess(
    IReadOnlySet<int> AdminEnvironmentIds,
    IReadOnlySet<int> OperatorEnvironmentIds);

public sealed class EnvironmentAccessService(InfraOrgItDbContext db)
{
    public const string SystemAdminRole = "SystemAdmin";
    public const string AdminRole = "ServerAdmin";
    public const string OperatorRole = "ServerOperator";

    public async Task<EnvironmentAccess> GetAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var username = user.Identity?.Name ?? "";
        var rows = await (
                from u in db.Users.AsNoTracking()
                join gu in db.GroupUsers.AsNoTracking() on u.Id equals gu.UsersId
                join per in db.ProviderEnvironmentGroupRoles.AsNoTracking() on gu.GroupsId equals per.GroupId
                join role in db.Roles.AsNoTracking() on per.RoleId equals role.Id
                where u.Username == username
                select new { per.ProviderEnvironmentId, role.Name })
            .ToListAsync(ct);

        var admin = rows.Where(x => x.Name == AdminRole)
            .Select(x => x.ProviderEnvironmentId).ToHashSet();
        var operators = rows.Where(x => x.Name is AdminRole or OperatorRole)
            .Select(x => x.ProviderEnvironmentId).ToHashSet();

        return new EnvironmentAccess(admin, operators);
    }

    public async Task RequireAdminAsync(
        ClaimsPrincipal user,
        IReadOnlyCollection<int> providerEnvironmentIds,
        CancellationToken ct)
    {
        if (providerEnvironmentIds.Count == 0)
            throw Guard.Forbidden("Nessun ambiente coinvolto.");

        var access = await GetAsync(user, ct);
        var missing = providerEnvironmentIds.Where(id => !access.AdminEnvironmentIds.Contains(id)).ToArray();
        if (missing.Length > 0)
            throw Guard.Forbidden(
                $"Serve il ruolo {AdminRole} sugli ambienti coinvolti (mancante su: {string.Join(", ", missing)}).");
    }

    public static void RequireSystemAdmin(ClaimsPrincipal user)
    {
        if (!user.IsInRole(SystemAdminRole))
            throw Guard.Forbidden($"Operazione riservata a {SystemAdminRole}.");
    }
}

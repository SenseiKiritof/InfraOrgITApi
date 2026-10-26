using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.Features.Menus;

namespace InfraOrgIt.Api.Features.Menus;

public static class MenuEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/menus", async (
            ClaimsPrincipal user,
            MenuService menus,
            EnvironmentAccessService access,
            CancellationToken ct) =>
        {
            var roles = user.FindAll(ClaimTypes.Role)
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var environmentAccess = await access.GetAsync(user, ct);
            if (environmentAccess.AdminEnvironmentIds.Count > 0)
                roles.Add(EnvironmentAccessService.AdminRole);
            if (environmentAccess.OperatorEnvironmentIds.Count > 0)
                roles.Add(EnvironmentAccessService.OperatorRole);

            return Results.Ok(await menus.GetForRolesAsync(roles.ToArray(), ct));
        }).RequireAuthorization();
    }
}

using System.Security.Claims;
using InfraOrgIt.Api.Features.Menus;

namespace InfraOrgIt.Api.Features.Menus;

public static class MenuEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/menus", async (ClaimsPrincipal user, MenuService menus, CancellationToken ct) =>
        {
            var roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
            return Results.Ok(await menus.GetForRolesAsync(roles, ct));
        }).RequireAuthorization();
    }
}

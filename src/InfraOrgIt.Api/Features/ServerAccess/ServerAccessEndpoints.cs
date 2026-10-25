using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;

namespace InfraOrgIt.Api.Features.ServerAccess;

public static class ServerAccessEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/server-access/me", async (
            ClaimsPrincipal user,
            EnvironmentAccessService access,
            CancellationToken ct) =>
        {
            var environmentAccess = await access.GetAsync(user, ct);
            return Results.Ok(new
            {
                isSystemAdmin = user.IsInRole(EnvironmentAccessService.SystemAdminRole),
                adminEnvironmentIds = environmentAccess.AdminEnvironmentIds.Order(),
                operatorEnvironmentIds = environmentAccess.OperatorEnvironmentIds.Order()
            });
        }).RequireAuthorization();
    }
}

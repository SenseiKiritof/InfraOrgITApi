using System.Security.Claims;

namespace InfraOrgIt.Api.Features.ServerAccess;

public static class ServerAccessEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/server-access/me", (ClaimsPrincipal user) =>
        {
            return Results.Ok(new
            {
                isSystemAdmin = user.IsInRole("SystemAdmin"),
                adminEnvironmentIds = Array.Empty<int>(),
                operatorEnvironmentIds = Array.Empty<int>()
            });
        }).RequireAuthorization();
    }
}

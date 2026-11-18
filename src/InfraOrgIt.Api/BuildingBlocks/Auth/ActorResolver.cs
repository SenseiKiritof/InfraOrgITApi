using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

/// <summary>
/// Session principals carry the username in NameIdentifier, bearer principals
/// the numeric user id. Resolve either to the provisioned user row.
/// </summary>
public static class ActorResolver
{
    public static async Task<User> ResolveAsync(
        ClaimsPrincipal principal,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(identifier, out var id))
        {
            var byId = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (byId is not null) return byId;
        }

        var username = principal.Identity?.Name ?? "";
        return await db.Users.SingleOrDefaultAsync(
                   x => x.Username == username, ct)
               ?? throw Guard.Forbidden("Utente non provisionato.");
    }
}

using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.Features.Auth;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Users;

public sealed class UserProvisioningService(
    InfraOrgItDbContext db,
    IOptions<AuthOptions> authOptions,
    ILogger<UserProvisioningService> logger)
{
    public async Task EnsureUserAsync(OidcIdentity identity, CancellationToken ct)
    {
        var user = await db.Users
            .Include(x => x.Roles)
            .SingleOrDefaultAsync(x => x.Username == identity.Username, ct);

        if (user is null)
        {
            user = new User
            {
                Username = identity.Username,
                Name = Fallback(identity.Name, identity.Username),
                Surname = identity.Surname
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Utente provisionato: {Username}", user.Username);
        }
        else
        {
            var changed = false;
            if (string.IsNullOrWhiteSpace(user.Name) && !string.IsNullOrWhiteSpace(identity.Name))
            {
                user.Name = identity.Name;
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(user.Surname) && !string.IsNullOrWhiteSpace(identity.Surname))
            {
                user.Surname = identity.Surname;
                changed = true;
            }
            if (changed)
                await db.SaveChangesAsync(ct);
        }

        var bootstrapUsername = authOptions.Value.BootstrapAdminUsername;
        if (string.IsNullOrWhiteSpace(bootstrapUsername) ||
            !string.Equals(bootstrapUsername, identity.Username, StringComparison.OrdinalIgnoreCase))
            return;

        var systemAdmin = await db.Roles.SingleOrDefaultAsync(x => x.Name == "SystemAdmin", ct);
        if (systemAdmin is null)
            return;

        var alreadyAssigned = await db.Users.AnyAsync(
            x => x.Roles.Any(role => role.Name == "SystemAdmin"), ct);

        if (alreadyAssigned || user.Roles.Any(role => role.Id == systemAdmin.Id))
            return;

        user.Roles.Add(systemAdmin);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Bootstrap SystemAdmin assegnato a {Username}", user.Username);
    }

    private static string Fallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}

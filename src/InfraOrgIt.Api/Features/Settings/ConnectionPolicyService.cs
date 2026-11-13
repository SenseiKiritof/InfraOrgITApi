using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Settings;

/// <summary>
/// Read side of the installation connection policy. Kept separate from the
/// endpoints so console/files/actions can enforce the same value without
/// duplicating the singleton query.
/// </summary>
public sealed class ConnectionPolicyService(InfraOrgItDbContext db)
{
    public async Task<string> GetModeAsync(CancellationToken ct)
    {
        var mode = await db.ConnectionPolicies.AsNoTracking()
            .Where(x => x.Id == 1)
            .Select(x => x.Mode)
            .SingleOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(mode)
            ? nameof(ConnectionPolicyMode.Auto)
            : mode;
    }

    /// <summary>
    /// Server-side console/files/actions are disabled only in Client mode. Auto
    /// and Server keep them available; the desktop chooses the direct path in
    /// Auto when the client reaches the host.
    /// </summary>
    public async Task RequireServerTransportAsync(CancellationToken ct)
    {
        if (await GetModeAsync(ct) == nameof(ConnectionPolicyMode.Client))
            throw Guard.Forbidden(
                "La modalità Client esegue console, file e azioni dal desktop InfraOrgIT. " +
                "Apri l'istanza dall'app desktop.");
    }

    /// <summary>
    /// The desktop can prepare a direct action in Client/Auto; Server mode
    /// keeps the existing server-side runner.
    /// </summary>
    public async Task RequireClientIntentAllowedAsync(CancellationToken ct)
    {
        if (await GetModeAsync(ct) == nameof(ConnectionPolicyMode.Server))
            throw Guard.Unprocessable(
                "La modalità Server esegue le azioni sul server: usare l'esecuzione standard.");
    }
}

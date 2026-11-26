using System.Collections.Concurrent;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Inventory;

/// <summary>
/// Runs one OS update check and stores the last result on the server row.
/// Manual checks run in the background and are deduplicated per server, so a
/// double click or the hourly pass cannot start two SSH sessions.
/// </summary>
public sealed class ServerOsUpdateRunner(
    IServiceScopeFactory scopes,
    ILogger<ServerOsUpdateRunner> logger)
{
    private readonly ConcurrentDictionary<int, byte> running = new();

    /// <summary>Starts a manual check; false when the server is already running.</summary>
    public bool Start(int serverId)
    {
        if (!running.TryAdd(serverId, 0)) return false;
        _ = Task.Run(() => RunAsync(serverId, null, CancellationToken.None));
        return true;
    }

    public async Task CheckAsync(
        int serverId,
        bool defaultLtsOnly,
        CancellationToken ct)
    {
        if (!running.TryAdd(serverId, 0)) return;
        await RunAsync(serverId, defaultLtsOnly, ct);
    }

    private async Task RunAsync(
        int serverId,
        bool? defaultLtsOnly,
        CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var server = await db.Servers
                .Include(x => x.Credential)
                .SingleOrDefaultAsync(x => x.Id == serverId, ct);
            if (server is null) return;

            var ltsOnly = server.OsUpdateLtsOnly
                          ?? defaultLtsOnly
                          ?? await DefaultLtsOnlyAsync(db, ct);
            var ssh = scope.ServiceProvider.GetRequiredService<ISshConnector>();
            var checker = scope.ServiceProvider
                .GetRequiredService<ServerOsUpdateChecker>();
            var result = await checker.CheckAsync(server, ltsOnly, ct);

            server.OsName = result.OsName ?? server.OsName;
            server.OsVersion = result.OsVersion ?? server.OsVersion;
            var osName = result.OsName?.Trim();
            if (!string.IsNullOrEmpty(osName))
                server.OperatingSystem =
                    osName.Length <= 100 ? osName : osName[..100];
            server.OsUpdateTarget = result.Target;
            server.OsUpdateTargetLts =
                result.Target is null ? null : result.TargetLts;
            server.OsEolDate = result.EolDate?.ToDateTime(TimeOnly.MinValue);
            server.OsUpdateStatus = result.Status;
            server.OsUpdateDetail = result.Detail;
            server.OsUpdateCheckedAt = result.CheckedAt;
            await db.SaveChangesAsync(ct);
            if (result.Status is "Error" or "Unsupported")
                logger.LogInformation(
                    "Controllo OS server {ServerId}: {Status} {Detail}",
                    serverId, result.Status, result.Detail);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // host shutting down
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Controllo OS non completato per il server {ServerId}",
                serverId);
        }
        finally
        {
            running.TryRemove(serverId, out _);
        }
    }

    private static async Task<bool> DefaultLtsOnlyAsync(
        InfraOrgItDbContext db,
        CancellationToken ct) =>
        (await db.ServerUpdatePolicies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, ct))?.LtsOnly ?? true;
}

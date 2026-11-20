using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Inventory;

/// <summary>
/// Hourly OS update check. The last result is stored on the server row; the
/// service never writes audit rows per check to avoid flooding the log.
/// </summary>
public sealed class ServerOsUpdateService(
    IServiceScopeFactory scopes,
    ILogger<ServerOsUpdateService> logger) : BackgroundService
{
    private const int Concurrency = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            do
            {
                await RunAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // host shutting down
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var policy = await db.ServerUpdatePolicies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (policy is { Enabled: false }) return;
        var ltsOnly = policy?.LtsOnly ?? true;
        var ids = await db.Servers.AsNoTracking()
            .Where(x => x.CredentialId != null)
            .Select(x => x.Id)
            .ToListAsync(ct);
        using var semaphore = new SemaphoreSlim(Concurrency);
        var tasks = ids.Select(async id =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await CheckOneAsync(id, ltsOnly, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);
    }

    private async Task CheckOneAsync(int serverId, bool defaultLtsOnly, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var server = await db.Servers.SingleOrDefaultAsync(x => x.Id == serverId, ct);
        if (server is null) return;
        var ssh = scope.ServiceProvider.GetRequiredService<ISshConnector>();
        var checker = scope.ServiceProvider.GetRequiredService<ServerOsUpdateChecker>();
        var result = await checker.CheckAsync(
            server, server.OsUpdateLtsOnly ?? defaultLtsOnly, ct);
        server.OsName = result.OsName ?? server.OsName;
        server.OsVersion = result.OsVersion ?? server.OsVersion;
        server.OsUpdateTarget = result.Target;
        server.OsUpdateTargetLts = result.Target is null ? null : result.TargetLts;
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
}

using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Inventory;

/// <summary>
/// Hourly OS update check. The last result is stored on the server row; the
/// service never writes audit rows per check to avoid flooding the log.
/// </summary>
public sealed class ServerOsUpdateService(
    IServiceScopeFactory scopes,
    ServerOsUpdateRunner runner) : BackgroundService
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
                await runner.CheckAsync(id, ltsOnly, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);
    }
}

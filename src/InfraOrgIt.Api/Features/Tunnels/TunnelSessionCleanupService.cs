using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Tunnels;

/// <summary>
/// Marks tunnel sessions that stopped heartbeating as Stale, so a crashed
/// desktop client does not leave ghost rows in the live projection.
/// </summary>
public sealed class TunnelSessionCleanupService(
    IServiceScopeFactory scopes,
    ILogger<TunnelSessionCleanupService> logger) : BackgroundService
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await MarkStaleAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    internal async Task<int> MarkStaleAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var now = DateTime.UtcNow;
            var count = await db.TunnelSessions
                .Where(x => x.Outcome == "Active" &&
                            x.LastSeenAt < now - StaleAfter)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Outcome, "Stale")
                    .SetProperty(x => x.EndedAt, now), ct);

            if (count > 0)
                logger.LogInformation(
                    "Tunnel sessioni: {Count} sessioni scadute marcate Stale.", count);
            else
                logger.LogDebug("Tunnel sessioni: nessuna sessione da marcare Stale.");
            return count;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Pulizia tunnel sessioni fallita.");
            return 0;
        }
    }
}

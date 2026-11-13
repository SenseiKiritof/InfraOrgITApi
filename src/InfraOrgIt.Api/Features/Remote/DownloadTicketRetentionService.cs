using InfraOrgIt.Api.Infrastructure.Files;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Remote;

/// <summary>
/// GDPR storage limitation for desktop download tickets: deletes expired
/// metadata rows after the configured retention window. Tickets are removed
/// only after they expire, so an unconsumed ticket keeps its full window.
/// The purge runs in the background and in batches, never holding a lock
/// while a download streams.
/// </summary>
public sealed class DownloadTicketRetentionService(
    IServiceScopeFactory scopes,
    IOptions<DesktopDownloadsOptions> options,
    ILogger<DownloadTicketRetentionService> logger) : BackgroundService
{
    // Let startup finish before the first pass; tests call PurgeAsync(now)
    // directly instead of waiting for the hosted loop.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(
            Math.Clamp(options.Value.CleanupIntervalMinutes, 15, 10_080));

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            using var timer = new PeriodicTimer(interval);
            do
            {
                await PurgeAsync(DateTime.UtcNow, stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// Deletes tickets whose expiry is older than <paramref name="now"/> minus
    /// the retention window. Consumed and unconsumed rows follow the same rule.
    /// </summary>
    internal async Task<int> PurgeAsync(DateTime now, CancellationToken ct)
    {
        var days = Math.Clamp(options.Value.RetentionDays, 1, 365);
        var cutoff = now.AddDays(-days);
        var total = 0;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            while (!ct.IsCancellationRequested)
            {
                var ids = await db.DownloadTickets.AsNoTracking()
                    .Where(x => x.ExpiresAt < cutoff)
                    .OrderBy(x => x.Id)
                    .Select(x => x.Id)
                    .Take(BatchSize)
                    .ToListAsync(ct);
                if (ids.Count == 0)
                    break;

                total += await db.DownloadTickets
                    .Where(x => ids.Contains(x.Id))
                    .ExecuteDeleteAsync(ct);
                if (ids.Count < BatchSize)
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Never log token hashes, ticket paths or row contents: only the
            // count and the configured window are diagnostic here.
            logger.LogError(exception,
                "Pulizia ticket download fallita dopo {Deleted} righe (retention {Days} giorni).",
                total, days);
            return total;
        }

        if (total > 0)
            logger.LogInformation(
                "Pulizia ticket download: eliminate {Count} righe scadute oltre {Days} giorni.",
                total, days);
        else
            logger.LogDebug(
                "Pulizia ticket download: nessuna riga da eliminare (retention {Days} giorni).",
                days);

        return total;
    }
}

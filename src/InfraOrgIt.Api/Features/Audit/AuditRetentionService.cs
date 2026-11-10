using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Audit;

public sealed class AuditRetentionOptions
{
    public const string SectionName = "Audit";

    public int RetentionDays { get; set; } = 180;
}

/// <summary>
/// Deletes audit rows older than the configured retention in small batches.
/// It runs in the background, so a slow database never delays startup.
/// </summary>
public sealed class AuditRetentionService(
    IServiceScopeFactory scopes,
    IOptions<AuditRetentionOptions> options,
    ILogger<AuditRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await PurgeAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    internal async Task<int> PurgeAsync(CancellationToken ct)
    {
        var days = Math.Clamp(options.Value.RetentionDays, 1, 3650);
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var total = 0;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            while (!ct.IsCancellationRequested)
            {
                var ids = await db.AuditEvents.AsNoTracking()
                    .Where(x => x.OccurredAt < cutoff)
                    .OrderBy(x => x.Id)
                    .Select(x => x.Id)
                    .Take(BatchSize)
                    .ToListAsync(ct);
                if (ids.Count == 0)
                    break;

                total += await db.AuditEvents
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
            logger.LogError(exception,
                "Pulizia audit fallita dopo {Deleted} righe (retention {Days} giorni).",
                total, days);
            return total;
        }

        if (total > 0)
            logger.LogInformation(
                "Pulizia audit: eliminate {Count} righe più vecchie di {Days} giorni.",
                total, days);
        else
            logger.LogDebug(
                "Pulizia audit: nessuna riga da eliminare (retention {Days} giorni).", days);

        return total;
    }
}

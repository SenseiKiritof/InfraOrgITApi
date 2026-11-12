using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Audit;

public sealed class AuditAnchorOptions
{
    public const string SectionName = "Audit:Anchor";

    /// <summary>
    /// Sends the current chain head to an external append-only sink. The API
    /// itself stays tamper-evident, not WORM: the sink is what preserves proof
    /// against a database writer who rewrites or deletes the chain.
    /// </summary>
    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 5;
    public string WebhookUrl { get; set; } = "";
    public string HmacKey { get; set; } = "";
    public string InstanceId { get; set; } = "";
}

/// <summary>
/// One anchor sample: <c>lastId</c>/<c>chainHead</c>/<c>occurredAt</c> describe
/// the newest hashed audit row, <c>checked</c> is the UTC time the sample was
/// taken. No audit detail is exported.
/// </summary>
public sealed record AuditAnchorPayload(
    string InstanceId,
    int LastId,
    string ChainHead,
    DateTime OccurredAt,
    DateTime Checked);

public interface IAuditAnchorSource
{
    Task<AuditAnchorPayload?> ReadHeadAsync(
        string instanceId,
        DateTime checkedAt,
        CancellationToken ct);
}

/// <summary>Reads the newest hash-chained row, skipping legacy rows without a hash.</summary>
public sealed class AuditAnchorSource(InfraOrgItDbContext db) : IAuditAnchorSource
{
    public async Task<AuditAnchorPayload?> ReadHeadAsync(
        string instanceId,
        DateTime checkedAt,
        CancellationToken ct)
    {
        var head = await db.AuditEvents.AsNoTracking()
            .Where(x => x.Hash != null)
            .OrderByDescending(x => x.Id)
            .Select(x => new { x.Id, x.Hash, x.OccurredAt })
            .FirstOrDefaultAsync(ct);
        if (head is null)
            return null;

        return new AuditAnchorPayload(
            instanceId,
            head.Id,
            head.Hash!,
            DateTime.SpecifyKind(head.OccurredAt, DateTimeKind.Utc),
            checkedAt);
    }
}

/// <summary>
/// One anchor attempt. Split from the hosted service so tests can use a fake
/// source and publisher without a database.
/// </summary>
public sealed class AuditAnchorRunner(
    IAuditAnchorSource source,
    AuditAnchorPublisher publisher,
    IOptions<AuditAnchorOptions> options,
    ILogger<AuditAnchorRunner> logger)
{
    public async Task<bool> RunAsync(CancellationToken ct)
    {
        var anchor = options.Value;
        if (!anchor.Enabled)
            return false;

        var payload = await source.ReadHeadAsync(
            anchor.InstanceId, DateTime.UtcNow, ct);
        if (payload is null)
        {
            logger.LogDebug("Ancoraggio audit: nessuna riga con hash da pubblicare.");
            return false;
        }

        try
        {
            await publisher.PublishAsync(payload, ct);
            logger.LogInformation(
                "Ancoraggio audit pubblicato per l'istanza {InstanceId}.",
                payload.InstanceId);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A missing SIEM must never take the API down; retry next interval.
            logger.LogError(exception,
                "Ancoraggio audit fallito per l'istanza {InstanceId}; nuovo tentativo al prossimo intervallo.",
                payload.InstanceId);
            return false;
        }
    }
}

/// <summary>
/// Periodically pushes the audit chain head to the configured webhook. The
/// consumer is expected to be append-only (SIEM or WORM bucket); that external
/// copy is what makes prefix/suffix deletion of the local chain detectable.
/// </summary>
public sealed class AuditAnchorService(
    IServiceScopeFactory scopes,
    IOptions<AuditAnchorOptions> options,
    ILogger<AuditAnchorService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            using var timer = new PeriodicTimer(
                TimeSpan.FromMinutes(Math.Clamp(options.Value.IntervalMinutes, 1, 1440)));
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<AuditAnchorRunner>()
                        .RunAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception,
                        "Ancoraggio audit non eseguito; nuovo tentativo tra {Minutes} minuti.",
                        options.Value.IntervalMinutes);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}

using System.Collections.Concurrent;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Haproxy;

/// <summary>
/// Reads the HAProxy stats page every 15 s for HTTP/2, HTTP/3 and QUIC counters.
/// It runs only for instances with StatsBaseUrl and stats credentials.
/// </summary>
public sealed class HaproxyProtocolMetricsService(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<HaproxyProtocolMetricsService> logger) : BackgroundService
{
    private sealed class Saved
    {
        public DateTime LastMinute { get; set; }
        public DateTime LastFive { get; set; }
    }

    private readonly ConcurrentDictionary<int, Saved> saved = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Haproxy:ProtocolMetricsEnabled", true))
            return;
        var interval = TimeSpan.FromSeconds(configuration.GetValue(
            "Haproxy:ProtocolMetricsIntervalSeconds", 15));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
            using var timer = new PeriodicTimer(interval);
            do
            {
                await TickAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // host shutting down
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var ids = await db.HaproxyInstances.AsNoTracking()
            .Where(x => x.MonitoringChoice == "Enabled" &&
                        x.StatsBaseUrl != null &&
                        x.StatsUsername != null &&
                        x.EncryptedStatsPassword != null)
            .Select(x => x.Id)
            .ToListAsync(ct);
        foreach (var id in ids)
        {
            try
            {
                await CollectAsync(id, ct);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(exception,
                    "Metriche protocollo HAProxy non raccolte per {InstanceId}",
                    id);
            }
        }
    }

    private async Task CollectAsync(int instanceId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var instance = await db.HaproxyInstances
            .Include(x => x.Server).ThenInclude(x => x!.Credential)
            .SingleOrDefaultAsync(x => x.Id == instanceId, ct);
        if (instance?.Server is null) return;
        var factory = scope.ServiceProvider
            .GetRequiredService<HaproxyStatsClientFactory>();
        await using var client = await factory.CreateAsync(
            instance, instance.Server, ct);
        var csv = await client.GetCsvAsync(ct);
        var rows = HaproxyStatsCsv.ParseProtocol(csv);
        if (rows.Count == 0) return;

        var now = DateTime.UtcNow;
        var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour,
            now.Minute, 0, DateTimeKind.Utc);
        var five = new DateTime(now.Year, now.Month, now.Day, now.Hour,
            now.Minute / 5 * 5, 0, DateTimeKind.Utc);
        var state = saved.GetOrAdd(instanceId, _ => new Saved());
        var wrote = false;
        if (state.LastMinute != minute)
        {
            await SaveAsync(db, instanceId, 1, minute, rows, ct);
            state.LastMinute = minute;
            wrote = true;
        }
        if (state.LastFive != five)
        {
            await SaveAsync(db, instanceId, 5, five, rows, ct);
            state.LastFive = five;
            wrote = true;
        }
        if (!wrote) return;
        await db.SaveChangesAsync(ct);
        var cutoff = now.AddDays(-7);
        var longCutoff = now.AddDays(-90);
        await db.HaproxyProtocolMetricSamples
            .Where(x => x.HaproxyInstanceId == instanceId &&
                        ((x.ResolutionMinutes == 1 && x.Bucket < cutoff) ||
                         (x.ResolutionMinutes == 5 && x.Bucket < longCutoff)))
            .ExecuteDeleteAsync(ct);
    }

    private static async Task SaveAsync(
        InfraOrgItDbContext db,
        int instanceId,
        int resolution,
        DateTime bucket,
        IReadOnlyList<HaproxyProtocolRow> rows,
        CancellationToken ct)
    {
        var exists = await db.HaproxyProtocolMetricSamples.AnyAsync(
            x => x.HaproxyInstanceId == instanceId &&
                 x.ResolutionMinutes == resolution &&
                 x.Bucket == bucket, ct);
        if (exists) return;
        foreach (var row in rows)
            db.HaproxyProtocolMetricSamples.Add(
                new HaproxyProtocolMetricSample
                {
                    HaproxyInstanceId = instanceId,
                    ResolutionMinutes = resolution,
                    Bucket = bucket,
                    Kind = row.Kind,
                    Proxy = row.Proxy,
                    Server = row.Server,
                    H2Req = row.H2Req,
                    H2Sess = row.H2Sess,
                    H3Req = row.H3Req,
                    H3Sess = row.H3Sess,
                    H2OpenConnections = row.H2OpenConnections,
                    H2BackendOpenStreams = row.H2BackendOpenStreams,
                    H2ConnErrors = row.H2ConnErrors,
                    H2StreamErrors = row.H2StreamErrors,
                    QuicHandshakeFail = row.QuicHandshakeFail,
                    QuicDroppedPkt = row.QuicDroppedPkt,
                    QuicLostPkt = row.QuicLostPkt,
                    QuicSentPkt = row.QuicSentPkt,
                    QuicRetryError = row.QuicRetryError,
                    QuicTooShortDgram = row.QuicTooShortDgram,
                    QuicRxbufFull = row.QuicRxbufFull,
                    QuicSocketFull = row.QuicSocketFull,
                    H3VersionFallback = row.H3VersionFallback,
                    H3ProtocolErrors = row.H3ProtocolErrors,
                    QuicTransportErrors = row.QuicTransportErrors,
                    QpackEncoderErrors = row.QpackEncoderErrors,
                    QpackDecoderErrors = row.QpackDecoderErrors,
                    SslFailedHandshake = row.SslFailedHandshake
                });
    }
}

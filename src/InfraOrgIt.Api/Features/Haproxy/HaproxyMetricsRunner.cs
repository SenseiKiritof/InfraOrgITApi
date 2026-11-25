using System.Collections.Concurrent;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Haproxy;

public sealed record HaproxyLiveSeries(
    string Kind,
    string Proxy,
    string Server,
    string Status,
    string? CheckStatus,
    long ReqTotal,
    long H4xx,
    long H5xx,
    long Scur,
    long Qcur,
    long Rate,
    long ReqRate,
    long Rtime,
    long Ctime,
    long Ttime,
    long Bin,
    long Bout);

public sealed record HaproxyLiveProcess(
    long Uptime,
    long CurrConns,
    long CumConns,
    long CurrSslConns,
    long CumSslConns,
    long MaxConn,
    long SessRate,
    long ConnRate,
    long Tasks,
    long RunQueue,
    long IdlePct,
    long Threads,
    long ActivePeers,
    long ConnectedPeers);

public sealed record HaproxyLiveSnapshot(
    DateTime At,
    IReadOnlyList<HaproxyLiveSeries> Series,
    HaproxyLiveProcess? Process);

public sealed class HaproxyMetricsCache
{
    private readonly ConcurrentDictionary<int, HaproxyLiveSnapshot> latest = new();

    public void Set(int instanceId, HaproxyLiveSnapshot snapshot) =>
        latest[instanceId] = snapshot;

    public HaproxyLiveSnapshot? Get(int instanceId) =>
        latest.TryGetValue(instanceId, out var snapshot) ? snapshot : null;
}

public sealed class HaproxyMetricsRunner(
    IServiceScopeFactory scopes,
    HaproxyClientCache clients,
    HaproxyMetricsCache cache,
    IConfiguration configuration,
    ILogger<HaproxyMetricsRunner> logger)
{
    private sealed class Saved
    {
        public DateTime LastPoll { get; set; }
        public DateTime LastMinute { get; set; }
        public DateTime LastFiveMinutes { get; set; }
        public DateTime LastPrune { get; set; }
    }

    private readonly ConcurrentDictionary<int, Saved> saved = new();
    private readonly TimeSpan statsOnlyInterval = TimeSpan.FromSeconds(
        configuration.GetValue("Haproxy:StatsOnlyIntervalSeconds", 15));
    private readonly TimeSpan rawRetention = TimeSpan.FromDays(Math.Clamp(
        configuration.GetValue("Haproxy:MetricsRawRetentionDays", 7), 1, 3650));
    private readonly TimeSpan longRetention = TimeSpan.FromDays(Math.Clamp(
        configuration.GetValue("Haproxy:MetricsRetentionDays", 90), 1, 3650));

    public async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var ids = await db.HaproxyInstances.AsNoTracking()
            .Where(x => x.MonitoringChoice == "Enabled" &&
                        (x.ApiVersion == "stats"
                            ? x.StatsBaseUrl != null
                            : x.EncryptedPassword != null &&
                              x.Username != null))
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
                    "Metriche HAProxy non raccolte per l'istanza {InstanceId}", id);
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

        var at = DateTime.UtcNow;
        var state = saved.GetOrAdd(instanceId, _ => new Saved());
        var statsOnly = instance.ApiVersion == "stats";
        if (statsOnly && at - state.LastPoll < statsOnlyInterval) return;
        state.LastPoll = at;

        IReadOnlyList<HaproxyStatsRow> stats;
        HaproxyProcessInfo? process = null;
        if (statsOnly)
        {
            var factory = scope.ServiceProvider
                .GetRequiredService<HaproxyStatsClientFactory>();
            await using var statsClient = await factory.CreateAsync(
                instance, instance.Server, ct);
            stats = HaproxyStatsCsv.ParseStats(
                await statsClient.GetCsvAsync(ct));
        }
        else
        {
            var client = await clients.GetAsync(instance, instance.Server, ct);
            stats = await client.StatsAsync(ct);
            process = await client.ProcessAsync(ct);
        }

        cache.Set(instanceId, new HaproxyLiveSnapshot(
            at,
            stats.Select(row => new HaproxyLiveSeries(
                row.Kind, row.Proxy, row.Server, row.Status, row.CheckStatus,
                row.ReqTot, row.H4xx, row.H5xx, row.Scur, row.Qcur, row.Rate,
                row.ReqRate, row.Rtime, row.Ctime, row.Ttime, row.Bin,
                row.Bout)).ToList(),
            process is null
                ? null
                : new HaproxyLiveProcess(
                    process.Uptime, process.CurrConns, process.CumConns,
                    process.CurrSslConns, process.CumSslConns, process.MaxConn,
                    process.SessRate, process.ConnRate, process.Tasks,
                    process.RunQueue, process.IdlePct, process.Threads,
                    process.ActivePeers, process.ConnectedPeers)));

        var minute = new DateTime(at.Year, at.Month, at.Day, at.Hour,
            at.Minute, 0, DateTimeKind.Utc);
        var five = new DateTime(at.Year, at.Month, at.Day, at.Hour,
            at.Minute / 5 * 5, 0, DateTimeKind.Utc);
        var wrote = false;
        if (state.LastMinute != minute)
        {
            await SaveAsync(db, instanceId, 1, minute, stats, process, ct);
            state.LastMinute = minute;
            wrote = true;
        }
        if (state.LastFiveMinutes != five)
        {
            await SaveAsync(db, instanceId, 5, five, stats, process, ct);
            state.LastFiveMinutes = five;
            wrote = true;
        }
        if (wrote) await db.SaveChangesAsync(ct);

        if (at - state.LastPrune > TimeSpan.FromHours(1))
        {
            var raw = at - rawRetention;
            var longTerm = at - longRetention;
            await db.HaproxyMetricSamples
                .Where(x => x.HaproxyInstanceId == instanceId &&
                            ((x.ResolutionMinutes == 1 && x.Bucket < raw) ||
                             (x.ResolutionMinutes == 5 && x.Bucket < longTerm)))
                .ExecuteDeleteAsync(ct);
            await db.HaproxyProcessMetricSamples
                .Where(x => x.HaproxyInstanceId == instanceId &&
                            ((x.ResolutionMinutes == 1 && x.Bucket < raw) ||
                             (x.ResolutionMinutes == 5 && x.Bucket < longTerm)))
                .ExecuteDeleteAsync(ct);
            state.LastPrune = at;
        }
    }

    private static async Task SaveAsync(
        InfraOrgItDbContext db,
        int instanceId,
        int resolution,
        DateTime bucket,
        IReadOnlyList<HaproxyStatsRow> stats,
        HaproxyProcessInfo? process,
        CancellationToken ct)
    {
        var exists = await db.HaproxyMetricSamples.AnyAsync(
            x => x.HaproxyInstanceId == instanceId &&
                 x.ResolutionMinutes == resolution &&
                 x.Bucket == bucket, ct);
        if (exists) return;
        foreach (var row in stats)
            db.HaproxyMetricSamples.Add(new HaproxyMetricSample
            {
                HaproxyInstanceId = instanceId,
                ResolutionMinutes = resolution,
                Bucket = bucket,
                Kind = row.Kind,
                Proxy = row.Proxy,
                Server = row.Server,
                Status = row.Status,
                CheckStatus = row.CheckStatus,
                Req = row.ReqTot > 0 ? row.ReqTot : row.Stot,
                H1xx = row.H1xx,
                H2xx = row.H2xx,
                H3xx = row.H3xx,
                H4xx = row.H4xx,
                H5xx = row.H5xx,
                HOther = row.HOther,
                Bin = row.Bin,
                Bout = row.Bout,
                Econ = row.Econ,
                Eresp = row.Eresp,
                Dreq = row.Dreq,
                Dresp = row.Dresp,
                CliAbrt = row.CliAbrt,
                SrvAbrt = row.SrvAbrt,
                ConnTot = row.ConnTot,
                Dcon = row.Dcon,
                Dses = row.Dses,
                Lbtot = row.Lbtot,
                Wretr = row.Wretr,
                Wredis = row.Wredis,
                Scur = row.Scur,
                Smax = row.Smax,
                Slim = row.Slim,
                Qcur = row.Qcur,
                Qmax = row.Qmax,
                Rate = row.Rate,
                RateMax = row.RateMax,
                ReqRate = row.ReqRate,
                ReqRateMax = row.ReqRateMax,
                ConnRate = row.ConnRate,
                ConnRateMax = row.ConnRateMax,
                Rtime = row.Rtime,
                Ctime = row.Ctime,
                Ttime = row.Ttime,
                Qtime = row.Qtime,
                Downtime = row.Downtime,
                Lastsess = row.Lastsess,
                Weight = row.Weight,
                CheckHealth = row.CheckHealth,
                Chkfail = row.Chkfail,
                Chkdown = row.Chkdown
            });
        if (process is null) return;
        db.HaproxyProcessMetricSamples.Add(new HaproxyProcessMetricSample
        {
            HaproxyInstanceId = instanceId,
            ResolutionMinutes = resolution,
            Bucket = bucket,
            Uptime = process.Uptime,
            CurrConns = process.CurrConns,
            CumConns = process.CumConns,
            CurrSslConns = process.CurrSslConns,
            CumSslConns = process.CumSslConns,
            MaxConn = process.MaxConn,
            SessRate = process.SessRate,
            ConnRate = process.ConnRate,
            Tasks = process.Tasks,
            RunQueue = process.RunQueue,
            IdlePct = process.IdlePct,
            MaxMemoryMb = process.MaxMemoryMb,
            Threads = process.Threads,
            ActivePeers = process.ActivePeers,
            ConnectedPeers = process.ConnectedPeers,
            FailedResolutions = process.FailedResolutions,
            DroppedLogs = process.DroppedLogs,
            BytesOutRate = process.BytesOutRate
        });
    }
}

public sealed class HaproxyMetricsService(
    HaproxyMetricsRunner runner,
    IConfiguration configuration,
    ILogger<HaproxyMetricsService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Haproxy:MetricsEnabled", true)) return;
        var interval = TimeSpan.FromSeconds(configuration.GetValue(
            "Haproxy:MetricsIntervalSeconds", 5));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    await runner.TickAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception,
                        "Raccolta metriche HAProxy non riuscita");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // host shutting down
        }
    }
}

using System.Collections.Concurrent;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Haproxy;

/// <summary>
/// Refreshes the read-only route/server snapshots for one instance. Manual and
/// automatic refreshes are deduplicated per instance.
/// </summary>
public sealed class HaproxyRefreshRunner(
    IServiceScopeFactory scopes,
    ILogger<HaproxyRefreshRunner> logger)
{
    private readonly ConcurrentDictionary<int, byte> running = new();

    public bool Start(int instanceId)
    {
        if (!running.TryAdd(instanceId, 0)) return false;
        _ = Task.Run(() => RefreshClaimedAsync(instanceId, CancellationToken.None));
        return true;
    }

    public async Task<HaproxyRefreshOutcome> RefreshAsync(
        int instanceId,
        CancellationToken ct)
    {
        if (!running.TryAdd(instanceId, 0))
            throw new InvalidOperationException(
                "Aggiornamento HAProxy già in corso per questa istanza.");
        try
        {
            return await RefreshCoreAsync(instanceId, ct);
        }
        finally
        {
            running.TryRemove(instanceId, out _);
        }
    }

    private async Task RefreshClaimedAsync(int instanceId, CancellationToken ct)
    {
        try
        {
            await RefreshCoreAsync(instanceId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // host shutting down
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Aggiornamento HAProxy non riuscito per l'istanza {InstanceId}",
                instanceId);
        }
        finally
        {
            running.TryRemove(instanceId, out _);
        }
    }

    private async Task<HaproxyRefreshOutcome> RefreshCoreAsync(
        int instanceId,
        CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
        var instance = await db.HaproxyInstances
            .Include(x => x.Server)
            .ThenInclude(x => x!.Credential)
            .SingleOrDefaultAsync(x => x.Id == instanceId, ct)
            ?? throw new InvalidOperationException("Istanza HAProxy inesistente.");
        if (instance.Server is null)
            throw new InvalidOperationException("Server HAProxy inesistente.");
        var statsOnly = instance.ApiVersion == "stats";
        if (!statsOnly &&
            (string.IsNullOrEmpty(instance.Username) ||
             string.IsNullOrEmpty(instance.EncryptedPassword)))
            throw new InvalidOperationException(
                "Credenziali Data Plane API non configurate.");

        var now = DateTime.UtcNow;
        try
        {
            HaproxyRefreshResult result;
            if (statsOnly)
            {
                instance.EffectiveApiVersion = "stats";
                var statsFactory = scope.ServiceProvider
                    .GetRequiredService<HaproxyStatsClientFactory>();
                await using var statsClient = await statsFactory.CreateAsync(
                    instance, instance.Server, ct);
                var rows = HaproxyStatsCsv.ParseStats(
                    await statsClient.GetCsvAsync(ct));
                result = new HaproxyRefreshResult(
                    [],
                    rows.Where(x => x.Kind == "server")
                        .Select(x => new HaproxyServerResult(
                            x.Proxy, x.Server, x.Address, x.Status,
                            x.CheckStatus, (int)x.Weight))
                        .ToList(),
                    [],
                    null);
            }
            else
            {
                var clients = scope.ServiceProvider
                    .GetRequiredService<HaproxyClientFactory>();
                await using var client = await clients.CreateAsync(
                    instance, instance.Server, ct);
                result = await client.RefreshAsync(ct);
            }

            await using var transaction =
                await db.Database.BeginTransactionAsync(ct);
            await db.HaproxyRouteSnapshots
                .Where(x => x.HaproxyInstanceId == instance.Id)
                .ExecuteDeleteAsync(ct);
            await db.HaproxyServerSnapshots
                .Where(x => x.HaproxyInstanceId == instance.Id)
                .ExecuteDeleteAsync(ct);
            await db.HaproxyCertificateSnapshots
                .Where(x => x.HaproxyInstanceId == instance.Id)
                .ExecuteDeleteAsync(ct);
            foreach (var route in result.Routes)
                db.HaproxyRouteSnapshots.Add(new HaproxyRouteSnapshot
                {
                    HaproxyInstanceId = instance.Id,
                    Frontend = route.Frontend,
                    Bind = route.Bind,
                    Mode = route.Mode,
                    Host = route.Host,
                    Path = route.Path,
                    Backend = route.Backend,
                    IsDefault = route.IsDefault,
                    RawCondition = route.RawCondition,
                    ConfigurationVersion = result.ConfigurationVersion,
                    CollectedAt = now
                });
            foreach (var server in result.Servers)
                db.HaproxyServerSnapshots.Add(new HaproxyServerSnapshot
                {
                    HaproxyInstanceId = instance.Id,
                    Backend = server.Backend,
                    Server = server.Server,
                    Address = server.Address,
                    Status = server.Status,
                    CheckStatus = server.CheckStatus,
                    Weight = server.Weight,
                    CollectedAt = now
                });
            foreach (var certificate in result.Certificates)
                db.HaproxyCertificateSnapshots.Add(new HaproxyCertificateSnapshot
                {
                    HaproxyInstanceId = instance.Id,
                    StorageName = certificate.StorageName,
                    Path = certificate.Path,
                    Domains = certificate.Domains,
                    Issuer = certificate.Issuer,
                    NotBefore = certificate.NotBefore,
                    NotAfter = certificate.NotAfter,
                    Size = certificate.Size,
                    CollectedAt = now
                });

            instance.ConfigurationVersion = result.ConfigurationVersion;
            instance.LastRefreshAt = now;
            instance.LastRefreshStatus = "Ok";
            instance.LastRefreshDetail = null;
            instance.LastTestAt = now;
            instance.LastTestStatus = "Ok";
            instance.LastTestDetail = null;
            instance.DiscoveryStatus = "Ready";
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new HaproxyRefreshOutcome(
                result.Routes.Count, result.Servers.Count,
                result.Certificates.Count, now);
        }
        catch (Exception exception)
        {
            instance.LastRefreshAt = now;
            instance.LastRefreshStatus = "Error";
            instance.LastRefreshDetail = Trim(exception.Message, 500);
            instance.LastTestAt = now;
            instance.LastTestStatus = "Error";
            instance.LastTestDetail = Trim(exception.Message, 500);
            instance.DiscoveryStatus = "Unreachable";
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning(exception,
                "Aggiornamento HAProxy non riuscito per l'istanza {InstanceId}",
                instanceId);
            throw;
        }
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

public sealed record HaproxyRefreshOutcome(
    int Routes,
    int Servers,
    int Certificates,
    DateTime RefreshedAt);

/// <summary>Periodic read-only snapshot refresh for instances with monitoring enabled.</summary>
public sealed class HaproxyRefreshService(
    IServiceScopeFactory scopes,
    HaproxyRefreshRunner runner,
    IConfiguration configuration,
    ILogger<HaproxyRefreshService> logger) : BackgroundService
{
    private const int Concurrency = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Haproxy:RefreshEnabled", true)) return;
        var interval = TimeSpan.FromSeconds(configuration.GetValue(
            "Haproxy:RefreshIntervalSeconds", 60));
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    await RefreshAllAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception,
                        "Aggiornamento HAProxy periodico non riuscito");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // host shutting down
        }
    }

    private async Task RefreshAllAsync(CancellationToken ct)
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
        using var semaphore = new SemaphoreSlim(Concurrency);
        await Task.WhenAll(ids.Select(async id =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await runner.RefreshAsync(id, ct);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(exception,
                    "Aggiornamento HAProxy periodico non riuscito per l'istanza {InstanceId}",
                    id);
            }
            finally
            {
                semaphore.Release();
            }
        }));
    }
}

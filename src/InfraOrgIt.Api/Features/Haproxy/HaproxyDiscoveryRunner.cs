using System.Collections.Concurrent;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Haproxy;

/// <summary>
/// Discovers HAProxy over SSH and updates the candidate instance. Discovery is
/// read-only, never starts monitoring and never touches credentials.
/// </summary>
public sealed class HaproxyDiscoveryRunner(
    IServiceScopeFactory scopes,
    ILogger<HaproxyDiscoveryRunner> logger)
{
    private const int Concurrency = 4;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<int, byte> running = new();
    private int runningAll;

    public async Task RunAllAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref runningAll, 1) == 1) return;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var ids = await db.Servers.AsNoTracking()
                .Where(x => x.CredentialId != null)
                .Where(x => !db.HaproxyInstances.Any(h =>
                    h.ServerId == x.Id && h.MonitoringChoice == "Disabled"))
                .Select(x => x.Id)
                .ToListAsync(ct);

            using var semaphore = new SemaphoreSlim(Concurrency);
            await Task.WhenAll(ids.Select(async id =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    await DiscoverServerAsync(id, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }
        finally
        {
            Interlocked.Exchange(ref runningAll, 0);
        }
    }

    public bool Start(int serverId)
    {
        if (!running.TryAdd(serverId, 0)) return false;
        _ = Task.Run(() => DiscoverClaimedAsync(serverId, CancellationToken.None));
        return true;
    }

    private async Task DiscoverServerAsync(int serverId, CancellationToken ct)
    {
        if (!running.TryAdd(serverId, 0)) return;
        await DiscoverClaimedAsync(serverId, ct);
    }

    private async Task DiscoverClaimedAsync(int serverId, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>();
            var server = await db.Servers
                .Include(x => x.Credential)
                .SingleOrDefaultAsync(x => x.Id == serverId, ct);
            if (server is null || server.CredentialId is null) return;

            var ssh = scope.ServiceProvider.GetRequiredService<ISshConnector>();
            await using var session = await ssh.ConnectAsync(server, ct);
            var result = await session.RunAsync(HaproxyDiscovery.Script, Timeout, ct);
            if (result.TimedOut)
            {
                logger.LogInformation(
                    "Scoperta HAProxy sul server {ServerId}: timeout", serverId);
                return;
            }

            var info = HaproxyDiscovery.Parse(result.Output);
            var instance = await db.HaproxyInstances
                .SingleOrDefaultAsync(x => x.ServerId == serverId, ct);
            if (instance is null)
            {
                if (!info.HasHaproxy) return;
                instance = new HaproxyInstance
                {
                    ServerId = server.Id,
                    Name = NameFor(server),
                    BaseUrl = $"http://127.0.0.1:{info.ApiPort ?? 5555}",
                    Transport = "SshForward",
                    ApiHost = "127.0.0.1",
                    ApiPort = info.ApiPort ?? 5555,
                    ApiVersion = "auto",
                    MonitoringChoice = "Undecided",
                    DiscoverySource = "Auto",
                    DiscoveryStatus = "Found"
                };
                db.HaproxyInstances.Add(instance);
            }

            instance.DiscoveryStatus = info.HasHaproxy
                ? info.ApiPort is not null ? "Found" : "Inactive"
                : "Inactive";
            instance.LastSeenAt = DateTime.UtcNow;
            instance.DetectedVersion = info.Version ?? instance.DetectedVersion;
            instance.DetectedConfigPath = info.ConfigPath ?? instance.DetectedConfigPath;
            if (instance.ServerApplicationId is null)
                instance.ServerApplicationId = await db.ServerApplications
                    .Where(x => x.ServerId == server.Id &&
                                x.ApplicationVersion!.Application!.Type == "HAProxy")
                    .Select(x => (int?)x.Id)
                    .FirstOrDefaultAsync(ct);

            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // host shutting down
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Scoperta HAProxy non riuscita sul server {ServerId}",
                serverId);
        }
        finally
        {
            running.TryRemove(serverId, out _);
        }
    }

    private static string NameFor(Server server)
    {
        var raw = $"haproxy-{server.Name}".Trim();
        return raw.Length <= 64 ? raw : raw[..64];
    }
}

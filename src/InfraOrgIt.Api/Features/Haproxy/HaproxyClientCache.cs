using System.Collections.Concurrent;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;

namespace InfraOrgIt.Api.Features.Haproxy;

/// <summary>
/// Keeps one Data Plane API client per instance for the 5 s live polling. A
/// settings change increments HaproxyInstance.Version and replaces the client.
/// </summary>
public sealed class HaproxyClientCache(
    IServiceScopeFactory scopes,
    ILogger<HaproxyClientCache> logger) : IAsyncDisposable
{
    private sealed record Entry(int Version, HaproxyClient Client);

    private readonly ConcurrentDictionary<int, Entry> entries = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> gates = new();

    public async Task<HaproxyClient> GetAsync(
        HaproxyInstance instance,
        Server server,
        CancellationToken ct)
    {
        if (entries.TryGetValue(instance.Id, out var existing) &&
            existing.Version == instance.Version)
            return existing.Client;

        var gate = gates.GetOrAdd(instance.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (entries.TryGetValue(instance.Id, out existing) &&
                existing.Version == instance.Version)
                return existing.Client;

            using var scope = scopes.CreateScope();
            var factory = scope.ServiceProvider
                .GetRequiredService<HaproxyClientFactory>();
            var client = await factory.CreateAsync(instance, server, ct);
            var entry = new Entry(instance.Version, client);
            if (entries.TryGetValue(instance.Id, out var previous))
            {
                entries[instance.Id] = entry;
                await previous.Client.DisposeAsync();
            }
            else
            {
                entries[instance.Id] = entry;
            }
            return client;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task InvalidateAsync(int instanceId)
    {
        if (entries.TryRemove(instanceId, out var entry))
        {
            try
            {
                await entry.Client.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception,
                    "Chiusura client HAProxy {InstanceId} non riuscita", instanceId);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in entries.Values)
            await entry.Client.DisposeAsync();
        entries.Clear();
        foreach (var gate in gates.Values) gate.Dispose();
        gates.Clear();
    }
}

namespace InfraOrgIt.Api.Features.Haproxy;

/// <summary>Hourly read-only HAProxy discovery. It only creates/updates candidates.</summary>
public sealed class HaproxyDiscoveryService(
    HaproxyDiscoveryRunner runner,
    IConfiguration configuration,
    ILogger<HaproxyDiscoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Haproxy:DiscoveryEnabled", true)) return;
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(4), stoppingToken);
            using var timer = new PeriodicTimer(
                TimeSpan.FromMinutes(configuration.GetValue(
                    "Haproxy:DiscoveryIntervalMinutes", 60)));
            do
            {
                try
                {
                    await runner.RunAllAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Scoperta HAProxy oraria non riuscita");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // host shutting down
        }
    }
}

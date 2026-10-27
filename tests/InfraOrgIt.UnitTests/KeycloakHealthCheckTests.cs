using InfraOrgIt.Api.Infrastructure.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace InfraOrgIt.UnitTests;

public sealed class KeycloakHealthCheckTests
{
    [Fact]
    public async Task Healthy_when_configuration_is_available()
    {
        var check = new KeycloakHealthCheck(new Manager());

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Unhealthy_when_configuration_fails()
    {
        var check = new KeycloakHealthCheck(
            new Manager(new InvalidOperationException("discovery non raggiungibile")));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Keycloak non raggiungibile", result.Description);
    }

    private sealed class Manager(Exception? error = null) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            error is null
                ? Task.FromResult(new OpenIdConnectConfiguration())
                : Task.FromException<OpenIdConnectConfiguration>(error);

        public void RequestRefresh()
        {
        }

        public TimeSpan AutomaticRefreshInterval { get; set; } = TimeSpan.FromMinutes(12);

        public DateTimeOffset? LastKnownGoodConfiguration => null;
    }
}

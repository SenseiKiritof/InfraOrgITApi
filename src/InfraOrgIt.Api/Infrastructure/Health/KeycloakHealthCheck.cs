using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace InfraOrgIt.Api.Infrastructure.Health;

// Usa il ConfigurationManager condiviso: la discovery resta in cache e non viene
// richiesta a Keycloak a ogni probe.
public sealed class KeycloakHealthCheck(
    IConfigurationManager<OpenIdConnectConfiguration> configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await configuration.GetConfigurationAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Keycloak non raggiungibile.");
        }
    }
}

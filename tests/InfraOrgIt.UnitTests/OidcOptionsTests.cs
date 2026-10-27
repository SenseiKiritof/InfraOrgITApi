using InfraOrgIt.Api.BuildingBlocks.Auth;

namespace InfraOrgIt.UnitTests;

public sealed class OidcOptionsTests
{
    [Fact]
    public void Internal_issuer_serves_server_endpoints_and_public_issuer_serves_browser()
    {
        var options = new OidcOptions
        {
            Issuer = "https://public.test/realms/ds",
            InternalIssuer = "http://keycloak:8080/realms/ds"
        };

        Assert.Equal(
            "https://public.test/realms/ds/protocol/openid-connect/auth",
            options.AuthorizationEndpoint);
        Assert.Equal(
            "http://keycloak:8080/realms/ds/protocol/openid-connect/token",
            options.TokenEndpoint);
        Assert.Equal(
            "http://keycloak:8080/realms/ds/protocol/openid-connect/logout",
            options.LogoutEndpoint);
        Assert.Equal(
            "http://keycloak:8080/realms/ds/.well-known/openid-configuration",
            options.DiscoveryEndpoint);
    }

    [Fact]
    public void Without_internal_issuer_all_endpoints_use_the_public_issuer()
    {
        var options = new OidcOptions { Issuer = "http://localhost:8080/realms/ds" };

        Assert.Equal("http://localhost:8080/realms/ds/protocol/openid-connect/token", options.TokenEndpoint);
        Assert.Equal(
            "http://localhost:8080/realms/ds/.well-known/openid-configuration",
            options.DiscoveryEndpoint);
    }
}

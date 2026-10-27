namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed class OidcOptions
{
    public string Issuer { get; set; } = "";
    // URL raggiungibile dall'API quando l'issuer pubblico non lo è (Keycloak dietro Caddy).
    public string InternalIssuer { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    // Solo per reti interne fidate: abilita la discovery su InternalIssuer in HTTP.
    public bool AllowInsecureInternalDiscovery { get; set; }

    public string AuthorizationEndpoint => TrimmedIssuer + "/protocol/openid-connect/auth";
    public string TokenEndpoint => InternalBase + "/protocol/openid-connect/token";
    public string LogoutEndpoint => InternalBase + "/protocol/openid-connect/logout";
    public string DiscoveryEndpoint => InternalBase + "/.well-known/openid-configuration";
    private string TrimmedIssuer => Issuer.TrimEnd('/');
    private string InternalBase => string.IsNullOrWhiteSpace(InternalIssuer)
        ? TrimmedIssuer
        : InternalIssuer.TrimEnd('/');
}

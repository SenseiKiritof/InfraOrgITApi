namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed class OidcOptions
{
    public string Issuer { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";

    public string AuthorizationEndpoint => TrimmedIssuer + "/protocol/openid-connect/auth";
    public string TokenEndpoint => TrimmedIssuer + "/protocol/openid-connect/token";
    public string LogoutEndpoint => TrimmedIssuer + "/protocol/openid-connect/logout";
    public string DiscoveryEndpoint => TrimmedIssuer + "/.well-known/openid-configuration";
    private string TrimmedIssuer => Issuer.TrimEnd('/');
}

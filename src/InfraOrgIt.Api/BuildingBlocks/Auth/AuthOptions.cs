namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed class AuthOptions
{
    public bool SecureCookies { get; set; } = true;
    public string BootstrapAdminUsername { get; set; } = "";
}

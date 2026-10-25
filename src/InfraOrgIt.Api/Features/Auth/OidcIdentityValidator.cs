using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Auth;

public sealed record OidcIdentity(string Username, string Name, string Surname);

public interface IOidcIdentityValidator
{
    Task<OidcIdentity> ValidateAsync(string idToken, CancellationToken ct);
}

public sealed class OidcIdentityValidator(
    IConfigurationManager<OpenIdConnectConfiguration> configuration,
    IOptions<OidcOptions> options,
    ILogger<OidcIdentityValidator> logger) : IOidcIdentityValidator
{
    public async Task<OidcIdentity> ValidateAsync(string idToken, CancellationToken ct)
    {
        var oidc = options.Value;
        var configurationData = await configuration.GetConfigurationAsync(ct);

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = oidc.Issuer.TrimEnd('/'),
            ValidateAudience = true,
            ValidAudience = oidc.ClientId,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configurationData.SigningKeys,
            NameClaimType = "preferred_username"
        };

        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(idToken, parameters, out _);
            var username = principal.FindFirst("preferred_username")?.Value
                           ?? throw new ApiException(StatusCodes.Status502BadGateway, "oidc_error",
                               "ID token senza preferred_username.");
            var name = principal.FindFirst("given_name")?.Value
                       ?? principal.FindFirst(ClaimTypes.GivenName)?.Value
                       ?? principal.FindFirst("name")?.Value
                       ?? username;
            var surname = principal.FindFirst("family_name")?.Value
                          ?? principal.FindFirst(ClaimTypes.Surname)?.Value
                          ?? "";
            return new OidcIdentity(username.ToLowerInvariant(), name, surname);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            logger.LogWarning(exception, "ID token non valido");
            throw new ApiException(StatusCodes.Status502BadGateway, "oidc_error",
                "ID token non valido.", exception);
        }
    }
}

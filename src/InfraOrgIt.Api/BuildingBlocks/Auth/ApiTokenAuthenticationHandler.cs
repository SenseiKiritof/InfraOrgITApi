using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

/// <summary>Bearer authentication with a personal InfraOrgIT API token.</summary>
public sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var raw = header["Bearer ".Length..].Trim();
        if (!raw.StartsWith(ApiTokenService.Prefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var identity = await tokens.ValidateAsync(raw, Context.RequestAborted);
        if (identity is null)
            return AuthenticateResult.Fail("Token non valido o scaduto.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
            new(ClaimTypes.Name, identity.Username),
            new("preferred_username", identity.Username),
            new("api_token_id", identity.TokenId.ToString())
        };
        claims.AddRange(identity.Scopes.Select(scope => new Claim("scope", scope)));

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName));
    }
}

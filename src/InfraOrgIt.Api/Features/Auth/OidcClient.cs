using System.Net.Http.Headers;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Auth;

public sealed record OidcTokenResult(
    string AccessToken,
    string? RefreshToken,
    string? IdToken,
    int ExpiresIn,
    int RefreshExpiresIn);

public sealed class OidcClient(HttpClient http, IOptions<OidcOptions> options, ILogger<OidcClient> logger)
{
    public string BuildAuthorizationUrl(string state, string codeChallenge)
    {
        var oidc = options.Value;
        var query = new Dictionary<string, string>
        {
            ["client_id"] = oidc.ClientId,
            ["redirect_uri"] = oidc.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid profile email",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var encoded = string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return $"{oidc.AuthorizationEndpoint}?{encoded}";
    }

    public async Task<OidcTokenResult> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
    {
        var oidc = options.Value;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = oidc.RedirectUri,
            ["client_id"] = oidc.ClientId,
            ["code_verifier"] = codeVerifier
        };
        AddSecret(form, oidc);
        return await PostTokenAsync(form, ct);
    }

    public async Task<OidcTokenResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var oidc = options.Value;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = oidc.ClientId
        };
        AddSecret(form, oidc);
        return await PostTokenAsync(form, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var oidc = options.Value;
        var form = new Dictionary<string, string>
        {
            ["client_id"] = oidc.ClientId,
            ["refresh_token"] = refreshToken
        };
        AddSecret(form, oidc);

        using var request = new HttpRequestMessage(HttpMethod.Post, oidc.LogoutEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            logger.LogWarning("Keycloak logout ha risposto {Status}", (int)response.StatusCode);
    }

    private async Task<OidcTokenResult> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new ApiException(StatusCodes.Status502BadGateway, "oidc_error",
                "Scambio token con Keycloak fallito.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;

        if (!root.TryGetProperty("access_token", out var accessToken) ||
            string.IsNullOrEmpty(accessToken.GetString()))
            throw new ApiException(StatusCodes.Status502BadGateway, "oidc_error",
                "Risposta Keycloak senza access token.");

        return new OidcTokenResult(
            accessToken.GetString()!,
            root.TryGetProperty("refresh_token", out var refreshToken) ? refreshToken.GetString() : null,
            root.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null,
            root.TryGetProperty("expires_in", out var expiresIn) ? expiresIn.GetInt32() : 300,
            root.TryGetProperty("refresh_expires_in", out var refreshExpiresIn) ? refreshExpiresIn.GetInt32() : 0);
    }

    private static void AddSecret(Dictionary<string, string> form, OidcOptions oidc)
    {
        if (!string.IsNullOrWhiteSpace(oidc.ClientSecret))
            form["client_secret"] = oidc.ClientSecret;
    }
}

using System.Net.Http.Headers;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Auth;

/// <summary>
/// Scambia il refresh token del device flow desktop con Keycloak usando il
/// client pubblico infraorgit-mcp-desktop: nessun client secret e nessun
/// riuso del client BFF confidenziale.
/// </summary>
public sealed class DesktopOidcClient(
    HttpClient http,
    IOptions<OidcOptions> options,
    ILogger<DesktopOidcClient> logger)
{
    public async Task<OidcTokenResult> RedeemRefreshTokenAsync(
        string refreshToken,
        CancellationToken ct)
    {
        var oidc = options.Value;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = oidc.DesktopClientId
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, oidc.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception exception) when (
            (exception is HttpRequestException or TaskCanceledException) &&
            !ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Keycloak non raggiungibile per il refresh token desktop");
            throw new ApiException(StatusCodes.Status502BadGateway, "oidc_error",
                "Keycloak non raggiungibile.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                logger.LogWarning("Scambio refresh token desktop rifiutato con stato {Status}", status);
                if (status >= 500)
                    throw new ApiException(StatusCodes.Status502BadGateway, "oidc_error",
                        "Scambio token con Keycloak fallito.");
                throw new ApiException(StatusCodes.Status401Unauthorized, "desktop_session_invalid",
                    "Refresh token non valido o scaduto.");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = document.RootElement;

                return new OidcTokenResult(
                    root.TryGetProperty("refresh_token", out var rotated) ? rotated.GetString() : null,
                    root.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null,
                    root.TryGetProperty("expires_in", out var expiresIn) ? expiresIn.GetInt32() : 300,
                    root.TryGetProperty("refresh_expires_in", out var refreshExpiresIn)
                        ? refreshExpiresIn.GetInt32()
                        : 0);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Risposta token desktop non valida");
                throw new ApiException(StatusCodes.Status502BadGateway, "oidc_error",
                    "Risposta token di Keycloak non valida.");
            }
        }
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var oidc = options.Value;
        var form = new Dictionary<string, string>
        {
            ["client_id"] = oidc.DesktopClientId,
            ["refresh_token"] = refreshToken
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, oidc.LogoutEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            logger.LogWarning("Keycloak logout desktop ha risposto {Status}", (int)response.StatusCode);
    }
}

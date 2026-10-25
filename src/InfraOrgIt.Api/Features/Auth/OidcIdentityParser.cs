using System.Text;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Errors;

namespace InfraOrgIt.Api.Features.Auth;

public sealed record OidcIdentity(string Username, string Name, string Surname);

public static class OidcIdentityParser
{
    public static OidcIdentity Parse(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2)
            throw new ApiException(StatusCodes.Status502BadGateway, "oidc_error", "ID token non valido.");

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            var root = document.RootElement;
            var username = GetString(root, "preferred_username")
                           ?? throw new ApiException(StatusCodes.Status502BadGateway, "oidc_error",
                               "ID token senza preferred_username.");
            var name = GetString(root, "given_name") ?? GetString(root, "name") ?? username;
            var surname = GetString(root, "family_name") ?? "";
            return new OidcIdentity(username.ToLowerInvariant(), name, surname);
        }
        catch (FormatException exception)
        {
            throw new ApiException(StatusCodes.Status502BadGateway, "oidc_error", "ID token non decodificabile.", exception);
        }
        catch (JsonException exception)
        {
            throw new ApiException(StatusCodes.Status502BadGateway, "oidc_error", "ID token non JSON.", exception);
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

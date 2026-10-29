using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

/// <summary>
/// The OIDC state and PKCE verifier travel in the login cookie, protected with
/// DataProtection: no server-side memory, so an API restart between
/// <c>/auth/login</c> and <c>/auth/callback</c> does not break the login.
/// </summary>
public sealed class LoginStateProtector(IDataProtectionProvider provider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private readonly IDataProtector _protector = provider.CreateProtector("InfraOrgIT.LoginState.v1");

    public string Protect(string state, string verifier) =>
        _protector.Protect(JsonSerializer.Serialize(new LoginStatePayload(
            state, verifier, DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds())));

    /// <summary>Returns the PKCE verifier when the payload is valid and the state matches.</summary>
    public string? Verifier(string value, string expectedState)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<LoginStatePayload>(_protector.Unprotect(value));
            if (payload is null ||
                payload.ExpiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
                !FixedTimeEquals(payload.State, expectedState))
                return null;

            return payload.CodeVerifier;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private sealed record LoginStatePayload(string State, string CodeVerifier, long ExpiresAt);
}

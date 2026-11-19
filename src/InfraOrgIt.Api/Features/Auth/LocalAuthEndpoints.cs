using System.Security.Cryptography;
using System.Text;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.Features.Users;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Auth;

public sealed class LocalOptions
{
    public bool Enabled { get; set; }
    public string BootstrapSecret { get; set; } = "";
    public string Username { get; set; } = "local";
    public string DisplayName { get; set; } = "Local user";
}

public static class LocalAuthEndpoints
{
    public const string BootstrapHeader = "X-InfraOrgIT-Local";

    public static void Map(WebApplication app)
    {
        app.MapPost("/auth/local/session", CreateSession)
            .AllowAnonymous()
            .RequireRateLimiting("auth-login");
    }

    private static async Task<IResult> CreateSession(
        HttpContext http,
        IOptions<LocalOptions> local,
        IOptions<AuthOptions> auth,
        UserProvisioningService users,
        ISessionStore sessions,
        CancellationToken ct)
    {
        var secret = local.Value.BootstrapSecret;
        var supplied = http.Request.Headers[BootstrapHeader].ToString();
        if (string.IsNullOrWhiteSpace(secret) || !FixedTimeEquals(secret, supplied))
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Bootstrap locale non valido",
                extensions: new Dictionary<string, object?> { ["code"] = "local_bootstrap_invalid" });

        var username = string.IsNullOrWhiteSpace(local.Value.Username)
            ? "local"
            : local.Value.Username.Trim();
        var roles = await users.EnsureUserAsync(
            new OidcIdentity(username, local.Value.DisplayName, ""), ct);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(365);
        var session = new SessionData(
            username,
            local.Value.DisplayName,
            "",
            null,
            expiresAt,
            null,
            CryptoRandom.Token(),
            roles,
            Desktop: true);
        var sessionId = sessions.Create(session);
        http.Response.Cookies.Append(
            SessionCookie.Session(auth.Value.SecureCookies),
            sessionId,
            SessionCookie.Options(auth.Value.SecureCookies, expiresAt));
        return Results.NoContent();
    }

    private static bool FixedTimeEquals(string expected, string supplied)
    {
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(supplied ?? "");
        return left.Length == right.Length &&
               CryptographicOperations.FixedTimeEquals(left, right);
    }
}

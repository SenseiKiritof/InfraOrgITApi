using System.Security.Cryptography;
using System.Text;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.Features.Auth;
using InfraOrgIt.Api.Features.Users;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Auth;

public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/auth/login", StartLogin).AllowAnonymous();
        app.MapGet("/auth/callback", Callback).AllowAnonymous();
        app.MapPost("/auth/logout", Logout).AllowAnonymous();
    }

    private static IResult StartLogin(
        HttpContext http,
        LoginStateStore states,
        ISessionStore sessions,
        OidcClient oidc,
        IOptions<AuthOptions> auth,
        IOptions<FrontendOptions> frontend)
    {
        var secure = auth.Value.SecureCookies;
        var sessionId = http.Request.Cookies[SessionCookie.Session(secure)];
        if (!string.IsNullOrEmpty(sessionId) && sessions.Get(sessionId) is not null)
            return Results.Redirect(frontend.Value.BaseUrl);

        var state = CryptoRandom.Token(16);
        var verifier = CryptoRandom.Token(32);
        var loginId = states.Create(state, verifier);
        var challenge = CryptoRandom.Sha256Base64Url(verifier);

        http.Response.Cookies.Append(
            SessionCookie.Login(secure),
            loginId,
            SessionCookie.Options(secure, DateTimeOffset.UtcNow.AddMinutes(10)));

        return Results.Redirect(oidc.BuildAuthorizationUrl(state, challenge));
    }

    private static async Task<IResult> Callback(
        HttpContext http,
        string? code,
        string? state,
        LoginStateStore states,
        OidcClient oidc,
        UserProvisioningService users,
        ISessionStore sessions,
        IOptions<AuthOptions> auth,
        IOptions<FrontendOptions> frontend,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var secure = auth.Value.SecureCookies;
        var loginCookie = SessionCookie.Login(secure);

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) ||
            !http.Request.Cookies.TryGetValue(loginCookie, out var loginId))
            return LoginError(http, frontend.Value.BaseUrl, "callback_non_valido");

        var login = states.Consume(loginId);
        if (login is null || !FixedTimeEquals(login.State, state))
            return LoginError(http, frontend.Value.BaseUrl, "state_non_valido");

        try
        {
            var token = await oidc.ExchangeCodeAsync(code, login.CodeVerifier, ct);
            if (string.IsNullOrWhiteSpace(token.IdToken))
                throw new InvalidOperationException("Keycloak non ha restituito id_token.");

            var identity = OidcIdentityParser.Parse(token.IdToken);
            await users.EnsureUserAsync(identity, ct);

            var now = DateTimeOffset.UtcNow;
            var refreshExpiresAt = token.RefreshExpiresIn > 0
                ? now.AddSeconds(token.RefreshExpiresIn)
                : (DateTimeOffset?)null;

            var session = new SessionData(
                identity.Username,
                identity.Name,
                identity.Surname,
                token.AccessToken,
                token.RefreshToken,
                now.AddSeconds(token.ExpiresIn),
                refreshExpiresAt,
                CryptoRandom.Token());

            var sessionId = sessions.Create(session);
            http.Response.Cookies.Append(
                SessionCookie.Session(secure),
                sessionId,
                SessionCookie.Options(secure, refreshExpiresAt ?? session.AccessExpiresAt));
            SessionCookie.Delete(http.Response, secure, loginCookie);

            return Results.Redirect(frontend.Value.BaseUrl);
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("AuthEndpoints").LogWarning(exception, "Login fallito");
            SessionCookie.Delete(http.Response, secure, loginCookie);
            return LoginError(http, frontend.Value.BaseUrl, "login_fallito");
        }
    }

    private static async Task<IResult> Logout(
        HttpContext http,
        ISessionStore sessions,
        OidcClient oidc,
        IOptions<AuthOptions> auth,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var secure = auth.Value.SecureCookies;
        var session = http.Items[SessionAuthenticationHandler.SessionItemKey] as SessionData;

        if (session is not null)
        {
            if (!string.IsNullOrWhiteSpace(session.RefreshToken))
            {
                try
                {
                    await oidc.LogoutAsync(session.RefreshToken, ct);
                }
                catch (Exception exception)
                {
                    loggerFactory.CreateLogger("AuthEndpoints").LogWarning(exception, "Logout Keycloak fallito");
                }
            }

            var sessionId = http.Request.Cookies[SessionCookie.Session(secure)];
            if (!string.IsNullOrEmpty(sessionId))
                sessions.Remove(sessionId);
        }

        SessionCookie.Delete(http.Response, secure, SessionCookie.Session(secure));
        return Results.NoContent();
    }

    private static IResult LoginError(HttpContext http, string frontendBaseUrl, string code) =>
        Results.Redirect($"{frontendBaseUrl.TrimEnd('/')}/login-error?code={Uri.EscapeDataString(code)}");

    private static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}

using System.Security.Cryptography;
using System.Text;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.Features.Auth;
using InfraOrgIt.Api.Features.Users;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Auth;

public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/auth/login", StartLogin).AllowAnonymous().RequireRateLimiting("auth-login");
        app.MapGet("/auth/callback", Callback).AllowAnonymous().RequireRateLimiting("auth-callback");
        app.MapPost("/auth/logout", Logout).AllowAnonymous();
    }

    private static IResult StartLogin(
        HttpContext http,
        LoginStateProtector loginStates,
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
        var loginId = loginStates.Protect(state, verifier);
        var challenge = CryptoRandom.Sha256Base64Url(verifier);

        http.Response.Cookies.Append(
            SessionCookie.Login(secure, state),
            loginId,
            SessionCookie.Options(secure, DateTimeOffset.UtcNow.AddMinutes(30)));

        return Results.Redirect(oidc.BuildAuthorizationUrl(state, challenge));
    }

    private static async Task<IResult> Callback(
        HttpContext http,
        string? code,
        string? state,
        LoginStateProtector loginStates,
        OidcClient oidc,
        IOidcIdentityValidator identityValidator,
        UserProvisioningService users,
        ISessionStore sessions,
        IOptions<AuthOptions> auth,
        IOptions<FrontendOptions> frontend,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var secure = auth.Value.SecureCookies;

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return LoginFailure(http, sessions, secure, frontend.Value.BaseUrl, loggerFactory, "callback_non_valido");

        var loginCookie = SessionCookie.Login(secure, state);
        if (!http.Request.Cookies.TryGetValue(loginCookie, out var loginValue))
            return LoginFailure(http, sessions, secure, frontend.Value.BaseUrl, loggerFactory, "login_cookie_assente");

        var verifier = loginStates.Verifier(loginValue, state);
        if (verifier is null)
            return LoginFailure(http, sessions, secure, frontend.Value.BaseUrl, loggerFactory, "state_non_valido");

        try
        {
            var token = await oidc.ExchangeCodeAsync(code, verifier, ct);
            if (string.IsNullOrWhiteSpace(token.IdToken))
                throw new InvalidOperationException("Keycloak non ha restituito id_token.");

            var identity = await identityValidator.ValidateAsync(token.IdToken, ct);
            var roles = await users.EnsureUserAsync(identity, ct);

            var now = DateTimeOffset.UtcNow;
            var refreshExpiresAt = token.RefreshExpiresIn > 0
                ? now.AddSeconds(token.RefreshExpiresIn)
                : (DateTimeOffset?)null;

            var session = new SessionData(
                identity.Username,
                identity.Name,
                identity.Surname,
                token.RefreshToken,
                now.AddSeconds(token.ExpiresIn),
                refreshExpiresAt,
                CryptoRandom.Token(),
                roles);

            var sessionId = sessions.Create(session);
            http.Response.Cookies.Append(
                SessionCookie.Session(secure),
                sessionId,
                SessionCookie.Options(secure, refreshExpiresAt ?? session.AccessExpiresAt));
            SessionCookie.Delete(http.Response, secure, loginCookie);
            SessionCookie.Delete(http.Response, secure, SessionCookie.Relogin(secure));

            return Results.Redirect(frontend.Value.BaseUrl);
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("AuthEndpoints").LogWarning(exception, "Login fallito");
            SessionCookie.Delete(http.Response, secure, loginCookie);
            return LoginFailure(
                http, sessions, secure, frontend.Value.BaseUrl, loggerFactory, "login_fallito");
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

    /// <summary>
    /// A second callback for the same login (back button, refresh, parallel tab)
    /// no longer fails when the first one already created the session.
    /// </summary>
    private static IResult LoginFailure(
        HttpContext http,
        ISessionStore sessions,
        bool secure,
        string frontendBaseUrl,
        ILoggerFactory loggerFactory,
        string reason)
    {
        loggerFactory.CreateLogger("AuthEndpoints").LogWarning("Login non completato: {Reason}", reason);

        var sessionId = http.Request.Cookies[SessionCookie.Session(secure)];
        if (!string.IsNullOrEmpty(sessionId) && sessions.Get(sessionId) is not null)
            return Results.Redirect(frontendBaseUrl);

        // A stale callback URL (old tab, expired login page) or a lost cookie:
        // retry the login once before showing the error page.
        if (reason is "login_cookie_assente" or "state_non_valido")
        {
            var retryCookie = SessionCookie.Relogin(secure);
            if (!http.Request.Cookies.ContainsKey(retryCookie))
            {
                http.Response.Cookies.Append(
                    retryCookie, "1",
                    SessionCookie.Options(secure, DateTimeOffset.UtcNow.AddMinutes(2)));
                return Results.Redirect("/auth/login");
            }

            SessionCookie.Delete(http.Response, secure, retryCookie);
        }

        return LoginError(http, frontendBaseUrl, reason);
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

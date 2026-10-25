using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.Features.Auth;
using InfraOrgIt.Api.Features.Users;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Session;

public static class SessionEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/session", GetSession).RequireAuthorization();
    }

    private static async Task<IResult> GetSession(
        HttpContext http,
        ISessionStore sessions,
        OidcClient oidc,
        IOidcIdentityValidator identityValidator,
        UserProvisioningService users,
        IOptions<AuthOptions> auth,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var secure = auth.Value.SecureCookies;
        var sessionId = http.Request.Cookies[SessionCookie.Session(secure)];
        var session = http.Items[SessionAuthenticationHandler.SessionItemKey] as SessionData;

        if (session is null || string.IsNullOrEmpty(sessionId))
            return Unauthorized();

        if (session.AccessExpiresAt <= DateTimeOffset.UtcNow)
        {
            if (string.IsNullOrWhiteSpace(session.RefreshToken))
            {
                sessions.Remove(sessionId);
                SessionCookie.Delete(http.Response, secure, SessionCookie.Session(secure));
                return Unauthorized();
            }

            try
            {
                var token = await oidc.RefreshAsync(session.RefreshToken, ct);
                if (string.IsNullOrWhiteSpace(token.IdToken))
                    throw new InvalidOperationException("Keycloak non ha restituito id_token.");

                var identity = await identityValidator.ValidateAsync(token.IdToken, ct);
                var roles = await users.EnsureUserAsync(identity, ct);

                var now = DateTimeOffset.UtcNow;
                var refreshExpiresAt = token.RefreshExpiresIn > 0
                    ? now.AddSeconds(token.RefreshExpiresIn)
                    : session.RefreshExpiresAt;

                session = session with
                {
                    Name = identity.Name,
                    Surname = identity.Surname,
                    RefreshToken = token.RefreshToken ?? session.RefreshToken,
                    AccessExpiresAt = now.AddSeconds(token.ExpiresIn),
                    RefreshExpiresAt = refreshExpiresAt,
                    Roles = roles
                };

                sessions.Update(sessionId, session);
                http.Response.Cookies.Append(
                    SessionCookie.Session(secure),
                    sessionId,
                    SessionCookie.Options(secure, refreshExpiresAt ?? session.AccessExpiresAt));
            }
            catch (Exception exception)
            {
                loggerFactory.CreateLogger("SessionEndpoints").LogWarning(exception, "Refresh sessione fallito per {Username}", session.Username);
                sessions.Remove(sessionId);
                SessionCookie.Delete(http.Response, secure, SessionCookie.Session(secure));
                return Unauthorized();
            }
        }

        var expiresIn = Math.Max(0, (int)Math.Ceiling((session.AccessExpiresAt - DateTimeOffset.UtcNow).TotalSeconds));
        var refreshExpiresIn = session.RefreshExpiresAt is null
            ? 0
            : Math.Max(0, (int)Math.Ceiling((session.RefreshExpiresAt.Value - DateTimeOffset.UtcNow).TotalSeconds));

        return Results.Ok(new
        {
            user = new { session.Username, session.Name, session.Surname },
            expiresIn,
            refreshExpiresIn,
            csrfToken = session.CsrfToken
        });
    }

    private static IResult Unauthorized() =>
        Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Sessione assente o scaduta",
            extensions: new Dictionary<string, object?> { ["code"] = "unauthorized" });
}

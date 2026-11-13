using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Features.Auth;
using InfraOrgIt.Api.Features.Users;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Session;

public static class SessionEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/session", GetSession).RequireAuthorization();
        app.MapPatch("/api/session/theme", UpdateTheme)
            .RequireAuthorization("Session")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private sealed record ThemeUpdateRequest(string? Theme);

    private static async Task<IResult> GetSession(
        HttpContext http,
        ISessionStore sessions,
        OidcClient oidc,
        DesktopOidcClient desktopOidc,
        IOidcIdentityValidator identityValidator,
        UserProvisioningService users,
        IOptions<AuthOptions> auth,
        IOptions<OidcOptions> oidcOptions,
        InfraOrgItDbContext db,
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
                var token = session.Desktop
                    ? await desktopOidc.RedeemRefreshTokenAsync(session.RefreshToken, ct)
                    : await oidc.RefreshAsync(session.RefreshToken, ct);
                if (string.IsNullOrWhiteSpace(token.IdToken))
                    throw new InvalidOperationException("Keycloak non ha restituito id_token.");

                var identity = session.Desktop
                    ? await identityValidator.ValidateAsync(
                        token.IdToken, oidcOptions.Value.DesktopClientId, ct)
                    : await identityValidator.ValidateAsync(token.IdToken, ct);
                var roles = await users.EnsureUserAsync(identity, ct);

                var now = DateTimeOffset.UtcNow;
                var refreshExpiresAt = token.RefreshExpiresIn > 0
                    ? now.AddSeconds(token.RefreshExpiresIn)
                    : session.RefreshExpiresAt;
                // La sessione desktop resta valida fino al limite configurato,
                // anche se Keycloak riporta un refresh token più breve.
                if (session.Desktop)
                    refreshExpiresAt = session.RefreshExpiresAt ?? refreshExpiresAt;

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

        var theme = await db.Users.AsNoTracking()
            .Where(x => x.Username == session.Username)
            .Select(x => x.Theme)
            .SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(theme))
            theme = "system";

        return Results.Ok(new
        {
            user = new { session.Username, session.Name, session.Surname },
            theme,
            expiresIn,
            refreshExpiresIn,
            csrfToken = session.CsrfToken
        });
    }

    private static async Task<IResult> UpdateTheme(
        HttpContext http,
        ThemeUpdateRequest? request,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var session = http.Items[SessionAuthenticationHandler.SessionItemKey] as SessionData;
        if (session is null)
            return Unauthorized();

        var theme = request?.Theme?.Trim().ToLowerInvariant();
        if (theme is not ("light" or "dark" or "system"))
            throw Guard.Unprocessable("Il campo theme ammette: light, dark, system.");

        var user = await db.Users.SingleOrDefaultAsync(x => x.Username == session.Username, ct)
                   ?? throw new ApiException(StatusCodes.Status404NotFound, "not_found",
                       "Utente non trovato.");
        user.Theme = theme;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static IResult Unauthorized() =>
        Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Sessione assente o scaduta",
            extensions: new Dictionary<string, object?> { ["code"] = "unauthorized" });
}

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISessionStore sessions,
    IOptions<AuthOptions> authOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";
    public const string SessionItemKey = "session";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookieName = SessionCookie.Session(authOptions.Value.SecureCookies);
        if (!Request.Cookies.TryGetValue(cookieName, out var sessionId) || string.IsNullOrEmpty(sessionId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var session = sessions.Get(sessionId);
        if (session is null)
            return Task.FromResult(AuthenticateResult.NoResult());

        Context.Items[SessionItemKey] = session;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.Username),
            new(ClaimTypes.Name, session.Username),
            new("preferred_username", session.Username),
            new("display_name", session.Name)
        };
        claims.AddRange(session.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Sessione assente o scaduta",
                extensions: new Dictionary<string, object?> { ["code"] = "unauthorized" })
            .ExecuteAsync(Context);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Accesso negato",
                extensions: new Dictionary<string, object?> { ["code"] = "forbidden" })
            .ExecuteAsync(Context);
    }
}

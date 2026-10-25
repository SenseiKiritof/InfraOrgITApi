using System.Security.Claims;
using System.Text.Encodings.Web;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISessionStore sessions,
    InfraOrgItDbContext db,
    IOptions<AuthOptions> authOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";
    public const string SessionItemKey = "session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookieName = SessionCookie.Session(authOptions.Value.SecureCookies);
        if (!Request.Cookies.TryGetValue(cookieName, out var sessionId) || string.IsNullOrEmpty(sessionId))
            return AuthenticateResult.NoResult();

        var session = sessions.Get(sessionId);
        if (session is null)
            return AuthenticateResult.NoResult();

        var user = await db.Users
            .AsNoTracking()
            .Include(x => x.Roles)
            .SingleOrDefaultAsync(x => x.Username == session.Username, Context.RequestAborted);

        if (user is null)
        {
            sessions.Remove(sessionId);
            return AuthenticateResult.NoResult();
        }

        Context.Items[SessionItemKey] = session;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.Username),
            new(ClaimTypes.Name, session.Username),
            new("preferred_username", session.Username),
            new("display_name", session.Name)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role.Name)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

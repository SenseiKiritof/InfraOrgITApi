namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public static class SessionCookie
{
    public const string SessionName = "infraorgit_session";
    public const string SecureSessionName = "__Host-infraorgit_session";
    public const string LoginName = "infraorgit_login";
    public const string SecureLoginName = "__Host-infraorgit_login";
    public const string ReloginName = "infraorgit_relogin";
    public const string SecureReloginName = "__Host-infraorgit_relogin";

    public static string Session(bool secure) => secure ? SecureSessionName : SessionName;
    public static string Relogin(bool secure) => secure ? SecureReloginName : ReloginName;
    public static string Login(bool secure, string state) =>
        (secure ? SecureLoginName : LoginName) + "_" + state;

    public static CookieOptions Options(bool secure, DateTimeOffset? expires = null) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
        MaxAge = expires is null ? null : expires.Value - DateTimeOffset.UtcNow
    };

    public static void Delete(HttpResponse response, bool secure, string name) =>
        response.Cookies.Delete(name, Options(secure));
}

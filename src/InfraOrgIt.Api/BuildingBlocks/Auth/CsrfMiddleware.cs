using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed class CsrfMiddleware(
    RequestDelegate next,
    IOptions<FrontendOptions> frontendOptions,
    ILogger<CsrfMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (IsMutating(context.Request.Method) &&
            (context.Request.Path.StartsWithSegments("/api") || context.Request.Path == "/auth/logout"))
        {
            var session = context.Items[SessionAuthenticationHandler.SessionItemKey] as SessionData;
            if (session is not null)
            {
                var presented = context.Request.Headers["X-CSRF-Token"].ToString();
                if (!FixedTimeEquals(presented, session.CsrfToken))
                {
                    await WriteProblem(context, "csrf_failed", "CSRF token mancante o non valido");
                    return;
                }

                var origin = context.Request.Headers.Origin.ToString().TrimEnd('/');
                if (!string.IsNullOrEmpty(origin) &&
                    !frontendOptions.Value.EffectiveOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Mutazione rifiutata per Origin non ammessa: {Origin}", origin);
                    await WriteProblem(context, "origin_failed", "Origin non ammessa");
                    return;
                }
            }
        }

        await next(context);
    }

    private static bool IsMutating(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static bool FixedTimeEquals(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return false;

        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static async Task WriteProblem(HttpContext context, string code, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Richiesta non autorizzata",
                detail: detail,
                extensions: new Dictionary<string, object?> { ["code"] = code })
            .ExecuteAsync(context);
    }
}

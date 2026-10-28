using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Catalog;

/// <summary>
/// Test call on a stored application API: the API key never leaves the server and
/// the browser never talks to the target host directly.
/// The call can reach any http/https host, including internal ones, on purpose:
/// this tool manages internal infrastructure. Only SystemAdmin can execute it.
/// </summary>
public static class ApplicationApiCallEndpoints
{
    private const int MaxResponseCharacters = 20_000;

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/application-version-apis/{id:int}/call", CallAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> CallAsync(
        int id,
        ApplicationApiCallInput input,
        ClaimsPrincipal user,
        CredentialCipher cipher,
        IHttpClientFactory httpClientFactory,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);

        var api = await db.ApplicationVersionApis.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Url,
                x.RequestTypeEnum,
                x.Body,
                ApplicationId = x.ApplicationVersion!.ApplicationId,
                Hostname = x.ApplicationVersion!.Application!.Hostname
            })
            .SingleOrDefaultAsync(ct)
            ?? throw Guard.NotFound("API", id);

        var keys = await ApplicationApiKeysEndpoints.LoadAsync(db, api.ApplicationId, ct);

        var url = ApiCallUrl.Build(api.Hostname, api.Url, input.Query);
        foreach (var key in keys.Where(x => x.Location == "Query"))
            url = ApiCallUrl.AddQuery(url, key.Name, cipher.Decrypt(key.EncryptedValue, key.KeyVersion));

        using var request = new HttpRequestMessage(new HttpMethod(api.RequestTypeEnum), url);
        foreach (var key in keys.Where(x => x.Location == "Header"))
            request.Headers.TryAddWithoutValidation(
                key.Name, cipher.Decrypt(key.EncryptedValue, key.KeyVersion));

        var sendsBody = api.RequestTypeEnum is "POST" or "PUT" or "PATCH";
        var body = input.Body ?? (sendsBody ? api.Body : null);
        if (sendsBody && !string.IsNullOrEmpty(body))
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var client = httpClientFactory.CreateClient("application-api");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var (text, truncated) = await ReadLimitedAsync(response, ct);
            return Results.Ok(new ApplicationApiCallResult(
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString() ?? "",
                response.ReasonPhrase ?? "",
                (long)stopwatch.ElapsedMilliseconds,
                text,
                truncated));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new ApiException(
                StatusCodes.Status502BadGateway,
                "api_unreachable",
                $"Chiamata a {url.Authority} non riuscita: {exception.Message}",
                exception);
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadLimitedAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new char[MaxResponseCharacters + 1];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(0, buffer.Length), ct);
        return (new string(buffer, 0, Math.Min(read, MaxResponseCharacters)),
            read > MaxResponseCharacters);
    }
}

/// <summary>
/// Builds the target URL: the application hostname is the origin, the stored API
/// URL contributes the path (and any query it already carries).
/// </summary>
public static class ApiCallUrl
{
    public static Uri Build(
        string? hostname,
        string url,
        IReadOnlyDictionary<string, string>? query)
    {
        var stored = (url ?? "").Trim();
        string path;
        string? storedOrigin = null;

        if (Uri.TryCreate(stored, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            path = absolute.PathAndQuery;
            storedOrigin = absolute.GetLeftPart(UriPartial.Authority);
        }
        else
        {
            path = stored.StartsWith('/') ? stored : $"/{stored}";
        }

        var baseText = string.IsNullOrWhiteSpace(hostname) ? storedOrigin : hostname.Trim();
        if (string.IsNullOrWhiteSpace(baseText) ||
            !Uri.TryCreate(baseText, UriKind.Absolute, out var origin) ||
            (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
            throw Guard.Unprocessable(
                "Configura l'hostname dell'applicazione (http/https) prima di chiamare l'API.");

        var question = path.IndexOf('?');
        var pathOnly = question >= 0 ? path[..question] : path;
        var existingQuery = question >= 0 ? path[(question + 1)..] : "";

        var builder = new UriBuilder(origin)
        {
            Path = origin.AbsolutePath.TrimEnd('/') + (pathOnly.StartsWith('/') ? pathOnly : $"/{pathOnly}"),
            Query = existingQuery
        };

        foreach (var (name, value) in query ?? Empty)
        {
            if (value is null || name.Length == 0)
                continue;
            builder.Query = AppendQuery(builder.Query, name, value);
        }

        return builder.Uri;
    }

    public static Uri AddQuery(Uri uri, string name, string value)
    {
        var builder = new UriBuilder(uri) { Query = AppendQuery(uri.Query.TrimStart('?'), name, value) };
        return builder.Uri;
    }

    private static string AppendQuery(string current, string name, string value)
    {
        var pair = $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
        return string.IsNullOrEmpty(current) ? pair : $"{current}&{pair}";
    }

    private static readonly Dictionary<string, string> Empty = [];
}

public sealed record ApplicationApiCallInput(
    Dictionary<string, string>? Query = null,
    string? Body = null);

public sealed record ApplicationApiCallResult(
    int Status,
    string ContentType,
    string Reason,
    long DurationMs,
    string Body,
    bool Truncated);

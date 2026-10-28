using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
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
    /// <summary>Cap of the body returned to the browser (copy/download included).</summary>
    private const int MaxResponseBytes = 10 * 1024 * 1024;
    /// <summary>Cap of the body kept in the database for the history.</summary>
    private const int MaxStoredChars = 1_048_576;
    private const int MaxStoredUrlLength = 1000;
    private const int RetentionDays = 30;

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
            })
            .SingleOrDefaultAsync(ct)
            ?? throw Guard.NotFound("API", id);

        var environment = await db.ApplicationEnvironments.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ApplicationId == api.ApplicationId &&
                     x.EnvironmentDefinitionId == input.EnvironmentId, ct);
        if (environment is null || string.IsNullOrWhiteSpace(environment.Hostname))
            throw Guard.Unprocessable(
                "Configura l'hostname per l'ambiente selezionato prima di chiamare l'API.");

        var keys = await ApplicationApiKeysEndpoints.LoadAsync(
            db, api.ApplicationId, input.EnvironmentId, ct);

        var url = ApiCallUrl.Build(environment.Hostname, api.Url, input.Query);
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

        var client = httpClientFactory.CreateClient(
            environment.IgnoreSslErrors ? "application-api-insecure" : "application-api");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var (text, truncated, sizeBytes, binary) = await ReadLimitedAsync(response, ct);
            var status = (int)response.StatusCode;
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
            var reason = response.ReasonPhrase ?? "";
            var durationMs = stopwatch.ElapsedMilliseconds;

            int? callId = null;
            if (input.Save)
            {
                callId = await SaveAsync(
                    db, user, api.RequestTypeEnum, id, input, body, url, status, contentType,
                    reason, durationMs, text, sizeBytes, truncated, binary);
            }

            return Results.Ok(new ApplicationApiCallResult(
                callId,
                status,
                contentType,
                reason,
                durationMs,
                text,
                truncated,
                sizeBytes,
                binary));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            var durationMs = stopwatch.ElapsedMilliseconds;
            if (input.Save)
            {
                await SaveAsync(
                    db, user, api.RequestTypeEnum, id, input, body, url, null, null,
                    $"Chiamata non riuscita: {exception.Message}", durationMs,
                    null, 0, false, false);
            }

            throw new ApiException(
                StatusCodes.Status502BadGateway,
                "api_unreachable",
                $"Chiamata a {url.Authority} non riuscita: {exception.Message}",
                exception);
        }
    }

    private static async Task<int> SaveAsync(
        InfraOrgItDbContext db,
        ClaimsPrincipal user,
        string method,
        int apiId,
        ApplicationApiCallInput input,
        string? requestBody,
        Uri url,
        int? status,
        string? contentType,
        string? reason,
        long durationMs,
        string? body,
        long sizeBytes,
        bool truncated,
        bool binary)
    {
        var username = user.Identity?.Name ?? "";
        var storedBody = body is { Length: > MaxStoredChars } ? body[..MaxStoredChars] : body;
        var target = url.ToString();

        var call = new ApplicationApiCall
        {
            Username = username,
            ApplicationVersionApiId = apiId,
            EnvironmentDefinitionId = input.EnvironmentId,
            Method = method,
            // ponytail: the column is varchar(1000); longer URLs are cut to fit it.
            Url = target.Length > MaxStoredUrlLength ? target[..MaxStoredUrlLength] : target,
            QueryJson = input.Query is { Count: > 0 } ? JsonSerializer.Serialize(input.Query) : null,
            RequestBody = requestBody,
            Status = status,
            ContentType = contentType,
            Reason = reason is { Length: > 200 } ? reason[..200] : reason,
            DurationMs = durationMs,
            ResponseBody = storedBody,
            ResponseSizeBytes = sizeBytes,
            Truncated = truncated || (body?.Length ?? 0) > MaxStoredChars,
            Binary = binary
        };
        db.ApplicationApiCalls.Add(call);

        // Retention: a saved call is kept for 30 days per user.
        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        await db.ApplicationApiCalls
            .Where(x => x.Username == username && x.CreatedAt < cutoff)
            .ExecuteDeleteAsync(CancellationToken.None);

        await db.SaveChangesAsync(CancellationToken.None);
        return call.Id;
    }

    private static async Task<(string Text, bool Truncated, long SizeBytes, bool Binary)> ReadLimitedAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var contentLength = response.Content.Headers.ContentLength;
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[MaxResponseBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0)
                break;
            total += read;
        }

        var truncated = total > MaxResponseBytes;
        var sizeBytes = contentLength ?? total;
        if (contentLength is null && truncated)
        {
            // No Content-Length: read the rest without keeping it, to report the real size.
            var scratch = new byte[8192];
            long count = total;
            int read;
            while ((read = await stream.ReadAsync(scratch, ct)) > 0)
                count += read;
            sizeBytes = count;
        }

        var slice = buffer.AsSpan(0, Math.Min(total, MaxResponseBytes));
        var binary = !IsText(contentType);
        var text = binary ? Convert.ToBase64String(slice) : Encoding.UTF8.GetString(slice);
        return (text, truncated, sizeBytes, binary);
    }

    private static bool IsText(string mediaType) =>
        mediaType.Length == 0 ||
        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Contains("yaml", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Builds the target URL: the environment hostname is the origin, the stored API
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

        if (Uri.TryCreate(stored, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            path = absolute.PathAndQuery;
        }
        else
        {
            path = stored.StartsWith('/') ? stored : $"/{stored}";
        }

        if (string.IsNullOrWhiteSpace(hostname) ||
            !Uri.TryCreate(hostname.Trim(), UriKind.Absolute, out var origin) ||
            (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
            throw Guard.Unprocessable(
                "Configura l'hostname dell'applicazione per questo ambiente (http/https) prima di chiamare l'API.");

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
    int EnvironmentId,
    Dictionary<string, string>? Query = null,
    string? Body = null,
    bool Save = true);

public sealed record ApplicationApiCallResult(
    int? CallId,
    int Status,
    string ContentType,
    string Reason,
    long DurationMs,
    string Body,
    bool Truncated,
    long SizeBytes,
    bool Binary);

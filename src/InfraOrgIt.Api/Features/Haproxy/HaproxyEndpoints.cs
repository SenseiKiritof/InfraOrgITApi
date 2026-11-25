using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Haproxy;

public static class HaproxyEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/haproxy").RequireAuthorization();

        group.MapGet("/instances", ListAsync);
        group.MapPut("/instances/{id:int}", UpdateAsync)
            .RequireRateLimiting("remote-run");
        group.MapPut("/instances/{id:int}/secrets", UpdateSecretsAsync)
            .RequireRateLimiting("remote-run");
        group.MapPut("/instances/{id:int}/monitoring", SetMonitoringAsync)
            .RequireRateLimiting("remote-run");
        group.MapPost("/instances/{id:int}/test", TestAsync)
            .RequireRateLimiting("remote-run");
        group.MapPost("/instances/{id:int}/refresh", RefreshAsync)
            .RequireRateLimiting("remote-run");
        group.MapGet("/instances/{id:int}/routes", ListRoutesAsync);
        group.MapGet("/instances/{id:int}/servers", ListServersAsync);
        group.MapGet("/instances/{id:int}/live", LiveAsync);
        group.MapGet("/instances/{id:int}/metrics", MetricsAsync);
        group.MapPost("/discovery/run", RunDiscoveryAsync)
            .RequireRateLimiting("remote-run");
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var serverId = PageRequest.IntQuery(http, "serverId");
        var accessInfo = await access.GetAsync(user, ct);
        if (accessInfo.AdminEnvironmentIds.Count == 0)
            return Results.Ok(Array.Empty<HaproxyInstanceDto>());

        var adminIds = accessInfo.AdminEnvironmentIds.ToList();
        var query = db.HaproxyInstances.AsNoTracking()
            .Where(x => adminIds.Contains(x.Server!.ProviderEnvironmentId));
        if (serverId is not null)
            query = query.Where(x => x.ServerId == serverId.Value);

        var items = await query
            .OrderBy(x => x.Server!.Name)
            .ThenBy(x => x.Id)
            .Select(x => new HaproxyInstanceDto(
                x.Id,
                x.ServerId,
                x.Server!.Name,
                x.Server.ProviderEnvironmentId,
                x.Server.ProviderEnvironment!.Provider!.Name + " / " +
                x.Server.ProviderEnvironment.EnvironmentDefinition!.Name,
                x.ServerApplicationId,
                x.Name,
                x.BaseUrl,
                x.Transport,
                x.ApiHost,
                x.ApiPort,
                x.ApiVersion,
                x.EffectiveApiVersion,
                x.MonitoringChoice,
                x.DiscoveryStatus,
                x.DetectedVersion,
                x.DetectedConfigPath,
                x.LastSeenAt,
                x.LastTestAt,
                x.LastTestStatus,
                x.LastTestDetail,
                x.LastRefreshAt,
                x.LastRefreshStatus,
                x.LastRefreshDetail,
                x.Routes.Count,
                x.Servers.Count,
                x.Username,
                x.StatsUsername,
                x.CaPem,
                x.Username != null && x.EncryptedPassword != null,
                x.AllowSelfSigned))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        HaproxySettingsInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        IAuditService audit,
        HaproxyClientCache clientCache,
        CancellationToken ct)
    {
        var instance = await db.HaproxyInstances
            .Include(x => x.Server)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);
        await access.RequireAdminAsync(
            user, [instance.Server!.ProviderEnvironmentId], ct);

        var name = Guard.Text(input.Name, "Name", 64);
        var baseUrl = Guard.Text(input.BaseUrl, "BaseUrl", 300).TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw Guard.Unprocessable("BaseUrl deve essere un URL http o https.");
        var transport = input.Transport?.Trim() ?? "SshForward";
        if (transport is not ("Direct" or "SshForward" or "DesktopRelay"))
            throw Guard.Unprocessable("Transport non valido.");
        if (transport == "Direct" && uri.Scheme != "https")
            throw Guard.Unprocessable("Direct richiede un BaseUrl https.");
        if (transport == "SshForward" && uri.Scheme == "https" &&
            string.IsNullOrWhiteSpace(instance.CaPem) &&
            !instance.AllowSelfSigned &&
            !(input.AllowSelfSigned ?? false))
            throw Guard.Unprocessable(
                "Per https serve una CA oppure Accetta self-signed.");

        var apiVersion = input.ApiVersion?.Trim().ToLowerInvariant() ?? "auto";
        if (apiVersion is not ("auto" or "v2" or "v3" or "stats"))
            throw Guard.Unprocessable("ApiVersion ammessa: auto, v2, v3, stats.");
        var apiPort = input.ApiPort ?? instance.ApiPort;
        if (apiPort is < 1 or > 65535)
            throw Guard.Unprocessable("ApiPort non valida.");
        var caPem = Guard.OptionalText(input.CaPem, "CaPem", 200_000);
        if (!string.IsNullOrWhiteSpace(caPem))
        {
            try
            {
                var certificates = new X509Certificate2Collection();
                certificates.ImportFromPem(caPem);
                if (certificates.Count == 0)
                    throw new FormatException();
            }
            catch (Exception exception) when (exception is CryptographicException
                                                  or FormatException
                                                  or ArgumentException)
            {
                throw Guard.Unprocessable(
                    "CaPem non contiene certificati PEM validi.");
            }
        }

        if (input.ServerApplicationId is not null)
        {
            var belongs = await db.ServerApplications.AnyAsync(
                x => x.Id == input.ServerApplicationId.Value &&
                     x.ServerId == instance.ServerId, ct);
            if (!belongs)
                throw Guard.Unprocessable(
                    "Il deployment HAProxy non appartiene a questo server.");
        }

        instance.Name = name;
        instance.BaseUrl = baseUrl;
        instance.Transport = transport;
        instance.ApiHost = Guard.Text(input.ApiHost, "ApiHost", 200).Trim();
        instance.ApiPort = apiPort;
        instance.ApiVersion = apiVersion;
        instance.Username = Guard.OptionalText(input.Username, "Username", 100);
        instance.StatsUsername = Guard.OptionalText(
            input.StatsUsername, "StatsUsername", 100);
        instance.CaPem = caPem;
        instance.AllowSelfSigned = input.AllowSelfSigned ?? false;
        instance.ServerApplicationId = input.ServerApplicationId;
        if (instance.EncryptedPassword is null)
            instance.DiscoveryStatus = "CredentialsRequired";
        await db.SaveChangesAsync(ct);
        await clientCache.InvalidateAsync(instance.Id);
        await audit.RecordAsync(AuditEntries.Change(
            user, "haproxy.instance", "HaproxyInstance", instance.Id,
            detail: new { instance.Name, instance.Transport, instance.ApiPort }), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateSecretsAsync(
        int id,
        HaproxySecretsInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CredentialCipher cipher,
        IAuditService audit,
        HaproxyClientCache clientCache,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var instance = await db.HaproxyInstances
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);

        if (input.ClearPassword)
        {
            instance.EncryptedPassword = null;
            instance.PasswordKeyVersion = null;
        }
        else if (!string.IsNullOrWhiteSpace(input.Password))
        {
            var encrypted = cipher.Encrypt(input.Password);
            instance.EncryptedPassword = encrypted.Ciphertext;
            instance.PasswordKeyVersion = encrypted.KeyVersion;
        }

        if (input.ClearStatsPassword)
        {
            instance.EncryptedStatsPassword = null;
            instance.StatsPasswordKeyVersion = null;
        }
        else if (!string.IsNullOrWhiteSpace(input.StatsPassword))
        {
            var encrypted = cipher.Encrypt(input.StatsPassword);
            instance.EncryptedStatsPassword = encrypted.Ciphertext;
            instance.StatsPasswordKeyVersion = encrypted.KeyVersion;
        }

        instance.DiscoveryStatus = instance.EncryptedPassword is null
            ? "CredentialsRequired"
            : instance.MonitoringChoice == "Enabled" ? "Found" : instance.DiscoveryStatus;
        await db.SaveChangesAsync(ct);
        await clientCache.InvalidateAsync(instance.Id);
        await audit.RecordAsync(AuditEntries.Change(
            user, "haproxy.secrets", "HaproxyInstance", instance.Id,
            detail: new
            {
                passwordSet = instance.EncryptedPassword is not null,
                statsPasswordSet = instance.EncryptedStatsPassword is not null
            }), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetMonitoringAsync(
        int id,
        MonitoringInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        IAuditService audit,
        HaproxyRefreshRunner refresher,
        HaproxyClientCache clientCache,
        CancellationToken ct)
    {
        var instance = await db.HaproxyInstances
            .Include(x => x.Server)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);
        await access.RequireAdminAsync(
            user, [instance.Server!.ProviderEnvironmentId], ct);

        var choice = input.Choice?.Trim() ?? "";
        if (choice is not ("Undecided" or "Enabled" or "Disabled"))
            throw Guard.Unprocessable(
                "Choice ammessa: Undecided, Enabled, Disabled.");

        instance.MonitoringChoice = choice;
        instance.MonitoringChangedAt = DateTime.UtcNow;
        instance.MonitoringChangedBy = user.Identity?.Name ?? "";
        if (choice == "Enabled")
        {
            instance.DiscoveryStatus =
                instance.Username is not null && instance.EncryptedPassword is not null
                    ? "Found"
                    : "CredentialsRequired";
        }
        else if (choice == "Disabled")
        {
            instance.DiscoveryStatus = "Inactive";
        }

        await db.SaveChangesAsync(ct);
        await clientCache.InvalidateAsync(instance.Id);
        await audit.RecordAsync(AuditEntries.Change(
            user, "haproxy.monitoring", "HaproxyInstance", instance.Id,
            detail: new { choice }), ct);
        if (choice == "Enabled" &&
            instance.Username is not null &&
            instance.EncryptedPassword is not null)
            refresher.Start(instance.Id);
        return Results.NoContent();
    }

    private static async Task<IResult> TestAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        HaproxyClientFactory clients,
        IAuditService audit,
        CancellationToken ct)
    {
        var instance = await db.HaproxyInstances
            .Include(x => x.Server).ThenInclude(x => x!.Credential)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);
        await access.RequireAdminAsync(
            user, [instance.Server!.ProviderEnvironmentId], ct);
        if (string.IsNullOrEmpty(instance.Username) ||
            string.IsNullOrEmpty(instance.EncryptedPassword))
            throw Guard.Unprocessable(
                "Credenziali Data Plane API non configurate.");

        try
        {
            await using var client = await clients.CreateAsync(
                instance, instance.Server, ct);
            var result = await client.TestAsync(ct);
            instance.DiscoveryStatus = "Ready";
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(AuditEntries.Change(
                user, "haproxy.test", "HaproxyInstance", instance.Id,
                detail: new { result.ApiVersion, result.HaproxyVersion }), ct);
            return Results.Ok(result);
        }
        catch (Exception exception)
        {
            instance.LastTestAt = DateTime.UtcNow;
            instance.LastTestStatus = "Error";
            instance.LastTestDetail = Trim(exception.Message, 500);
            instance.DiscoveryStatus = "Unreachable";
            await db.SaveChangesAsync(CancellationToken.None);
            return Results.Json(
                new { message = exception.Message }, statusCode: 502);
        }
    }

    private static async Task<IResult> RefreshAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        HaproxyRefreshRunner refresher,
        IAuditService audit,
        CancellationToken ct)
    {
        var instance = await db.HaproxyInstances.AsNoTracking()
            .Include(x => x.Server)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);
        await access.RequireAdminAsync(
            user, [instance.Server!.ProviderEnvironmentId], ct);
        try
        {
            var result = await refresher.RefreshAsync(instance.Id, ct);
            await audit.RecordAsync(AuditEntries.Change(
                user, "haproxy.refresh", "HaproxyInstance", instance.Id,
                detail: new { result.Routes, result.Servers }), ct);
            return Results.Ok(result);
        }
        catch (Exception exception)
        {
            return Results.Json(
                new { message = exception.Message }, statusCode: 502);
        }
    }

    private static async Task<IResult> ListRoutesAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var instance = await db.HaproxyInstances.AsNoTracking()
            .Include(x => x.Server)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);
        await access.RequireOperatorAsync(
            user, [instance.Server!.ProviderEnvironmentId], ct);
        var items = await db.HaproxyRouteSnapshots.AsNoTracking()
            .Where(x => x.HaproxyInstanceId == id)
            .OrderBy(x => x.Frontend).ThenBy(x => x.Host).ThenBy(x => x.Path)
            .Select(x => new HaproxyRouteDto(
                x.Id, x.Frontend, x.Bind, x.Host, x.Path, x.Backend,
                x.IsDefault, x.RawCondition, x.ConfigurationVersion,
                x.CollectedAt))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> ListServersAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var instance = await db.HaproxyInstances.AsNoTracking()
            .Include(x => x.Server)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);
        await access.RequireOperatorAsync(
            user, [instance.Server!.ProviderEnvironmentId], ct);
        var items = await db.HaproxyServerSnapshots.AsNoTracking()
            .Where(x => x.HaproxyInstanceId == id)
            .OrderBy(x => x.Backend).ThenBy(x => x.Server)
            .Select(x => new HaproxyServerDto(
                x.Id, x.Backend, x.Server, x.Address, x.Status,
                x.CheckStatus, x.Weight, x.CollectedAt))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> LiveAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        HaproxyMetricsCache cache,
        CancellationToken ct)
    {
        var instance = await db.HaproxyInstances.AsNoTracking()
            .Include(x => x.Server)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);
        await access.RequireOperatorAsync(
            user, [instance.Server!.ProviderEnvironmentId], ct);
        return Results.Ok(cache.Get(id));
    }

    private static async Task<IResult> MetricsAsync(
        int id,
        HttpContext http,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var instance = await db.HaproxyInstances.AsNoTracking()
            .Include(x => x.Server)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);
        await access.RequireOperatorAsync(
            user, [instance.Server!.ProviderEnvironmentId], ct);

        var minutes = Math.Clamp(
            PageRequest.IntQuery(http, "minutes") ?? 60, 5, 129_600);
        var kind = (http.Request.Query["kind"].ToString() is { Length: > 0 } value
            ? value
            : "frontend").ToLowerInvariant();
        if (kind is not ("frontend" or "backend" or "server"))
            throw Guard.Unprocessable("Kind ammesso: frontend, backend, server.");
        var proxy = http.Request.Query["proxy"].ToString();
        var server = http.Request.Query["server"].ToString();
        var resolution = minutes > 10_080 ? 5 : 1;
        var since = DateTime.UtcNow.AddMinutes(-minutes);
        var rows = await db.HaproxyMetricSamples.AsNoTracking()
            .Where(x => x.HaproxyInstanceId == id &&
                        x.ResolutionMinutes == resolution &&
                        x.Kind == kind &&
                        x.Bucket >= since &&
                        (proxy == "" || x.Proxy == proxy) &&
                        (server == "" || x.Server == server))
            .OrderBy(x => x.Bucket)
            .ThenBy(x => x.Proxy)
            .ThenBy(x => x.Server)
            .ToListAsync(ct);

        var previous = new Dictionary<string, HaproxyMetricSample>();
        var points = new Dictionary<DateTime, MetricAccumulator>();
        foreach (var row in rows)
        {
            var key = $"{row.Kind}|{row.Proxy}|{row.Server}";
            previous.TryGetValue(key, out var before);
            previous[key] = row;
            var point = points.TryGetValue(row.Bucket, out var current)
                ? current
                : points[row.Bucket] = new MetricAccumulator();
            point.Add(row, before);
        }

        return Results.Ok(points.OrderBy(x => x.Key)
            .Select(x => x.Value.ToDto(x.Key))
            .ToList());
    }

    private static async Task<IResult> RunDiscoveryAsync(
        int? serverId,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        HaproxyDiscoveryRunner runner,
        CancellationToken ct)
    {
        if (serverId is null)
        {
            EnvironmentAccessService.RequireSystemAdmin(user);
            _ = Task.Run(() => runner.RunAllAsync(CancellationToken.None));
            return Results.Accepted(value: new { started = true });
        }

        var server = await db.Servers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == serverId.Value, ct)
            ?? throw Guard.NotFound("Server", serverId.Value);
        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);
        if (!runner.Start(server.Id))
            throw Guard.Conflict("Scoperta HAProxy già in corso per questo server.");
        return Results.Accepted(value: new { started = true });
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

public sealed record MonitoringInput(string? Choice);

public sealed record HaproxySettingsInput(
    string? Name,
    string? BaseUrl,
    string? Transport,
    string? ApiHost,
    int? ApiPort,
    string? ApiVersion,
    string? Username,
    string? StatsUsername,
    string? CaPem,
    bool? AllowSelfSigned,
    int? ServerApplicationId);

public sealed record HaproxySecretsInput(
    string? Password,
    string? StatsPassword,
    bool ClearPassword = false,
    bool ClearStatsPassword = false);

public sealed record HaproxyInstanceDto(
    int Id,
    int ServerId,
    string ServerName,
    int ProviderEnvironmentId,
    string EnvironmentLabel,
    int? ServerApplicationId,
    string Name,
    string BaseUrl,
    string Transport,
    string ApiHost,
    int ApiPort,
    string ApiVersion,
    string? EffectiveApiVersion,
    string MonitoringChoice,
    string DiscoveryStatus,
    string? DetectedVersion,
    string? DetectedConfigPath,
    DateTime? LastSeenAt,
    DateTime? LastTestAt,
    string? LastTestStatus,
    string? LastTestDetail,
    DateTime? LastRefreshAt,
    string? LastRefreshStatus,
    string? LastRefreshDetail,
    int RouteCount,
    int ServerCount,
    string? Username,
    string? StatsUsername,
    string? CaPem,
    bool HasCredentials,
    bool AllowSelfSigned);

public sealed record HaproxyRouteDto(
    int Id,
    string Frontend,
    string Bind,
    string Host,
    string Path,
    string Backend,
    bool IsDefault,
    string? RawCondition,
    int? ConfigurationVersion,
    DateTime CollectedAt);

public sealed record HaproxyServerDto(
    int Id,
    string Backend,
    string Server,
    string Address,
    string Status,
    string? CheckStatus,
    int? Weight,
    DateTime CollectedAt);

public sealed record HaproxyMetricPointDto(
    DateTime Timestamp,
    long Req,
    long H4xx,
    long H5xx,
    long Bin,
    long Bout,
    long Scur,
    long Qcur,
    long Rtime,
    long Ctime,
    long Ttime,
    long Qtime);

internal sealed class MetricAccumulator
{
    private long req;
    private long h4xx;
    private long h5xx;
    private long bin;
    private long bout;
    private long scur;
    private long qcur;
    private long rtime;
    private long ctime;
    private long ttime;
    private long qtime;
    private int samples;

    public void Add(HaproxyMetricSample row, HaproxyMetricSample? previous)
    {
        req += Delta(row.Req, previous?.Req);
        h4xx += Delta(row.H4xx, previous?.H4xx);
        h5xx += Delta(row.H5xx, previous?.H5xx);
        bin += Delta(row.Bin, previous?.Bin);
        bout += Delta(row.Bout, previous?.Bout);
        scur += row.Scur;
        qcur += row.Qcur;
        rtime += row.Rtime;
        ctime += row.Ctime;
        ttime += row.Ttime;
        qtime += row.Qtime;
        samples++;
    }

    public HaproxyMetricPointDto ToDto(DateTime timestamp) =>
        new(timestamp,
            req,
            h4xx,
            h5xx,
            bin,
            bout,
            scur,
            qcur,
            samples == 0 ? 0 : rtime / samples,
            samples == 0 ? 0 : ctime / samples,
            samples == 0 ? 0 : ttime / samples,
            samples == 0 ? 0 : qtime / samples);

    private static long Delta(long current, long? previous) =>
        previous is null ? 0 : current >= previous.Value
            ? current - previous.Value
            : current;
}

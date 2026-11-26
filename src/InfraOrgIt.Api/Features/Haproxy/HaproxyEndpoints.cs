using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
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
        group.MapGet("/clusters", ListClustersAsync);
        group.MapPost("/clusters", CreateClusterAsync)
            .RequireRateLimiting("remote-run");
        group.MapPut("/clusters/{id:int}", UpdateClusterAsync)
            .RequireRateLimiting("remote-run");
        group.MapDelete("/clusters/{id:int}", DeleteClusterAsync)
            .RequireRateLimiting("remote-run");
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
        group.MapPost("/instances/{id:int}/server-actions", ServerActionAsync)
            .RequireRateLimiting("remote-run");
        group.MapGet("/instances/{id:int}/routes", ListRoutesAsync);
        group.MapGet("/instances/{id:int}/servers", ListServersAsync);
        group.MapGet("/instances/{id:int}/live", LiveAsync);
        group.MapGet("/instances/{id:int}/metrics", MetricsAsync);
        group.MapGet("/instances/{id:int}/metrics/series", MetricsSeriesAsync);
        group.MapGet("/instances/{id:int}/protocol", ProtocolAsync);
        group.MapGet("/instances/{id:int}/certificates", ListCertificatesAsync);
        group.MapPost("/instances/{id:int}/certificates", UploadCertificateAsync)
            .RequireRateLimiting("remote-run");
        group.MapPut("/instances/{id:int}/certificates/{name}", ReplaceCertificateAsync)
            .RequireRateLimiting("remote-run");
        group.MapDelete("/instances/{id:int}/certificates/{name}", DeleteCertificateAsync)
            .RequireRateLimiting("remote-run");
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
        if (accessInfo.OperatorEnvironmentIds.Count == 0)
            return Results.Ok(Array.Empty<HaproxyInstanceDto>());

        var operatorIds = accessInfo.OperatorEnvironmentIds.ToList();
        var query = db.HaproxyInstances.AsNoTracking()
            .Where(x => operatorIds.Contains(x.Server!.ProviderEnvironmentId));
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
                x.ClusterId,
                x.Cluster != null ? x.Cluster.Name : null,
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
                x.Certificates.Count,
                x.Username,
                x.StatsUsername,
                x.StatsBaseUrl,
                x.CaPem,
                x.Username != null && x.EncryptedPassword != null,
                x.AllowSelfSigned))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> ListClustersAsync(
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var info = await access.GetAsync(user, ct);
        if (info.AdminEnvironmentIds.Count == 0)
            return Results.Ok(Array.Empty<HaproxyClusterDto>());
        var ids = info.AdminEnvironmentIds.ToList();
        var items = await db.HaproxyClusters.AsNoTracking()
            .Where(x => ids.Contains(x.ProviderEnvironmentId))
            .OrderBy(x => x.Name)
            .Select(x => new HaproxyClusterDto(
                x.Id, x.ProviderEnvironmentId, x.Name, x.Mode, x.Vip,
                x.Notes, x.Instances.Count))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateClusterAsync(
        HaproxyClusterInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        var environmentId = input.ProviderEnvironmentId
                            ?? throw Guard.Unprocessable(
                                "ProviderEnvironmentId è obbligatorio.");
        await access.RequireAdminAsync(user, [environmentId], ct);
        var entity = new HaproxyCluster
        {
            ProviderEnvironmentId = environmentId,
            Name = Guard.Text(input.Name, "Name", 64),
            Mode = ClusterMode(input.Mode),
            Vip = Guard.OptionalText(input.Vip, "Vip", 200),
            Notes = Guard.OptionalText(input.Notes, "Notes", 500)
        };
        db.HaproxyClusters.Add(entity);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditEntries.Change(
            user, "haproxy.cluster", "HaproxyCluster", entity.Id,
            detail: new { entity.Name, entity.Mode }), ct);
        return Results.Ok(new HaproxyClusterDto(
            entity.Id, entity.ProviderEnvironmentId, entity.Name,
            entity.Mode, entity.Vip, entity.Notes, 0));
    }

    private static async Task<IResult> UpdateClusterAsync(
        int id,
        HaproxyClusterInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        var entity = await db.HaproxyClusters
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Cluster HAProxy", id);
        await access.RequireAdminAsync(
            user, [entity.ProviderEnvironmentId], ct);
        if (input.ProviderEnvironmentId is not null &&
            input.ProviderEnvironmentId != entity.ProviderEnvironmentId)
            throw Guard.Unprocessable(
                "Il cluster non può cambiare ambiente.");
        entity.Name = Guard.Text(input.Name, "Name", 64);
        entity.Mode = ClusterMode(input.Mode);
        entity.Vip = Guard.OptionalText(input.Vip, "Vip", 200);
        entity.Notes = Guard.OptionalText(input.Notes, "Notes", 500);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditEntries.Change(
            user, "haproxy.cluster", "HaproxyCluster", entity.Id,
            detail: new { entity.Name, entity.Mode }), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteClusterAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        var entity = await db.HaproxyClusters
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Cluster HAProxy", id);
        await access.RequireAdminAsync(
            user, [entity.ProviderEnvironmentId], ct);
        db.HaproxyClusters.Remove(entity);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditEntries.Change(
            user, "haproxy.cluster.delete", "HaproxyCluster", id,
            detail: new { entity.Name }), ct);
        return Results.NoContent();
    }

    private static string ClusterMode(string? value)
    {
        var mode = value?.Trim().ToLowerInvariant() ?? "active-active";
        return mode is "active-active" or "active-passive"
            ? mode
            : throw Guard.Unprocessable(
                "Mode ammesso: active-active, active-passive.");
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
        var apiVersion = input.ApiVersion?.Trim().ToLowerInvariant() ?? "auto";
        if (apiVersion is not ("auto" or "v2" or "v3" or "stats"))
            throw Guard.Unprocessable("ApiVersion ammessa: auto, v2, v3, stats.");
        var statsOnly = apiVersion == "stats";
        var baseUrl = (input.BaseUrl ?? "").Trim().TrimEnd('/');
        if (baseUrl.Length > 300)
            throw Guard.Unprocessable("BaseUrl troppo lungo (max 300).");
        var baseUri = statsOnly ? null : HaproxyUrlRules.Parse(baseUrl, "BaseUrl");
        var transport = input.Transport?.Trim() ?? "SshForward";
        if (transport is not ("Direct" or "SshForward" or "DesktopRelay"))
            throw Guard.Unprocessable("Transport non valido.");
        var statsBaseUrl = OptionalUrl(input.StatsBaseUrl, "StatsBaseUrl");
        if (statsOnly && statsBaseUrl is null)
            throw Guard.Unprocessable(
                "In modalità solo stats serve l'URL della pagina stats.");
        var statsUri = statsBaseUrl is null ? null : new Uri(statsBaseUrl);
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

        var tlsUrl = statsOnly ? statsBaseUrl! : baseUrl;
        if (transport == "Direct" &&
            !tlsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw Guard.Unprocessable("Direct richiede un URL https.");
        if (transport == "SshForward" &&
            tlsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(caPem) &&
            !(input.AllowSelfSigned ?? false))
            throw Guard.Unprocessable(
                "Per https serve una CA oppure Accetta self-signed.");
        if (transport == "Direct" &&
            ((baseUri is not null &&
              !HaproxyUrlRules.HostMatchesServer(baseUri, instance.Server!)) ||
             (statsUri is not null &&
              !HaproxyUrlRules.HostMatchesServer(statsUri, instance.Server!))))
            EnvironmentAccessService.RequireSystemAdmin(user);

        var apiPort = input.ApiPort ?? instance.ApiPort;
        if (apiPort is < 1 or > 65535)
            throw Guard.Unprocessable("ApiPort non valida.");

        if (input.ServerApplicationId is not null)
        {
            var belongs = await db.ServerApplications.AnyAsync(
                x => x.Id == input.ServerApplicationId.Value &&
                     x.ServerId == instance.ServerId, ct);
            if (!belongs)
                throw Guard.Unprocessable(
                    "Il deployment HAProxy non appartiene a questo server.");
        }
        if (input.ClusterId is not null)
        {
            var cluster = await db.HaproxyClusters.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == input.ClusterId.Value, ct)
                ?? throw Guard.NotFound("Cluster HAProxy", input.ClusterId.Value);
            if (cluster.ProviderEnvironmentId != instance.Server.ProviderEnvironmentId)
                throw Guard.Unprocessable(
                    "Il cluster appartiene a un altro ambiente.");
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
        instance.StatsBaseUrl = statsBaseUrl;
        instance.CaPem = caPem;
        instance.AllowSelfSigned = input.AllowSelfSigned ?? false;
        instance.ServerApplicationId = input.ServerApplicationId;
        instance.ClusterId = input.ClusterId;
        if (instance.EncryptedPassword is null && !statsOnly)
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

        if (instance.ApiVersion == "stats")
        {
            if (instance.MonitoringChoice == "Enabled")
                instance.DiscoveryStatus = "Found";
        }
        else if (instance.EncryptedPassword is null)
        {
            instance.DiscoveryStatus = "CredentialsRequired";
        }
        else if (instance.MonitoringChoice == "Enabled")
        {
            instance.DiscoveryStatus = "Found";
        }
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
        var statsOnly = instance.ApiVersion == "stats";
        var ready = statsOnly
            ? instance.StatsBaseUrl is not null
            : instance.Username is not null && instance.EncryptedPassword is not null;
        if (choice == "Enabled")
            instance.DiscoveryStatus = ready ? "Found" : "CredentialsRequired";
        else if (choice == "Disabled")
            instance.DiscoveryStatus = "Inactive";

        await db.SaveChangesAsync(ct);
        await clientCache.InvalidateAsync(instance.Id);
        await audit.RecordAsync(AuditEntries.Change(
            user, "haproxy.monitoring", "HaproxyInstance", instance.Id,
            detail: new { choice }), ct);
        if (choice == "Enabled" && ready)
            refresher.Start(instance.Id);
        return Results.NoContent();
    }

    private static async Task<IResult> TestAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        HaproxyClientFactory clients,
        HaproxyStatsClientFactory statsClients,
        IAuditService audit,
        CancellationToken ct)
    {
        var instance = await db.HaproxyInstances
            .Include(x => x.Server).ThenInclude(x => x!.Credential)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);
        await access.RequireAdminAsync(
            user, [instance.Server!.ProviderEnvironmentId], ct);
        var statsOnly = instance.ApiVersion == "stats";
        if (statsOnly && instance.StatsBaseUrl is null)
            throw Guard.Unprocessable(
                "URL della pagina stats non configurato.");
        if (!statsOnly &&
            (string.IsNullOrEmpty(instance.Username) ||
             string.IsNullOrEmpty(instance.EncryptedPassword)))
            throw Guard.Unprocessable(
                "Credenziali Data Plane API non configurate.");

        try
        {
            HaproxyConnectionTest result;
            if (statsOnly)
            {
                await using var statsClient = await statsClients.CreateAsync(
                    instance, instance.Server, ct);
                var rows = HaproxyStatsCsv.ParseStats(
                    await statsClient.GetCsvAsync(ct));
                instance.EffectiveApiVersion = "stats";
                result = new HaproxyConnectionTest(
                    "stats", null, rows.Count, DateTime.UtcNow);
            }
            else
            {
                await using var client = await clients.CreateAsync(
                    instance, instance.Server, ct);
                result = await client.TestAsync(ct);
            }
            instance.LastTestAt = result.At;
            instance.LastTestStatus = "Ok";
            instance.LastTestDetail = null;
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
                detail: new { result.Routes, result.Servers, result.Certificates }), ct);
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
        var snapshots = await db.HaproxyRouteSnapshots.AsNoTracking()
            .Where(x => x.HaproxyInstanceId == id)
            .OrderBy(x => x.Frontend).ThenBy(x => x.Host).ThenBy(x => x.Path)
            .ToListAsync(ct);
        var inventory = await db.ProxyRoutes.AsNoTracking()
            .Where(x => x.ProxyConnection!.SourceServerId == instance.ServerId)
            .Include(x => x.TargetServerApplication)
            .ThenInclude(x => x!.ApplicationVersion)
            .ThenInclude(x => x!.Application)
            .Select(x => new
            {
                x.Id,
                x.VirtualHost,
                x.Route,
                x.ListenPort,
                x.MappedTo,
                ApplicationName = x.TargetServerApplication == null
                    ? null
                    : x.TargetServerApplication.ApplicationVersion == null
                        ? null
                        : x.TargetServerApplication.ApplicationVersion.Application == null
                            ? null
                            : x.TargetServerApplication.ApplicationVersion.Application.Name,
                x.TargetServerApplication!.HostPort
            })
            .ToListAsync(ct);
        var items = snapshots.Select(x =>
        {
            var port = FirstPort(x.Bind);
            var match = inventory.FirstOrDefault(candidate =>
                string.Equals(candidate.VirtualHost, x.Host,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizePath(candidate.Route),
                    NormalizePath(x.Path), StringComparison.OrdinalIgnoreCase) &&
                (candidate.ListenPort is null || candidate.ListenPort == port));
            return new HaproxyRouteDto(
                x.Id, x.Frontend, x.Bind, x.Mode, x.Host, x.Path, x.Backend,
                x.IsDefault, x.RawCondition, x.ConfigurationVersion,
                x.CollectedAt,
                match?.Id,
                match is null
                    ? null
                    : $"{match.VirtualHost ?? "*"}{match.Route}" +
                      (match.MappedTo is null ? "" : $" → {match.MappedTo}"),
                match?.ApplicationName,
                match?.HostPort);
        }).ToList();
        return Results.Ok(items);
    }

    private static int? FirstPort(string bind)
    {
        var first = bind.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return int.TryParse(first, out var port) ? port : null;
    }

    private static string NormalizePath(string? value) =>
        string.IsNullOrWhiteSpace(value) || value == "/" ? "/" : value;

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

    private static async Task<IResult> ProtocolAsync(
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
        var kind = (http.Request.Query["kind"].ToString() is { Length: > 0 } value
            ? value
            : "frontend").ToLowerInvariant();
        if (kind is not ("frontend" or "backend" or "server"))
            throw Guard.Unprocessable("Kind ammesso: frontend, backend, server.");
        var bucket = await db.HaproxyProtocolMetricSamples.AsNoTracking()
            .Where(x => x.HaproxyInstanceId == id &&
                        x.ResolutionMinutes == 1 &&
                        x.Kind == kind)
            .OrderByDescending(x => x.Bucket)
            .Select(x => (DateTime?)x.Bucket)
            .FirstOrDefaultAsync(ct);
        if (bucket is null) return Results.Ok(Array.Empty<HaproxyProtocolDto>());
        var items = await db.HaproxyProtocolMetricSamples.AsNoTracking()
            .Where(x => x.HaproxyInstanceId == id &&
                        x.ResolutionMinutes == 1 &&
                        x.Kind == kind &&
                        x.Bucket == bucket.Value)
            .OrderBy(x => x.Proxy).ThenBy(x => x.Server)
            .Select(x => new HaproxyProtocolDto(
                x.Proxy, x.Server, x.H2Req, x.H2Sess, x.H3Req, x.H3Sess,
                x.H2OpenConnections, x.H2BackendOpenStreams,
                x.H2ConnErrors, x.H2StreamErrors, x.QuicHandshakeFail,
                x.QuicDroppedPkt, x.QuicLostPkt, x.QuicSentPkt,
                x.QuicRetryError, x.QuicTooShortDgram, x.QuicRxbufFull,
                x.QuicSocketFull, x.H3VersionFallback, x.H3ProtocolErrors,
                x.QuicTransportErrors, x.QpackEncoderErrors,
                x.QpackDecoderErrors, x.SslFailedHandshake, x.Bucket))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> ServerActionAsync(
        int id,
        HaproxyServerActionInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        HaproxyClientFactory clients,
        HaproxyStatsClientFactory statsClients,
        IAuditService audit,
        CancellationToken ct)
    {
        var instance = await db.HaproxyInstances
            .Include(x => x.Server).ThenInclude(x => x!.Credential)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);
        await access.RequireOperatorAsync(
            user, [instance.Server!.ProviderEnvironmentId], ct);
        var action = input.Action?.Trim().ToLowerInvariant() ?? "";
        if (action is not ("ready" or "drain" or "maint"))
            throw Guard.Unprocessable("Azione ammessa: ready, drain, maint.");
        var backend = Guard.Text(input.Backend, "Backend", 100);
        var server = Guard.Text(input.Server, "Server", 200);
        try
        {
            if (instance.ApiVersion == "stats")
            {
                await using var statsClient = await statsClients.CreateAsync(
                    instance, instance.Server, ct);
                await statsClient.SetServerStateAsync(
                    backend, server, action, ct);
            }
            else
            {
                await using var client = await clients.CreateAsync(
                    instance, instance.Server, ct);
                await client.SetServerStateAsync(backend, server, action, ct);
            }
            await audit.RecordAsync(AuditEntries.Change(
                user, "haproxy.server-action", "HaproxyInstance", id,
                detail: new { backend, server, action }), ct);
            return Results.NoContent();
        }
        catch (Exception exception)
        {
            return Results.Json(
                new { message = exception.Message }, statusCode: 502);
        }
    }

    private static async Task<IResult> ListCertificatesAsync(
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
        var items = await db.HaproxyCertificateSnapshots.AsNoTracking()
            .Where(x => x.HaproxyInstanceId == id)
            .OrderBy(x => x.NotAfter).ThenBy(x => x.StorageName)
            .Select(x => new HaproxyCertificateDto(
                x.Id, x.StorageName, x.Path, x.Domains, x.Issuer,
                x.NotBefore, x.NotAfter, x.Size, x.CollectedAt))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> UploadCertificateAsync(
        int id,
        HttpContext http,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        HaproxyClientFactory clients,
        HaproxyClientCache clientCache,
        IAuditService audit,
        CancellationToken ct)
    {
        var loaded = await LoadCertificateTargetAsync(
            id, user, access, db, ct);
        var (name, pem) = await ReadCertificateFormAsync(http, null, ct);
        try
        {
            await using var client = await clients.CreateAsync(
                loaded.Instance, loaded.Server, ct);
            await client.UploadCertificateAsync(name, pem, false, ct);
        }
        catch (Exception exception)
        {
            return Results.Json(
                new { message = exception.Message }, statusCode: 502);
        }
        await clientCache.InvalidateAsync(id);
        await audit.RecordAsync(AuditEntries.Change(
            user, "haproxy.certificate", "HaproxyInstance", id,
            detail: new { name, operation = "upload" }), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReplaceCertificateAsync(
        int id,
        string name,
        HttpContext http,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        HaproxyClientFactory clients,
        HaproxyClientCache clientCache,
        IAuditService audit,
        CancellationToken ct)
    {
        var loaded = await LoadCertificateTargetAsync(
            id, user, access, db, ct);
        var (formName, pem) = await ReadCertificateFormAsync(http, name, ct);
        try
        {
            await using var client = await clients.CreateAsync(
                loaded.Instance, loaded.Server, ct);
            await client.UploadCertificateAsync(formName, pem, true, ct);
        }
        catch (Exception exception)
        {
            return Results.Json(
                new { message = exception.Message }, statusCode: 502);
        }
        await clientCache.InvalidateAsync(id);
        await audit.RecordAsync(AuditEntries.Change(
            user, "haproxy.certificate", "HaproxyInstance", id,
            detail: new { name = formName, operation = "replace" }), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteCertificateAsync(
        int id,
        string name,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        HaproxyClientFactory clients,
        HaproxyClientCache clientCache,
        IAuditService audit,
        CancellationToken ct)
    {
        var loaded = await LoadCertificateTargetAsync(
            id, user, access, db, ct);
        var clean = SafeCertificateName(name);
        try
        {
            await using var client = await clients.CreateAsync(
                loaded.Instance, loaded.Server, ct);
            await client.DeleteCertificateAsync(clean, ct);
        }
        catch (Exception exception)
        {
            return Results.Json(
                new { message = exception.Message }, statusCode: 502);
        }
        await clientCache.InvalidateAsync(id);
        await audit.RecordAsync(AuditEntries.Change(
            user, "haproxy.certificate", "HaproxyInstance", id,
            detail: new { name = clean, operation = "delete" }), ct);
        return Results.NoContent();
    }

    private static async Task<(HaproxyInstance Instance, Server Server)>
        LoadCertificateTargetAsync(
            int id,
            ClaimsPrincipal user,
            EnvironmentAccessService access,
            InfraOrgItDbContext db,
            CancellationToken ct)
    {
        var instance = await db.HaproxyInstances
            .Include(x => x.Server).ThenInclude(x => x!.Credential)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);
        await access.RequireAdminAsync(
            user, [instance.Server!.ProviderEnvironmentId], ct);
        if (instance.ApiVersion == "stats")
            throw Guard.Unprocessable(
                "La gestione dei certificati richiede la Data Plane API.");
        if (string.IsNullOrEmpty(instance.Username) ||
            string.IsNullOrEmpty(instance.EncryptedPassword))
            throw Guard.Unprocessable("Credenziali Data Plane API non configurate.");
        return (instance, instance.Server);
    }

    private static async Task<(string Name, string Pem)> ReadCertificateFormAsync(
        HttpContext http,
        string? pathName,
        CancellationToken ct)
    {
        string name;
        string pem;
        try
        {
            var form = await http.Request.ReadFormAsync(ct);
            var file = form.Files["file"] ?? form.Files.FirstOrDefault()
                       ?? throw Guard.Unprocessable("File PEM mancante.");
            if (file.Length is <= 0 or > 1_048_576)
                throw Guard.Unprocessable("Il PEM deve essere tra 1 byte e 1 MiB.");
            name = pathName ?? form["name"].ToString();
            if (string.IsNullOrWhiteSpace(name))
                name = Path.GetFileName(file.FileName);
            using var reader = new StreamReader(file.OpenReadStream());
            pem = await reader.ReadToEndAsync(ct);
        }
        catch (InvalidDataException)
        {
            throw Guard.Unprocessable("Multipart non valido.");
        }

        name = SafeCertificateName(name);
        if (!pem.Contains("PRIVATE KEY", StringComparison.Ordinal) ||
            !pem.Contains("CERTIFICATE", StringComparison.Ordinal))
            throw Guard.Unprocessable(
                "Il PEM deve contenere certificato e chiave privata.");
        try
        {
            var certificates = new X509Certificate2Collection();
            certificates.ImportFromPem(pem);
            if (certificates.Count == 0)
                throw new FormatException();
        }
        catch (Exception exception) when (exception is CryptographicException
                                              or FormatException
                                              or ArgumentException)
        {
            throw Guard.Unprocessable("PEM non valido.");
        }
        return (name, pem);
    }

    private static string SafeCertificateName(string value)
    {
        var name = Path.GetFileName(value.Trim());
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9._-]{1,200}$"))
            throw Guard.Unprocessable(
                "Nome certificato non valido (lettere, numeri, punto, trattino, underscore).");
        return name;
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

    private static async Task<IResult> MetricsSeriesAsync(
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
            : "server").ToLowerInvariant();
        if (kind is not ("frontend" or "backend" or "server"))
            throw Guard.Unprocessable("Kind ammesso: frontend, backend, server.");
        var resolution = minutes > 10_080 ? 5 : 1;
        var since = DateTime.UtcNow.AddMinutes(-minutes);
        var rows = await db.HaproxyMetricSamples.AsNoTracking()
            .Where(x => x.HaproxyInstanceId == id &&
                        x.ResolutionMinutes == resolution &&
                        x.Kind == kind &&
                        x.Bucket >= since)
            .OrderBy(x => x.Bucket)
            .ThenBy(x => x.Proxy)
            .ThenBy(x => x.Server)
            .ToListAsync(ct);

        var previous = new Dictionary<string, HaproxyMetricSample>();
        var series = new Dictionary<string, (string Proxy, string Server,
            List<HaproxyMetricPointDto> Points)>();
        foreach (var row in rows)
        {
            var key = $"{row.Proxy}|{row.Server}";
            previous.TryGetValue(key, out var before);
            previous[key] = row;
            if (!series.TryGetValue(key, out var item))
            {
                item = (row.Proxy, row.Server, []);
                series[key] = item;
            }
            var accumulator = new MetricAccumulator();
            accumulator.Add(row, before);
            item.Points.Add(accumulator.ToDto(row.Bucket));
        }

        return Results.Ok(series.Values
            .OrderBy(x => x.Proxy).ThenBy(x => x.Server)
            .Select(x => new HaproxyMetricSeriesDto(
                x.Proxy, x.Server, x.Points))
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

    private static string? OptionalUrl(string? value, string field)
    {
        var text = Guard.OptionalText(value, field, 300);
        if (text is null) return null;
        HaproxyUrlRules.Parse(text, field);
        return text.TrimEnd('/');
    }
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
    int? ServerApplicationId,
    int? ClusterId = null,
    string? StatsBaseUrl = null);

public sealed record HaproxyClusterInput(
    int? ProviderEnvironmentId,
    string? Name,
    string? Mode,
    string? Vip,
    string? Notes);

public sealed record HaproxyServerActionInput(
    string? Backend,
    string? Server,
    string? Action);

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
    int? ClusterId,
    string? ClusterName,
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
    int CertificateCount,
    string? Username,
    string? StatsUsername,
    string? StatsBaseUrl,
    string? CaPem,
    bool HasCredentials,
    bool AllowSelfSigned);

public sealed record HaproxyRouteDto(
    int Id,
    string Frontend,
    string Bind,
    string Mode,
    string Host,
    string Path,
    string Backend,
    bool IsDefault,
    string? RawCondition,
    int? ConfigurationVersion,
    DateTime CollectedAt,
    int? ProxyRouteId,
    string? ProxyRouteLabel,
    string? ApplicationName,
    int? HostPort);

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

public sealed record HaproxyMetricSeriesDto(
    string Proxy,
    string Server,
    IReadOnlyList<HaproxyMetricPointDto> Points);

public sealed record HaproxyCertificateDto(
    int Id,
    string StorageName,
    string Path,
    string Domains,
    string Issuer,
    DateTime? NotBefore,
    DateTime? NotAfter,
    long Size,
    DateTime CollectedAt);

public sealed record HaproxyClusterDto(
    int Id,
    int ProviderEnvironmentId,
    string Name,
    string Mode,
    string? Vip,
    string? Notes,
    int InstanceCount);

public sealed record HaproxyProtocolDto(
    string Proxy,
    string Server,
    long H2Req,
    long H2Sess,
    long H3Req,
    long H3Sess,
    long H2OpenConnections,
    long H2BackendOpenStreams,
    long H2ConnErrors,
    long H2StreamErrors,
    long QuicHandshakeFail,
    long QuicDroppedPkt,
    long QuicLostPkt,
    long QuicSentPkt,
    long QuicRetryError,
    long QuicTooShortDgram,
    long QuicRxbufFull,
    long QuicSocketFull,
    long H3VersionFallback,
    long H3ProtocolErrors,
    long QuicTransportErrors,
    long QpackEncoderErrors,
    long QpackDecoderErrors,
    long SslFailedHandshake,
    DateTime Bucket);

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

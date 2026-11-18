using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace InfraOrgIt.Api.Features.Data;

public static class DataTransferEndpoints
{
    private const string Format = "infraorgit.inventory";
    private const int FormatVersion = 1;
    private const long MaxImportBytes = 20 * 1024 * 1024;
    private const string PreviewKeyPrefix = "data-import:";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/data").RequireAuthorization();
        group.MapGet("/export", ExportAsync).RequireRateLimiting("files");
        group.MapPost("/import", PreviewAsync)
            .RequireRateLimiting("files");
        group.MapPost("/import/confirm", ConfirmAsync)
            .RequireRateLimiting("files");
    }

    private static async Task<IResult> ExportAsync(
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        IAuditService audit,
        IConfiguration configuration,
        CancellationToken ct)
    {
        await access.RequireSystemAdminAsync(user, ct);

        var data = new InventoryData
        {
            Providers = await db.Providers.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            EnvironmentDefinitions = await db.EnvironmentDefinitions.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            ProviderEnvironments = await db.ProviderEnvironments.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            Servers = await db.Servers.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            Applications = await db.Applications.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            ApplicationVersions = await db.ApplicationVersions.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            ApplicationVersionApis = await db.ApplicationVersionApis.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            ApplicationApiQueryParams = await db.ApplicationApiQueryParams.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            ServerApplications = await db.ServerApplications.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            Projects = await db.Projects.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            ApplicationProjects = await db.Set<ApplicationProject>().AsNoTracking()
                .OrderBy(x => x.ApplicationsId).ThenBy(x => x.ProjectsId).ToListAsync(ct),
            ProxyConnections = await db.ProxyConnections.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            ProxyRoutes = await db.ProxyRoutes.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            TunnelServices = await db.TunnelServices.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            CommandTemplates = await db.CommandTemplates.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            ApplicationActions = await db.ApplicationActions.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            ApplicationEnvironments = await db.ApplicationEnvironments.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            Diagrams = await db.Diagrams.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            DiagramScopeItems = await db.DiagramScopeItems.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            DiagramNodes = await db.DiagramNodes.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            DiagramEdges = await db.DiagramEdges.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
            Translations = await db.Translations.AsNoTracking()
                .OrderBy(x => x.Id).ToListAsync(ct),
        };

        var credentialIds = data.Servers
            .SelectMany(x => new[] { x.CredentialId, x.FileTransferCredentialId })
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var credentialRefs = await db.ServerCredentials.AsNoTracking()
            .Where(x => x.OwnerUserId == null && credentialIds.Contains(x.Id))
            .OrderBy(x => x.Id)
            .Select(x => new CredentialRef(
                x.Id, x.Name, x.Username, x.AuthType, "shared"))
            .ToListAsync(ct);

        var warnings = new List<string>();
        if (data.Servers.Any(x => x.CredentialId is not null ||
                                  x.FileTransferCredentialId is not null))
        {
            warnings.Add(
                "Credential references are metadata only; re-create or download credentials after import.");
        }
        if (await db.ApplicationApiKeys.AnyAsync(ct))
            warnings.Add("Application API keys are not exported (encrypted with the source instance key).");
        if (await db.Menus.AnyAsync(ct))
            warnings.Add("Custom menu labels are not exported; the local instance seeds the default menu.");

        var checksum = Checksum(data, credentialRefs);
        var document = new InventoryExportDocument(
            Format,
            FormatVersion,
            Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev",
            DateTimeOffset.UtcNow,
            new ExportSource(
                configuration.GetValue("Local:Enabled", false) ? "local" : "remote",
                configuration["Local:Username"] ?? "remote"),
            Counts(data, credentialRefs),
            checksum,
            data,
            credentialRefs,
            warnings);

        await audit.RecordAsync(AuditEntries.Change(
            user,
            "data.export",
            "DataExport",
            0,
            detail: new
            {
                document.Counts,
                credentialRefs = credentialRefs.Count,
                warnings = warnings.Count,
            }), ct);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        var fileName = $"infraorgit-inventory-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        return Results.File(bytes, "application/json", fileName);
    }

    private static async Task<IResult> PreviewAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        IMemoryCache cache,
        CancellationToken ct)
    {
        await access.RequireSystemAdminAsync(user, ct);
        if (db.Database.ProviderName?.Contains(
                "Sqlite", StringComparison.OrdinalIgnoreCase) != true)
        {
            throw Guard.Unprocessable(
                "Import replace is available only in the local SQLite instance.");
        }
        if (request.ContentLength > MaxImportBytes)
            throw Guard.Unprocessable("File too large (max 20 MB).");

        string json;
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault()
                       ?? throw Guard.Unprocessable("File di import mancante.");
            if (file.Length > MaxImportBytes)
                throw Guard.Unprocessable("File too large (max 20 MB).");
            using var reader = new StreamReader(file.OpenReadStream());
            json = await reader.ReadToEndAsync(ct);
        }
        else
        {
            using var reader = new StreamReader(request.Body);
            json = await reader.ReadToEndAsync(ct);
        }
        if (Encoding.UTF8.GetByteCount(json) > MaxImportBytes)
            throw Guard.Unprocessable("File too large (max 20 MB).");

        InventoryExportDocument document;
        try
        {
            document = JsonSerializer.Deserialize<InventoryExportDocument>(json, Json)
                       ?? throw new JsonException("Empty document.");
        }
        catch (JsonException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "File di import non valido",
                extensions: new Dictionary<string, object?> { ["code"] = "import_invalid" });
        }

        if (document.Data is null || document.CredentialRefs is null)
            throw Guard.Unprocessable("Il file non contiene dati di inventario validi.");
        if (!string.Equals(document.Format, Format, StringComparison.Ordinal) ||
            document.FormatVersion != FormatVersion)
            throw Guard.Unprocessable("Formato o versione di import non supportati.");
        if (!string.Equals(document.Checksum, Checksum(document.Data, document.CredentialRefs),
                StringComparison.Ordinal))
            throw Guard.Unprocessable("Checksum del file non valido.");
        Validate(data: document.Data);

        var existing = await InventoryCountAsync(db, ct);
        var canApply = existing == 0;
        var warnings = new List<string>(document.Warnings ?? []);
        if (document.Data.Servers.Any(x => x.CredentialId is not null ||
                                           x.FileTransferCredentialId is not null))
            warnings.Add("Server credentials will be left unassigned after import.");
        if (document.CredentialRefs is { Count: > 0 })
            warnings.Add($"Credential metadata: {document.CredentialRefs.Count} shared references.");

        var token = Guid.NewGuid().ToString("N");
        var preview = new ImportPreview(
            document,
            user.Identity?.Name ?? "",
            DateTimeOffset.UtcNow.AddMinutes(10));
        cache.Set(PreviewKeyPrefix + token, preview, preview.ExpiresAt);

        return Results.Ok(new
        {
            token,
            canApply,
            existing,
            counts = Counts(document.Data, document.CredentialRefs),
            warnings,
            credentialRefs = document.CredentialRefs,
            expiresAt = preview.ExpiresAt,
        });
    }

    private static async Task<IResult> ConfirmAsync(
        ImportConfirmInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        IMemoryCache cache,
        IAuditService audit,
        IConfiguration configuration,
        CancellationToken ct)
    {
        await access.RequireSystemAdminAsync(user, ct);
        if (!string.Equals(input.Mode, "replace", StringComparison.OrdinalIgnoreCase))
            throw Guard.Unprocessable("Modalità di import supportata: replace.");
        var token = input.Token?.Trim() ?? "";
        if (token.Length == 0 ||
            !cache.TryGetValue(PreviewKeyPrefix + token, out ImportPreview? preview) ||
            preview is null ||
            preview.ExpiresAt <= DateTimeOffset.UtcNow ||
            !string.Equals(preview.Username, user.Identity?.Name, StringComparison.Ordinal))
        {
            throw Guard.Unprocessable("Anteprima di import scaduta o non valida.");
        }

        if (await InventoryCountAsync(db, ct) != 0)
            throw Guard.Conflict("Il database locale non è vuoto; import replace rifiutato.");
        var backup = await BackupAsync(db, configuration, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await ApplyAsync(db, preview.Document.Data, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            cache.Remove(PreviewKeyPrefix + token);
            await audit.RecordAsync(AuditEntries.Change(
                user,
                "data.import",
                "DataImport",
                0,
                detail: new
                {
                    mode = "replace",
                    backup,
                    counts = preview.Document.Counts,
                }), ct);
            return Results.Ok(new
            {
                imported = true,
                counts = Counts(preview.Document.Data, preview.Document.CredentialRefs),
                backup,
                credentialRefs = preview.Document.CredentialRefs,
            });
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<string?> BackupAsync(
        InfraOrgItDbContext db,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var dataSource = db.Database.GetDbConnection().DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) ||
            db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) != true)
            return null;
        var directory = configuration["Local:BackupDirectory"];
        if (string.IsNullOrWhiteSpace(directory))
            directory = Path.Combine(Path.GetDirectoryName(dataSource)!, "backups");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(
            directory,
            $"infraorgit-before-import-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.db");
        var source = (SqliteConnection)db.Database.GetDbConnection();
        var wasOpen = source.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await source.OpenAsync(ct);
        try
        {
            await using var destination = new SqliteConnection($"Data Source={target}");
            await destination.OpenAsync(ct);
            source.BackupDatabase(destination);
        }
        finally
        {
            if (!wasOpen) await source.CloseAsync();
        }
        return target;
    }

    private static async Task ApplyAsync(
        InfraOrgItDbContext db,
        InventoryData data,
        CancellationToken ct)
    {
        var routeByApplication = data.ServerApplications
            .ToDictionary(x => x.Id, x => x.ProxyRouteId);
        var jumpByServer = data.Servers
            .ToDictionary(x => x.Id, x => x.JumpServerId);
        foreach (var application in data.ServerApplications)
            application.ProxyRouteId = null;
        foreach (var server in data.Servers)
        {
            server.CredentialId = null;
            server.FileTransferCredentialId = null;
            server.JumpServerId = null;
        }
        db.Providers.AddRange(data.Providers);
        db.EnvironmentDefinitions.AddRange(data.EnvironmentDefinitions);
        db.ProviderEnvironments.AddRange(data.ProviderEnvironments);
        db.Applications.AddRange(data.Applications);
        db.Projects.AddRange(data.Projects);
        db.ApplicationVersions.AddRange(data.ApplicationVersions);
        db.ApplicationVersionApis.AddRange(data.ApplicationVersionApis);
        db.ApplicationApiQueryParams.AddRange(data.ApplicationApiQueryParams);
        db.Servers.AddRange(data.Servers);
        await db.SaveChangesAsync(ct);

        foreach (var server in data.Servers)
        {
            if (jumpByServer[server.Id] is int jumpId)
                server.JumpServerId = jumpId;
        }
        db.ServerApplications.AddRange(data.ServerApplications);
        db.ProxyConnections.AddRange(data.ProxyConnections);
        await db.SaveChangesAsync(ct);
        db.ProxyRoutes.AddRange(data.ProxyRoutes);
        await db.SaveChangesAsync(ct);
        foreach (var application in data.ServerApplications)
        {
            if (routeByApplication[application.Id] is int routeId &&
                data.ProxyRoutes.Any(x => x.Id == routeId))
                application.ProxyRouteId = routeId;
        }
        db.TunnelServices.AddRange(data.TunnelServices);
        db.CommandTemplates.AddRange(data.CommandTemplates);
        db.ApplicationActions.AddRange(data.ApplicationActions);
        db.ApplicationEnvironments.AddRange(data.ApplicationEnvironments);
        db.Diagrams.AddRange(data.Diagrams);
        db.DiagramScopeItems.AddRange(data.DiagramScopeItems);
        db.DiagramNodes.AddRange(data.DiagramNodes);
        db.DiagramEdges.AddRange(data.DiagramEdges);
        db.Translations.AddRange(data.Translations);
        db.Set<ApplicationProject>().AddRange(data.ApplicationProjects);
        await db.SaveChangesAsync(ct);
    }

    private static void Validate(InventoryData data)
    {
        if (data.Providers.Select(x => x.Id).Distinct().Count() != data.Providers.Count ||
            data.EnvironmentDefinitions.Select(x => x.Id).Distinct().Count() !=
            data.EnvironmentDefinitions.Count ||
            data.Servers.Select(x => x.Id).Distinct().Count() != data.Servers.Count ||
            data.Applications.Select(x => x.Id).Distinct().Count() != data.Applications.Count ||
            data.Projects.Select(x => x.Id).Distinct().Count() != data.Projects.Count)
            throw Guard.Unprocessable("ID duplicati nel file di import.");
        if (data.Providers.Any(x => x.Id <= 0 || string.IsNullOrWhiteSpace(x.Name)))
            throw Guard.Unprocessable("Provider non validi nel file di import.");
        if (data.Servers.Any(x => x.Id <= 0 || string.IsNullOrWhiteSpace(x.Name)))
            throw Guard.Unprocessable("Server non validi nel file di import.");
        if (data.ServerApplications.Any(x =>
                !data.Servers.Any(s => s.Id == x.ServerId) ||
                !data.ApplicationVersions.Any(v => v.Id == x.ApplicationVersionId)))
            throw Guard.Unprocessable("Deployment con riferimenti mancanti nel file di import.");
    }

    private static int Counts(InventoryData data, IReadOnlyList<CredentialRef> refs) =>
        data.Providers.Count + data.EnvironmentDefinitions.Count +
        data.ProviderEnvironments.Count + data.Servers.Count +
        data.Applications.Count + data.ApplicationVersions.Count +
        data.ServerApplications.Count + data.Projects.Count +
        data.ProxyConnections.Count + data.ProxyRoutes.Count +
        data.TunnelServices.Count + data.Diagrams.Count + refs.Count;

    private static async Task<int> InventoryCountAsync(
        InfraOrgItDbContext db,
        CancellationToken ct) =>
        await db.Providers.CountAsync(ct) +
        await db.EnvironmentDefinitions.CountAsync(ct) +
        await db.ProviderEnvironments.CountAsync(ct) +
        await db.Servers.CountAsync(ct) +
        await db.Applications.CountAsync(ct) +
        await db.ApplicationVersions.CountAsync(ct) +
        await db.ApplicationVersionApis.CountAsync(ct) +
        await db.ApplicationApiQueryParams.CountAsync(ct) +
        await db.ServerApplications.CountAsync(ct) +
        await db.Projects.CountAsync(ct) +
        await db.Set<ApplicationProject>().CountAsync(ct) +
        await db.ProxyConnections.CountAsync(ct) +
        await db.ProxyRoutes.CountAsync(ct) +
        await db.TunnelServices.CountAsync(ct) +
        await db.CommandTemplates.CountAsync(ct) +
        await db.ApplicationActions.CountAsync(ct) +
        await db.ApplicationEnvironments.CountAsync(ct) +
        await db.Diagrams.CountAsync(ct) +
        await db.DiagramScopeItems.CountAsync(ct) +
        await db.DiagramNodes.CountAsync(ct) +
        await db.DiagramEdges.CountAsync(ct) +
        await db.Translations.CountAsync(ct);

    private static string Checksum(
        InventoryData data,
        IReadOnlyList<CredentialRef>? credentialRefs)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new { data, credentialRefs = credentialRefs ?? [] }, Json);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    public sealed record ImportConfirmInput(string? Token, string? Mode);
    private sealed record ImportPreview(
        InventoryExportDocument Document,
        string Username,
        DateTimeOffset ExpiresAt);
}

public sealed class InventoryData
{
    public List<Provider> Providers { get; set; } = [];
    public List<EnvironmentDefinition> EnvironmentDefinitions { get; set; } = [];
    public List<ProviderEnvironment> ProviderEnvironments { get; set; } = [];
    public List<Server> Servers { get; set; } = [];
    public List<Application> Applications { get; set; } = [];
    public List<ApplicationVersion> ApplicationVersions { get; set; } = [];
    public List<ApplicationVersionApi> ApplicationVersionApis { get; set; } = [];
    public List<ApplicationApiQueryParam> ApplicationApiQueryParams { get; set; } = [];
    public List<ServerApplication> ServerApplications { get; set; } = [];
    public List<Project> Projects { get; set; } = [];
    public List<ApplicationProject> ApplicationProjects { get; set; } = [];
    public List<ProxyConnection> ProxyConnections { get; set; } = [];
    public List<ProxyRoute> ProxyRoutes { get; set; } = [];
    public List<TunnelService> TunnelServices { get; set; } = [];
    public List<CommandTemplate> CommandTemplates { get; set; } = [];
    public List<ApplicationAction> ApplicationActions { get; set; } = [];
    public List<ApplicationEnvironment> ApplicationEnvironments { get; set; } = [];
    public List<Diagram> Diagrams { get; set; } = [];
    public List<DiagramScopeItem> DiagramScopeItems { get; set; } = [];
    public List<DiagramNode> DiagramNodes { get; set; } = [];
    public List<DiagramEdge> DiagramEdges { get; set; } = [];
    public List<Translation> Translations { get; set; } = [];
}

public sealed record CredentialRef(
    int Id,
    string Name,
    string Username,
    string AuthType,
    string Owner);

public sealed record ExportSource(string Mode, string Name);

public sealed record InventoryExportDocument(
    string Format,
    int FormatVersion,
    string ProductVersion,
    DateTimeOffset ExportedAt,
    ExportSource Source,
    int Counts,
    string Checksum,
    InventoryData Data,
    IReadOnlyList<CredentialRef> CredentialRefs,
    IReadOnlyList<string> Warnings);

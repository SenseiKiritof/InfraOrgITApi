using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.Features.Topology;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace InfraOrgIt.Api.Features.Mcp;

/// <summary>
/// Read-only MCP tools over the inventory and operational history. Every
/// call writes an audit row; action tools live in <see cref="ActionTools"/>.
/// </summary>
[McpServerToolType]
public static class InventoryTools
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "list_servers")]
    [Description("List the servers in the inventory with name, IP, hostname, public IP and environment.")]
    public static async Task<string> ListServers(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Optional text filter on name, IP or hostname")] string? q = null,
        [Description("Maximum rows, 1-200")] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = db.Servers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim();
            query = query.Where(x =>
                x.Name.Contains(text) ||
                x.IpAddress.Contains(text) ||
                x.Hostname.Contains(text));
        }

        var items = await query
            .OrderBy(x => x.Name)
            .Take(limit)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                hostname = x.Hostname,
                ipAddress = x.IpAddress,
                publicIpAddress = x.PublicIpAddress,
                operatingSystem = x.OperatingSystem,
                environment = x.ProviderEnvironment!.Provider!.Name + " / " +
                              x.ProviderEnvironment!.EnvironmentDefinition!.Name
            })
            .ToListAsync(ct);

        await McpToolAudit.OkAsync(audit, user, "list_servers", new { q, limit, count = items.Count }, ct);
        return JsonSerializer.Serialize(new { items }, JsonOptions);
    }

    [McpServerTool(Name = "list_providers")]
    [Description("List the providers with their provider environments (id and name labels).")]
    public static async Task<string> ListProviders(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Optional text filter on provider name")] string? q = null,
        [Description("Maximum providers, 1-200")] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = db.Providers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim();
            query = query.Where(x => x.Name.Contains(text));
        }

        var items = await query
            .OrderBy(x => x.Name)
            .Take(limit)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                environments = x.ProviderEnvironments
                    .OrderBy(pe => pe.Id)
                    .Select(pe => new
                    {
                        id = pe.Id,
                        environmentDefinitionId = pe.EnvironmentDefinitionId,
                        name = pe.EnvironmentDefinition!.Name
                    })
            })
            .ToListAsync(ct);

        await McpToolAudit.OkAsync(audit, user, "list_providers", new { q, limit, count = items.Count }, ct);
        return JsonSerializer.Serialize(new { items }, JsonOptions);
    }

    [McpServerTool(Name = "get_server")]
    [Description("Get one server with sizing, OS, HA node, operator and deployments.")]
    public static async Task<string> GetServer(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Server id")] int id)
    {
        var json = await LoadServerJsonAsync(db, id, ct);
        if (json is null)
        {
            await McpToolAudit.RecordAsync(audit, user, "get_server", "Error", new { id }, "not_found", ct);
            throw new McpException($"Server {id} non trovato.");
        }

        await McpToolAudit.OkAsync(audit, user, "get_server", new { id }, ct);
        return json;
    }

    internal static async Task<string?> LoadServerJsonAsync(
        InfraOrgItDbContext db,
        int id,
        CancellationToken ct)
    {
        var item = await db.Servers.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                hostname = x.Hostname,
                ipAddress = x.IpAddress,
                publicIpAddress = x.PublicIpAddress,
                cpuCores = x.CpuCores,
                memoryGb = x.MemoryGb,
                diskGb = x.DiskGb,
                operatingSystem = x.OperatingSystem,
                haNode = x.HaNode,
                managedBy = x.ManagedBy,
                environment = x.ProviderEnvironment!.Provider!.Name + " / " +
                              x.ProviderEnvironment!.EnvironmentDefinition!.Name,
                deployments = x.ServerApplications.Select(sa => new
                {
                    id = sa.Id,
                    application = sa.ApplicationVersion!.Application!.Name,
                    version = sa.ApplicationVersion.VersionNumber,
                    exposedPort = sa.ExposedPort,
                    runTime = sa.Runtime,
                    containerName = sa.ContainerName,
                    hostPort = sa.HostPort,
                    haRole = sa.HaRole
                })
            })
            .SingleOrDefaultAsync(ct);

        return item is null
            ? null
            : JsonSerializer.Serialize(item, JsonOptions);
    }

    internal static async Task<string?> LoadApplicationJsonAsync(
        InfraOrgItDbContext db,
        int id,
        CancellationToken ct)
    {
        var item = await db.Applications.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                description = x.Description,
                basePath = x.BasePath,
                listeningOnPort = x.ListeningOnPort,
                type = x.Type,
                versions = x.Versions
                    .OrderByDescending(v => v.CreatedAt)
                    .Select(v => new
                    {
                        id = v.Id,
                        versionNumber = v.VersionNumber,
                        createdAt = v.CreatedAt
                    }),
                deployments = x.Versions
                    .SelectMany(v => v.ServerApplications)
                    .Select(sa => new
                    {
                        id = sa.Id,
                        serverId = sa.ServerId,
                        serverName = sa.Server!.Name,
                        version = sa.ApplicationVersion!.VersionNumber,
                        exposedPort = sa.ExposedPort,
                        hostPort = sa.HostPort,
                        containerName = sa.ContainerName
                    })
            })
            .SingleOrDefaultAsync(ct);

        return item is null
            ? null
            : JsonSerializer.Serialize(item, JsonOptions);
    }

    [McpServerTool(Name = "list_applications")]
    [Description("List the application catalog with type, base path, listening port and version count.")]
    public static async Task<string> ListApplications(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Optional text filter on application name")] string? q = null,
        [Description("Maximum rows, 1-200")] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = db.Applications.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim();
            query = query.Where(x => x.Name.Contains(text));
        }

        var items = await query
            .OrderBy(x => x.Name)
            .Take(limit)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                description = x.Description,
                basePath = x.BasePath,
                listeningOnPort = x.ListeningOnPort,
                type = x.Type,
                versionCount = x.Versions.Count
            })
            .ToListAsync(ct);

        await McpToolAudit.OkAsync(audit, user, "list_applications", new { q, limit, count = items.Count }, ct);
        return JsonSerializer.Serialize(new { items }, JsonOptions);
    }

    [McpServerTool(Name = "get_application")]
    [Description("Get one application with its versions and deployments.")]
    public static async Task<string> GetApplication(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Application id")] int id)
    {
        var json = await LoadApplicationJsonAsync(db, id, ct);
        if (json is null)
        {
            await McpToolAudit.RecordAsync(audit, user, "get_application", "Error", new { id }, "not_found", ct);
            throw new McpException($"Applicazione {id} non trovata.");
        }

        await McpToolAudit.OkAsync(audit, user, "get_application", new { id }, ct);
        return json;
    }

    [McpServerTool(Name = "list_projects")]
    [Description("List the projects with their application count.")]
    public static async Task<string> ListProjects(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var items = await db.Projects.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                description = x.Description,
                applicationCount = x.Applications.Count
            })
            .ToListAsync(ct);

        await McpToolAudit.OkAsync(audit, user, "list_projects", new { count = items.Count }, ct);
        return JsonSerializer.Serialize(new { items }, JsonOptions);
    }

    [McpServerTool(Name = "get_project")]
    [Description("Get one project with its applications.")]
    public static async Task<string> GetProject(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Project id")] int id)
    {
        var item = await db.Projects.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                description = x.Description,
                applications = x.Applications
                    .OrderBy(a => a.Name)
                    .Select(a => new
                    {
                        id = a.Id,
                        name = a.Name,
                        description = a.Description,
                        basePath = a.BasePath,
                        listeningOnPort = a.ListeningOnPort,
                        type = a.Type,
                        versionCount = a.Versions.Count
                    })
            })
            .SingleOrDefaultAsync(ct);

        if (item is null)
        {
            await McpToolAudit.RecordAsync(audit, user, "get_project", "Error", new { id }, "not_found", ct);
            throw new McpException($"Progetto {id} non trovato.");
        }

        await McpToolAudit.OkAsync(audit, user, "get_project", new { id }, ct);
        return JsonSerializer.Serialize(item, JsonOptions);
    }

    [McpServerTool(Name = "list_deployments")]
    [Description("List application deployments, optionally filtered by application or server.")]
    public static async Task<string> ListDeployments(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Optional application id")] int? applicationId = null,
        [Description("Optional server id")] int? serverId = null,
        [Description("Maximum rows, 1-200")] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = db.ServerApplications.AsNoTracking();
        if (applicationId is not null)
            query = query.Where(x => x.ApplicationVersion!.ApplicationId == applicationId);
        if (serverId is not null)
            query = query.Where(x => x.ServerId == serverId);

        var items = await query
            .OrderBy(x => x.Id)
            .Take(limit)
            .Select(x => new
            {
                id = x.Id,
                serverId = x.ServerId,
                serverName = x.Server!.Name,
                applicationId = x.ApplicationVersion!.ApplicationId,
                application = x.ApplicationVersion!.Application!.Name,
                version = x.ApplicationVersion.VersionNumber,
                exposedPort = x.ExposedPort,
                runTime = x.Runtime,
                containerImage = x.ContainerImage,
                containerName = x.ContainerName,
                composeDirectory = x.ComposeDirectory,
                hostPort = x.HostPort,
                haRole = x.HaRole
            })
            .ToListAsync(ct);

        await McpToolAudit.OkAsync(audit, user, "list_deployments", new { applicationId, serverId, count = items.Count }, ct);
        return JsonSerializer.Serialize(new { items }, JsonOptions);
    }

    [McpServerTool(Name = "list_reachability_tests")]
    [Description("List the configured reachability tests, optionally filtered by server, with the server name.")]
    public static async Task<string> ListReachabilityTests(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Optional server id")] int? serverId = null,
        [Description("Maximum rows, 1-200")] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 200);
        if (serverId is not null &&
            !await db.Servers.AnyAsync(x => x.Id == serverId, ct))
        {
            await McpToolAudit.RecordAsync(
                audit, user, "list_reachability_tests", "Error",
                new { serverId }, "not_found", ct, serverId);
            throw new McpException($"Server {serverId} non trovato.");
        }

        var query = db.ReachabilityTests.AsNoTracking();
        if (serverId is not null)
            query = query.Where(x => x.ServerId == serverId);

        var items = await query
            .OrderBy(x => x.Hostname)
            .ThenBy(x => x.Id)
            .Take(limit)
            .Select(x => new
            {
                id = x.Id,
                serverId = x.ServerId,
                serverName = x.Server == null ? null : x.Server.Name,
                hostname = x.Hostname,
                port = x.Port,
                description = x.Description
            })
            .ToListAsync(ct);

        await McpToolAudit.OkAsync(
            audit, user, "list_reachability_tests",
            new { serverId, limit, count = items.Count }, ct, serverId);
        return JsonSerializer.Serialize(new { items }, JsonOptions);
    }

    [McpServerTool(Name = "get_topology")]
    [Description("Get the read-only topology model with providers, environments, servers, applications and proxy connections. Narrow it with the filters when the inventory is large.")]
    public static async Task<string> GetTopology(
        TopologyService topology,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Optional provider id")] int? providerId = null,
        [Description("Optional environment definition id")] int? environmentId = null,
        [Description("Optional project id")] int? projectId = null,
        [Description("Maximum servers in the model, 1-1000")] int maxServers = 100)
    {
        maxServers = Math.Clamp(maxServers, 1, 1000);
        var model = await topology.BuildAsync(
            new TopologyFilter(providerId, environmentId, projectId), ct);
        var serverCount = model.Providers
            .SelectMany(p => p.Environments)
            .SelectMany(e => e.Servers)
            .Count();

        var detail = new
        {
            providerId,
            environmentId,
            projectId,
            maxServers,
            servers = serverCount,
            connections = model.ProxyConnections.Count
        };
        if (serverCount > maxServers)
        {
            await McpToolAudit.RecordAsync(
                audit, user, "get_topology", "Error", detail, "too_large", ct);
            throw new McpException(
                $"Topologia troppo grande: {serverCount} server, massimo {maxServers}. Restringi con providerId, environmentId o projectId.");
        }

        await McpToolAudit.OkAsync(audit, user, "get_topology", detail, ct);
        return JsonSerializer.Serialize(model, JsonOptions);
    }

    [McpServerTool(Name = "list_executions")]
    [Description("List recent command executions with their output. Requires SystemAdmin.")]
    public static async Task<string> ListExecutions(
        InfraOrgItDbContext db,
        IAuditService audit,
        EnvironmentAccessService access,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Optional server id")] int? serverId = null,
        [Description("Optional kind: Action, Reachability or Console")] string? kind = null,
        [Description("Maximum rows, 1-100")] int limit = 50)
    {
        try
        {
            await access.RequireSystemAdminAsync(user, ct);
        }
        catch (ApiException)
        {
            await McpToolAudit.RecordAsync(
                audit, user, "list_executions", "Denied",
                new { serverId, kind, limit }, "forbidden", ct, serverId);
            throw new McpException("Operazione riservata a SystemAdmin.");
        }

        limit = Math.Clamp(limit, 1, 100);
        var query = db.CommandExecutions.AsNoTracking();
        if (serverId is not null)
            query = query.Where(x => x.ServerId == serverId);
        if (!string.IsNullOrWhiteSpace(kind))
        {
            var value = kind.Trim();
            query = query.Where(x => x.Kind == value);
        }

        var rows = await query
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .Take(limit)
            .Select(x => new
            {
                id = x.Id,
                username = x.Username,
                serverId = x.ServerId,
                serverName = x.ServerName,
                kind = x.Kind,
                label = x.Label,
                exitCode = x.ExitCode,
                output = x.Output,
                startedAt = x.StartedAt,
                endedAt = x.EndedAt
            })
            .ToListAsync(ct);

        var items = rows
            .Select(x => new
            {
                x.id,
                x.username,
                x.serverId,
                x.serverName,
                x.kind,
                x.label,
                x.exitCode,
                output = x.output.Length > 4000 ? x.output[..4000] : x.output,
                x.startedAt,
                x.endedAt
            })
            .ToList();

        await McpToolAudit.OkAsync(
            audit, user, "list_executions",
            new { serverId, kind, limit, count = items.Count }, ct, serverId);
        return JsonSerializer.Serialize(new { items }, JsonOptions);
    }
}

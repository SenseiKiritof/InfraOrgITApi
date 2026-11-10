using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace InfraOrgIt.Api.Features.Mcp;

/// <summary>
/// Read-only MCP tools over the inventory. Every call writes an audit row;
/// action tools will be added once McpGrants is in place.
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

    [McpServerTool(Name = "get_server")]
    [Description("Get one server with sizing, OS, HA node, operator and deployments.")]
    public static async Task<string> GetServer(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Server id")] int id)
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

        if (item is null)
        {
            await McpToolAudit.RecordAsync(audit, user, "get_server", "Error", new { id }, "not_found", ct);
            throw new McpException($"Server {id} non trovato.");
        }

        await McpToolAudit.OkAsync(audit, user, "get_server", new { id }, ct);
        return JsonSerializer.Serialize(item, JsonOptions);
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

}

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
/// Read-only MCP resources over the inventory. They mirror the read tools
/// so clients that prefer resources get the same JSON; every read is
/// audited.
/// </summary>
[McpServerResourceType]
public static class InventoryResources
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [McpServerResource(
        UriTemplate = "infraorgit://inventory/summary",
        Name = "inventory-summary",
        Title = "Inventory summary",
        MimeType = "application/json")]
    [Description("Counts of servers, applications, deployments and projects.")]
    public static async Task<string> Summary(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var item = new
        {
            servers = await db.Servers.CountAsync(ct),
            applications = await db.Applications.CountAsync(ct),
            deployments = await db.ServerApplications.CountAsync(ct),
            projects = await db.Projects.CountAsync(ct)
        };

        await McpToolAudit.OkAsync(
            audit, user, "read_resource",
            new { uri = "infraorgit://inventory/summary" }, ct);
        return JsonSerializer.Serialize(item, JsonOptions);
    }

    [McpServerResource(
        UriTemplate = "infraorgit://servers/{id}",
        Name = "server",
        Title = "Server detail",
        MimeType = "application/json")]
    [Description("One server with sizing, OS, HA node, operator and deployments.")]
    public static async Task<string> Server(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct,
        int id)
    {
        var json = await InventoryTools.LoadServerJsonAsync(db, id, ct);
        if (json is null)
        {
            await McpToolAudit.RecordAsync(
                audit, user, "read_resource", "Error",
                new { uri = $"infraorgit://servers/{id}" }, "not_found", ct, id);
            throw new McpException($"Server {id} non trovato.");
        }

        await McpToolAudit.OkAsync(
            audit, user, "read_resource",
            new { uri = $"infraorgit://servers/{id}" }, ct, id);
        return json;
    }

    [McpServerResource(
        UriTemplate = "infraorgit://applications/{id}",
        Name = "application",
        Title = "Application detail",
        MimeType = "application/json")]
    [Description("One application with its versions and deployments.")]
    public static async Task<string> Application(
        InfraOrgItDbContext db,
        IAuditService audit,
        ClaimsPrincipal user,
        CancellationToken ct,
        int id)
    {
        var json = await InventoryTools.LoadApplicationJsonAsync(db, id, ct);
        if (json is null)
        {
            await McpToolAudit.RecordAsync(
                audit, user, "read_resource", "Error",
                new { uri = $"infraorgit://applications/{id}" }, "not_found", ct);
            throw new McpException($"Applicazione {id} non trovata.");
        }

        await McpToolAudit.OkAsync(
            audit, user, "read_resource",
            new { uri = $"infraorgit://applications/{id}" }, ct);
        return json;
    }
}

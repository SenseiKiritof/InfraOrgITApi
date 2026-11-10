using System.ComponentModel;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.Features.Remote;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;

namespace InfraOrgIt.Api.Features.Mcp;

/// <summary>
/// MCP action tools. Each call needs the mcp:operate scope, an McpGrant for
/// the user, and the normal ServerOperator/ServerAdmin right on the
/// environment; every attempt is audited, including denials.
/// </summary>
[ModelContextProtocol.Server.McpServerToolType]
public static class ActionTools
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [ModelContextProtocol.Server.McpServerTool(
        Name = "run_reachability_tests")]
    [Description("Run the reachability tests of a server and return the OK/KO results. Requires mcp:operate, an MCP grant and confirm=true.")]
    public static async Task<string> RunReachabilityTests(
        InfraOrgItDbContext db,
        IAuditService audit,
        McpGrantService grants,
        EnvironmentAccessService access,
        ReachabilityRunner runner,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Server id")] int serverId,
        [Description("Pass true to confirm the action")] bool confirm,
        [Description("Optional test ids; omit for all tests")] int[]? testIds = null)
    {
        var started = Stopwatch.GetTimestamp();
        if (!McpToolAudit.HasScope(user, "mcp:operate"))
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_reachability_tests", "Denied",
                new { serverId }, "missing_scope", ct, serverId);
            throw new McpException("Serve lo scope mcp:operate.");
        }

        if (!confirm)
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_reachability_tests", "Error",
                new { serverId }, "confirmation_required", ct, serverId);
            throw new McpException("Passa confirm=true per eseguire i test.");
        }

        var server = await db.Servers
            .Include(x => x.ProviderEnvironment)
            .Include(x => x.Credential)
            .Include(x => x.ReachabilityTests)
            .SingleOrDefaultAsync(x => x.Id == serverId, ct);
        if (server is null)
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_reachability_tests", "Error",
                new { serverId }, "not_found", ct, serverId);
            throw new McpException($"Server {serverId} non trovato.");
        }

        if (!await grants.HasAsync(user, "reachability.run",
                server.ProviderEnvironmentId, ct))
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_reachability_tests", "Denied",
                new { serverId }, "missing_grant", ct, serverId);
            throw new McpException(
                "Nessun grant MCP per reachability.run su questo ambiente.");
        }

        var environmentAccess = await access.GetAsync(user, ct);
        if (!environmentAccess.OperatorEnvironmentIds.Contains(
                server.ProviderEnvironmentId))
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_reachability_tests", "Denied",
                new { serverId }, "forbidden", ct, serverId);
            throw new McpException(
                "Serve ServerOperator o ServerAdmin sull'ambiente del server.");
        }

        var results = await runner.RunAsync(user, [server], testIds, ct);
        await McpToolAudit.RecordAsync(
            audit, user, "run_reachability_tests", "Ok",
            new
            {
                serverId,
                count = results.Count,
                reachable = results.Count(x => x.Reachable)
            },
            null, ct, serverId);

        return JsonSerializer.Serialize(new
        {
            serverId,
            ok = results.Count > 0 && results.All(x => x.Reachable),
            results,
            durationMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds
        }, JsonOptions);
    }
}

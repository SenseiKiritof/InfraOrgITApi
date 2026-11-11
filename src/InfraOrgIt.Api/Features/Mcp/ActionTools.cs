using System.ComponentModel;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.Features.Remote;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
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
        Name = "run_application_action")]
    [Description("Run a configured application action on a deployment and return its exit code and output. Requires mcp:operate, an MCP grant, confirm=true and ServerOperator/ServerAdmin.")]
    public static async Task<string> RunApplicationAction(
        InfraOrgItDbContext db,
        IAuditService audit,
        McpGrantService grants,
        EnvironmentAccessService access,
        ApplicationActionRunner runner,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Server application deployment id")] int serverApplicationId,
        [Description("Application action id")] int actionId,
        [Description("Pass true to confirm the action")] bool confirm,
        [Description("Optional idempotency key")] string? idempotencyKey = null)
    {
        if (!McpToolAudit.HasScope(user, "mcp:operate"))
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_application_action", "Denied",
                new { serverApplicationId, actionId }, "missing_scope", ct);
            throw new McpException("Serve lo scope mcp:operate.");
        }

        if (!confirm)
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_application_action", "Error",
                new { serverApplicationId, actionId }, "confirmation_required", ct);
            throw new McpException("Passa confirm=true per eseguire l'azione.");
        }

        ServerApplication deploy;
        try
        {
            deploy = await ApplicationActionRunner.LoadDeployAsync(
                db, serverApplicationId, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_application_action", "Error",
                new { serverApplicationId, actionId }, "not_found", ct);
            throw new McpException($"Deployment {serverApplicationId} non trovato.");
        }

        var server = deploy.Server!;
        if (!await grants.HasAsync(user, "actions.run",
                server.ProviderEnvironmentId, ct))
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_application_action", "Denied",
                new { serverApplicationId, actionId }, "missing_grant", ct,
                server.Id);
            throw new McpException(
                "Nessun grant MCP per actions.run su questo ambiente.");
        }

        var environmentAccess = await access.GetAsync(user, ct);
        if (!environmentAccess.OperatorEnvironmentIds.Contains(
                server.ProviderEnvironmentId))
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_application_action", "Denied",
                new { serverApplicationId, actionId }, "forbidden", ct,
                server.Id);
            throw new McpException(
                "Serve ServerOperator o ServerAdmin sull'ambiente del server.");
        }

        var result = await runner.RunAsync(
            user, serverApplicationId, actionId, idempotencyKey, confirm: true, ct);
        await McpToolAudit.RecordAsync(
            audit, user, "run_application_action", "Ok",
            new { serverApplicationId, actionId, result.ExitCode },
            null, ct, server.Id);

        var output = result.Output.Length > 4000
            ? result.Output[..4000]
            : result.Output;
        return JsonSerializer.Serialize(new
        {
            exitCode = result.ExitCode,
            timedOut = result.TimedOut,
            output
        }, JsonOptions);
    }

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

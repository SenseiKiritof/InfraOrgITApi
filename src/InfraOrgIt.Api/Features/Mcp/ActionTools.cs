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
/// MCP action tools. Each call needs the mcp:operate scope, an approved MCP
/// agent with a matching permission (or a legacy user/group grant) and the
/// normal ServerOperator/ServerAdmin right on the environment; every attempt
/// is audited, including denials. confirm=true is still required, but there is
/// no per-call human approval: the agent is approved once with individual
/// permissions.
/// </summary>
[ModelContextProtocol.Server.McpServerToolType]
public static class ActionTools
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [ModelContextProtocol.Server.McpServerTool(
        Name = "run_application_action")]
    [Description("Run a configured application action on a deployment. Requires mcp:operate, an approved MCP agent with the actions.run permission, confirm=true and ServerOperator/ServerAdmin.")]
    public static async Task<string> RunApplicationAction(
        InfraOrgItDbContext db,
        IAuditService audit,
        McpGrantService grants,
        McpAgentService agents,
        EnvironmentAccessService access,
        ApplicationActionRunner runner,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Server application deployment id")] int serverApplicationId,
        [Description("Application action id")] int actionId,
        [Description("Pass true to confirm the request")] bool confirm,
        [Description("Optional idempotency key")] string? idempotencyKey = null)
    {
        if (!McpToolAudit.HasScope(user, "mcp:operate"))
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_application_action", "Denied",
                new { serverApplicationId, actionId }, "missing_scope", ct,
                targetType: "ServerApplication", targetId: serverApplicationId);
            throw new McpException("Serve lo scope mcp:operate.");
        }

        if (!confirm)
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_application_action", "Error",
                new { serverApplicationId, actionId }, "confirmation_required", ct,
                targetType: "ServerApplication", targetId: serverApplicationId);
            throw new McpException("Passa confirm=true per eseguire l'azione.");
        }

        var agent = await agents.ResolveAsync(user, ct);
        if (agent.Status != McpAgentStatuses.Approved)
            return await AgentRequiredAsync(
                audit, user, agent, "run_application_action",
                new { serverApplicationId, actionId }, ct,
                targetType: "ServerApplication", targetId: serverApplicationId);

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
                new { serverApplicationId, actionId }, "not_found", ct,
                targetType: "ServerApplication", targetId: serverApplicationId);
            throw new McpException($"Deployment {serverApplicationId} non trovato.");
        }

        var server = deploy.Server!;
        var environmentDefinitionId = await db.ProviderEnvironments
            .Where(x => x.Id == server.ProviderEnvironmentId)
            .Select(x => x.EnvironmentDefinitionId)
            .SingleAsync(ct);

        if (!await agents.HasPermissionAsync(
                agent.Id, "actions.run", environmentDefinitionId, ct) &&
            !await grants.HasAsync(
                user, "actions.run", server.ProviderEnvironmentId, ct))
        {
            var request = await agents.RequestPermissionAsync(
                agent.Id, "actions.run", environmentDefinitionId, null, ct);
            return await PermissionRequiredAsync(
                audit, user, agent, request, "run_application_action",
                new { serverApplicationId, actionId }, ct, server.Id,
                "ServerApplication", serverApplicationId);
        }

        var environmentAccess = await access.GetAsync(user, ct);
        if (!environmentAccess.OperatorEnvironmentIds.Contains(
                server.ProviderEnvironmentId))
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_application_action", "Denied",
                new { serverApplicationId, actionId }, "forbidden", ct,
                server.Id, "ServerApplication", serverApplicationId);
            throw new McpException(
                "Serve ServerOperator o ServerAdmin sull'ambiente del server.");
        }

        var result = await runner.RunAsync(
            user, serverApplicationId, actionId, idempotencyKey, confirm: true, ct);
        await McpToolAudit.RecordAsync(
            audit, user, "run_application_action", "Ok",
            new { serverApplicationId, actionId, result.ExitCode },
            null, ct, server.Id, "ServerApplication", serverApplicationId);

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
    [Description("Run the reachability tests of a server. Requires mcp:operate, an approved MCP agent with the reachability.run permission, confirm=true and ServerOperator/ServerAdmin.")]
    public static async Task<string> RunReachabilityTests(
        InfraOrgItDbContext db,
        IAuditService audit,
        McpGrantService grants,
        McpAgentService agents,
        EnvironmentAccessService access,
        ReachabilityRunner runner,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Server id")] int serverId,
        [Description("Pass true to confirm the request")] bool confirm,
        [Description("Optional test ids; omit for all tests")] int[]? testIds = null)
    {
        var started = Stopwatch.GetTimestamp();
        if (!McpToolAudit.HasScope(user, "mcp:operate"))
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_reachability_tests", "Denied",
                new { serverId }, "missing_scope", ct, serverId,
                "Server", serverId);
            throw new McpException("Serve lo scope mcp:operate.");
        }

        if (!confirm)
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_reachability_tests", "Error",
                new { serverId }, "confirmation_required", ct, serverId,
                "Server", serverId);
            throw new McpException("Passa confirm=true per eseguire i test.");
        }

        var agent = await agents.ResolveAsync(user, ct);
        if (agent.Status != McpAgentStatuses.Approved)
            return await AgentRequiredAsync(
                audit, user, agent, "run_reachability_tests",
                new { serverId }, ct, serverId, "Server", serverId);

        var server = await db.Servers
            .Include(x => x.ProviderEnvironment)
            .Include(x => x.Credential)
            .Include(x => x.ReachabilityTests)
            .SingleOrDefaultAsync(x => x.Id == serverId, ct);
        if (server is null)
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_reachability_tests", "Error",
                new { serverId }, "not_found", ct, serverId,
                "Server", serverId);
            throw new McpException($"Server {serverId} non trovato.");
        }

        var environmentDefinitionId = server.ProviderEnvironment!.EnvironmentDefinitionId;
        if (!await agents.HasPermissionAsync(
                agent.Id, "reachability.run", environmentDefinitionId, ct) &&
            !await grants.HasAsync(
                user, "reachability.run", server.ProviderEnvironmentId, ct))
        {
            var request = await agents.RequestPermissionAsync(
                agent.Id, "reachability.run", environmentDefinitionId, null, ct);
            return await PermissionRequiredAsync(
                audit, user, agent, request, "run_reachability_tests",
                new { serverId }, ct, serverId, "Server", serverId);
        }

        var environmentAccess = await access.GetAsync(user, ct);
        if (!environmentAccess.OperatorEnvironmentIds.Contains(
                server.ProviderEnvironmentId))
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_reachability_tests", "Denied",
                new { serverId }, "forbidden", ct, serverId,
                "Server", serverId);
            throw new McpException(
                "Serve ServerOperator o ServerAdmin sull'ambiente del server.");
        }

        var normalizedTestIds = testIds?.Distinct().Order().ToArray();
        if (normalizedTestIds is { Length: > 200 })
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_reachability_tests", "Error",
                new { serverId, count = normalizedTestIds.Length },
                "too_many_test_ids", ct, serverId, "Server", serverId);
            throw new McpException("Massimo 200 test id per richiesta.");
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
            null, ct, serverId, "Server", serverId);

        return JsonSerializer.Serialize(new
        {
            serverId,
            ok = results.Count > 0 && results.All(x => x.Reachable),
            results,
            durationMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds
        }, JsonOptions);
    }

    private static async Task<string> AgentRequiredAsync(
        IAuditService audit,
        ClaimsPrincipal user,
        McpAgent agent,
        string action,
        object detail,
        CancellationToken ct,
        int? serverId = null,
        string? targetType = null,
        int? targetId = null)
    {
        var errorCode = agent.Status == McpAgentStatuses.Denied
            ? "agent_denied"
            : "agent_pending";
        await McpToolAudit.RecordAsync(
            audit, user, action, "Denied",
            new { agentId = agent.Id, agent.Status, detail },
            errorCode, ct, serverId, targetType, targetId);

        return JsonSerializer.Serialize(new
        {
            status = "agent_approval_required",
            agentId = agent.Id,
            agentStatus = agent.Status,
            message = agent.Status == McpAgentStatuses.Denied
                ? "L'agente MCP è stato negato dal proprietario."
                : "Agente MCP in attesa di approvazione: il proprietario deve approvarlo dalla UI web."
        }, JsonOptions);
    }

    private static async Task<string> PermissionRequiredAsync(
        IAuditService audit,
        ClaimsPrincipal user,
        McpAgent agent,
        McpPermissionRequest request,
        string action,
        object detail,
        CancellationToken ct,
        int? serverId = null,
        string? targetType = null,
        int? targetId = null)
    {
        await McpToolAudit.RecordAsync(
            audit, user, action, "Denied",
            new { agentId = agent.Id, permissionRequestId = request.Id, detail },
            "permission_required", ct, serverId, targetType, targetId);

        return JsonSerializer.Serialize(new
        {
            status = "permission_required",
            agentId = agent.Id,
            permissionRequestId = request.Id,
            permissionAction = request.Action,
            environmentDefinitionId = request.EnvironmentDefinitionId,
            message = "Permesso mancante: il proprietario deve approvare la richiesta dalla UI web."
        }, JsonOptions);
    }
}

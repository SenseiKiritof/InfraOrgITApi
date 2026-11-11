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
/// environment; every attempt is audited, including denials. confirm=true is
/// not an execution permission: it only opens a Pending approval that the
/// user must approve from the cookie-authenticated web UI (PT-18).
/// </summary>
[ModelContextProtocol.Server.McpServerToolType]
public static class ActionTools
{
    private const string ApprovalRequiredMessage =
        "Richiesta inviata: un umano deve approvarla dalla UI web prima dell'esecuzione.";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [ModelContextProtocol.Server.McpServerTool(
        Name = "run_application_action")]
    [Description("Request or run a configured application action on a deployment. With no approvalId it creates a Pending human approval and executes nothing; with an approved approvalId it runs once. Requires mcp:operate, an MCP grant, confirm=true and ServerOperator/ServerAdmin.")]
    public static async Task<string> RunApplicationAction(
        InfraOrgItDbContext db,
        IAuditService audit,
        McpGrantService grants,
        McpApprovalService approvals,
        EnvironmentAccessService access,
        ApplicationActionRunner runner,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Server application deployment id")] int serverApplicationId,
        [Description("Application action id")] int actionId,
        [Description("Pass true to confirm the request")] bool confirm,
        [Description("Optional idempotency key")] string? idempotencyKey = null,
        [Description("Approval id returned by a previous request; required to execute")] int? approvalId = null)
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
            throw new McpException("Passa confirm=true per richiedere l'azione.");
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

        var parametersJson = McpApprovalService.SerializeParameters(new
        {
            serverApplicationId,
            actionId,
            idempotencyKey
        });
        var detail = new
        {
            serverApplicationId,
            actionId,
            idempotencyKey
        };

        if (approvalId is null)
            return await RequestApprovalAsync(
                audit, approvals, user,
                "run_application_action", server.Id,
                serverApplicationId, actionId, parametersJson, detail, ct);

        await ConsumeApprovalAsync(
            audit, approvals, user, approvalId.Value,
            "run_application_action", server.Id,
            serverApplicationId, actionId, parametersJson, detail, ct);

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
    [Description("Request or run the reachability tests of a server. With no approvalId it creates a Pending human approval and executes nothing; with an approved approvalId it runs once. Requires mcp:operate, an MCP grant, confirm=true and ServerOperator/ServerAdmin.")]
    public static async Task<string> RunReachabilityTests(
        InfraOrgItDbContext db,
        IAuditService audit,
        McpGrantService grants,
        McpApprovalService approvals,
        EnvironmentAccessService access,
        ReachabilityRunner runner,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Server id")] int serverId,
        [Description("Pass true to confirm the request")] bool confirm,
        [Description("Optional test ids; omit for all tests")] int[]? testIds = null,
        [Description("Approval id returned by a previous request; required to execute")] int? approvalId = null)
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
            throw new McpException("Passa confirm=true per richiedere i test.");
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

        var normalizedTestIds = testIds?.Distinct().Order().ToArray();
        if (normalizedTestIds is { Length: > 200 })
        {
            await McpToolAudit.RecordAsync(
                audit, user, "run_reachability_tests", "Error",
                new { serverId, count = normalizedTestIds.Length },
                "too_many_test_ids", ct, serverId);
            throw new McpException("Massimo 200 test id per richiesta.");
        }

        var parametersJson = McpApprovalService.SerializeParameters(new
        {
            serverId,
            testIds = normalizedTestIds
        });
        var detail = new { serverId, testIds = normalizedTestIds };

        if (approvalId is null)
            return await RequestApprovalAsync(
                audit, approvals, user,
                "run_reachability_tests", server.Id,
                null, null, parametersJson, detail, ct);

        await ConsumeApprovalAsync(
            audit, approvals, user, approvalId.Value,
            "run_reachability_tests", server.Id,
            null, null, parametersJson, detail, ct);

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

    private static async Task<string> RequestApprovalAsync(
        IAuditService audit,
        McpApprovalService approvals,
        ClaimsPrincipal user,
        string action,
        int serverId,
        int? serverApplicationId,
        int? applicationActionId,
        string parametersJson,
        object detail,
        CancellationToken ct)
    {
        var ownerId = await approvals.UserIdAsync(user, ct)
                      ?? throw new McpException("Utente non provisionato.");
        var approval = await approvals.RequestAsync(
            ownerId, action, serverId, serverApplicationId,
            applicationActionId, parametersJson, ct);

        await audit.RecordAsync(AuditEntries.Change(
            user,
            "mcp_approval_request",
            "McpApproval",
            approval.Id,
            serverId,
            new
            {
                approval.Action,
                approval.ServerApplicationId,
                approval.ApplicationActionId,
                approval.ParametersJson
            }), ct);
        await McpToolAudit.RecordAsync(
            audit, user, action, "Error",
            new { approvalId = approval.Id, detail },
            "approval_required", ct, serverId);

        return JsonSerializer.Serialize(new
        {
            status = "approval_required",
            approvalId = approval.Id,
            expiresAt = approval.ExpiresAt,
            message = ApprovalRequiredMessage
        }, JsonOptions);
    }

    /// <summary>
    /// Validates and consumes a human approval for this exact action, server,
    /// target and parameters. Throws an McpException and audits a Denied row
    /// for a missing, foreign, mismatched, denied, expired or reused approval.
    /// </summary>
    private static async Task ConsumeApprovalAsync(
        IAuditService audit,
        McpApprovalService approvals,
        ClaimsPrincipal user,
        int approvalId,
        string action,
        int serverId,
        int? serverApplicationId,
        int? applicationActionId,
        string parametersJson,
        object detail,
        CancellationToken ct)
    {
        var ownerId = await approvals.UserIdAsync(user, ct);
        var approval = ownerId is null
            ? null
            : await approvals.FindAsync(ownerId.Value, approvalId, ct);
        if (approval is null)
        {
            await DenyAsync(audit, user, action, approvalId, detail,
                "approval_not_found", serverId, ct);
            throw new McpException(
                $"Approvazione {approvalId} non trovata per l'utente corrente.");
        }

        if (approval.Action != action ||
            approval.ServerId != serverId ||
            approval.ServerApplicationId != serverApplicationId ||
            approval.ApplicationActionId != applicationActionId ||
            approval.ParametersJson != parametersJson)
        {
            await DenyAsync(audit, user, action, approvalId, detail,
                "approval_mismatch", serverId, ct);
            throw new McpException(
                "L'approvazione non corrisponde ad azione, server, destinazione o parametri richiesti.");
        }

        var now = DateTime.UtcNow;
        if (approval.ExpiresAt <= now &&
            approval.Status is McpApprovalStatuses.Pending
                or McpApprovalStatuses.Approved)
        {
            await DenyAsync(audit, user, action, approvalId, detail,
                "approval_expired", serverId, ct);
            throw new McpException(
                "Approvazione scaduta: richiedine una nuova.");
        }

        if (approval.Status != McpApprovalStatuses.Approved)
        {
            await DenyAsync(audit, user, action, approvalId, detail,
                "approval_" + approval.Status.ToLowerInvariant(), serverId, ct);
            throw new McpException(approval.Status switch
            {
                McpApprovalStatuses.Pending =>
                    "Approvazione ancora in attesa di un umano.",
                McpApprovalStatuses.Denied => "Approvazione negata da un umano.",
                McpApprovalStatuses.Used => "Approvazione già usata: richiedine una nuova.",
                McpApprovalStatuses.Expired => "Approvazione scaduta: richiedine una nuova.",
                _ => $"Approvazione non eseguibile: stato {approval.Status}."
            });
        }

        if (!await approvals.TryMarkUsedAsync(ownerId!.Value, approvalId, now, ct))
        {
            var current = await approvals.FindAsync(ownerId.Value, approvalId, ct);
            await DenyAsync(audit, user, action, approvalId, detail,
                "approval_not_consumable", serverId, ct);
            throw new McpException(
                $"Approvazione non più utilizzabile (stato {current?.Status ?? "sconosciuto"}).");
        }
    }

    private static Task DenyAsync(
        IAuditService audit,
        ClaimsPrincipal user,
        string action,
        int approvalId,
        object detail,
        string errorCode,
        int serverId,
        CancellationToken ct) =>
        McpToolAudit.RecordAsync(
            audit, user, action, "Denied",
            new { approvalId, detail }, errorCode, ct, serverId);
}

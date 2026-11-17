using System.Security.Claims;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Remote;

/// <summary>
/// Runs one configured application action on a deployment. Shared by the
/// HTTP endpoint, the MCP action tool and the desktop-native path; the same
/// script, audit row, idempotency and redaction apply.
/// </summary>
public sealed class ApplicationActionRunner(
    ISshConnector ssh,
    IOptions<ServerAccessOptions> options,
    EnvironmentAccessService access,
    InfraOrgItDbContext db)
{
    public sealed record PreparedAction(
        CommandExecution Execution,
        Server Server,
        string Script,
        string Label,
        IReadOnlyDictionary<string, string> CustomValues);

    public async Task<CommandResultDto> RunAsync(
        ClaimsPrincipal user,
        int deploymentId,
        int actionId,
        string? idempotencyKey,
        bool confirm,
        CancellationToken ct)
    {
        var prepared = await PrepareAsync(
            user, deploymentId, actionId, idempotencyKey, confirm, ct);
        try
        {
            await using var session = await ssh.ConnectAsync(prepared.Server, ct);
            await db.SaveChangesAsync(ct);

            var result = await session.RunAsync(
                prepared.Script,
                TimeSpan.FromMinutes(options.Value.CommandTimeoutMinutes),
                ct);

            CommandAudit.Complete(
                prepared.Execution,
                result,
                options.Value,
                prepared.CustomValues);
            await db.SaveChangesAsync(ct);
            return new CommandResultDto(
                result.ExitCode, result.Output, result.TimedOut);
        }
        catch (Exception exception)
        {
            CommandAudit.Complete(
                prepared.Execution,
                new SshCommandResult(null, exception.Message),
                options.Value,
                prepared.CustomValues);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Authorizes and renders the action, checks confirmation and idempotency,
    /// then persists a pending CommandExecution. The desktop executes the
    /// returned script with the user's SSH agent and reports the result back.
    /// </summary>
    public async Task<PreparedAction> PrepareAsync(
        ClaimsPrincipal user,
        int deploymentId,
        int actionId,
        string? idempotencyKey,
        bool confirm,
        CancellationToken ct)
    {
        var deploy = await LoadDeployAsync(db, deploymentId, ct);
        var server = deploy.Server!;
        await access.RequireOperatorAsync(
            user, [server.ProviderEnvironmentId], ct);

        var application = deploy.ApplicationVersion!.Application!;
        var action = await db.ApplicationActions.AsNoTracking()
                         .Include(x => x.CommandTemplate)
                         .SingleOrDefaultAsync(
                             x => x.Id == actionId &&
                                  x.ApplicationId == application.Id, ct)
                     ?? throw Guard.NotFound("Azione", actionId);

        // PT-13: RequiresConfirmation is a server-side guard, not only a UI
        // flag. A scripted client must send confirm=true for destructive
        // actions; nothing is executed before this check.
        if (action.RequiresConfirmation && !confirm)
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "confirmation_required",
                $"L'azione {action.Label} richiede una conferma esplicita.");

        var script = action.CommandTemplate?.Script ?? action.CustomScript ?? "";
        var custom = ParseParameterValues(action.ParameterValuesJson);
        CommandRenderer.CheckValues(script, custom);

        var builtIns = new Dictionary<string, string>
        {
            ["ServerName"] = server.Name,
            ["Hostname"] = server.Hostname,
            ["IpAddress"] = server.IpAddress,
            ["ApplicationName"] = application.Name,
            ["BasePath"] = application.BasePath,
            ["Version"] = deploy.ApplicationVersion.VersionNumber,
            ["ExposedPort"] = deploy.ExposedPort.ToString()
        };
        CommandRenderer.CheckValues(script, builtIns);

        var values = new Dictionary<string, string>(custom);
        foreach (var pair in builtIns)
            values[pair.Key] = pair.Value;
        var rendered = CommandRenderer.Render(script, values);

        if (idempotencyKey is { Length: > 100 })
            throw Guard.Unprocessable("Idempotency-Key supera 100 caratteri.");
        if (!string.IsNullOrEmpty(idempotencyKey) &&
            await db.CommandExecutions.AnyAsync(
                x => x.IdempotencyKey == idempotencyKey, ct))
            throw Guard.Conflict("Idempotency-Key già usata.");

        var username = user.Identity?.Name ?? "";
        var label = $"{application.Name}: {action.Label}";
        var execution = CommandAudit.Start(
            db, username, server, "Action", label, script, DateTime.UtcNow,
            string.IsNullOrEmpty(idempotencyKey) ? null : idempotencyKey);
        db.CommandExecutions.Add(execution);
        await db.SaveChangesAsync(ct);

        return new PreparedAction(
            execution, server, rendered, label, custom);
    }

    /// <summary>
    /// Completes an execution created by <see cref="PrepareAsync"/>. The
    /// desktop already redacts its output with the values returned by the
    /// intent endpoint; ownership and terminal state are re-checked here.
    /// </summary>
    public async Task CompleteClientAsync(
        ClaimsPrincipal user,
        int executionId,
        CommandResultDto result,
        CancellationToken ct)
    {
        var username = user.Identity?.Name ?? "";
        var execution = await db.CommandExecutions
                            .SingleOrDefaultAsync(
                                x => x.Id == executionId &&
                                     x.Username == username, ct)
                        ?? throw Guard.NotFound("Esecuzione", executionId);
        if (execution.Kind != "Action")
            throw Guard.Unprocessable("L'esecuzione non è un'azione.");
        if (execution.EndedAt is not null)
            throw Guard.Conflict("L'esecuzione è già conclusa.");

        CommandAudit.Complete(
            execution,
            new SshCommandResult(
                result.ExitCode, result.Output, result.TimedOut),
            options.Value);
        await db.SaveChangesAsync(ct);
    }

    internal static async Task<ServerApplication> LoadDeployAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.ServerApplications
            .Include(x => x.Server)!.ThenInclude(x => x!.ProviderEnvironment)
            .Include(x => x.Server)!.ThenInclude(x => x!.Credential)
            .Include(x => x.ApplicationVersion)!.ThenInclude(x => x!.Application)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw Guard.NotFound("Applicazione deployata", id);

    internal static Dictionary<string, string> ParseParameterValues(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

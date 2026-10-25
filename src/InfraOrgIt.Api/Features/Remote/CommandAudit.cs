using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;

namespace InfraOrgIt.Api.Features.Remote;

public static class CommandAudit
{
    public static CommandExecution Add(
        InfraOrgItDbContext db,
        string username,
        Server server,
        string kind,
        string label,
        string script,
        DateTime startedAt,
        SshCommandResult? result,
        ServerAccessOptions options,
        string? idempotencyKey = null)
    {
        var output = result?.Output ?? "";
        var execution = new CommandExecution
        {
            Username = username,
            ServerId = server.Id,
            ServerName = server.Name,
            Kind = kind,
            Label = label,
            Script = script,
            ExitCode = result?.ExitCode,
            Output = output.Length > options.MaxAuditOutputBytes
                ? output[^options.MaxAuditOutputBytes..]
                : output,
            StartedAt = startedAt,
            EndedAt = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey
        };
        db.CommandExecutions.Add(execution);
        return execution;
    }

    public static CommandExecution Start(
        InfraOrgItDbContext db,
        string username,
        Server server,
        string kind,
        string label,
        string script,
        DateTime startedAt,
        string? idempotencyKey = null) =>
        new()
        {
            Username = username,
            ServerId = server.Id,
            ServerName = server.Name,
            Kind = kind,
            Label = label,
            Script = script,
            ExitCode = null,
            Output = "",
            StartedAt = startedAt,
            EndedAt = null,
            IdempotencyKey = idempotencyKey
        };

    public static void Complete(
        CommandExecution execution,
        SshCommandResult result,
        ServerAccessOptions options,
        IReadOnlyDictionary<string, string>? redactValues = null)
    {
        var output = redactValues is null
            ? result.Output
            : Redact(result.Output, redactValues);
        execution.ExitCode = result.ExitCode;
        execution.Output = output.Length > options.MaxAuditOutputBytes
            ? output[^options.MaxAuditOutputBytes..]
            : output;
        execution.EndedAt = DateTime.UtcNow;
    }

    public static string Redact(
        string output,
        IReadOnlyDictionary<string, string> values)
    {
        foreach (var value in values.Values
                     .Where(value => !string.IsNullOrEmpty(value))
                     .Distinct()
                     .OrderByDescending(value => value.Length))
            output = output.Replace(value, "***", StringComparison.Ordinal);
        return output;
    }
}

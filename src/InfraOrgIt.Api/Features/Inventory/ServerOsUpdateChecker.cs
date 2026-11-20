using System.Text.RegularExpressions;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;

namespace InfraOrgIt.Api.Features.Inventory;

public sealed record ServerOsUpdateResult(
    string? OsName,
    string? OsVersion,
    string? Target,
    bool? TargetLts,
    DateOnly? EolDate,
    string Status,
    string? Detail,
    DateTime CheckedAt);

/// <summary>
/// Reads the OS release over SSH and resolves the major upgrade target and
/// EOL date. The server is never modified; all commands are read-only.
/// </summary>
public sealed partial class ServerOsUpdateChecker(
    ISshConnector ssh,
    IEndOfLifeCatalog catalog,
    ILogger<ServerOsUpdateChecker> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private const string Script =
        "cat /etc/os-release; echo '__INFRAORGIT_OS__'; "
        + "(sudo -n do-release-upgrade -c 2>&1 || "
        + "/usr/lib/ubuntu-release-upgrader/check-new-release -q 2>&1 || true)";

    public async Task<ServerOsUpdateResult> CheckAsync(
        Server server, bool ltsOnly, CancellationToken ct)
    {
        var checkedAt = DateTime.UtcNow;
        try
        {
            await using var session = await ssh.ConnectAsync(server, ct);
            var result = await session.RunAsync(Script, Timeout, ct);
            if (result.TimedOut)
                return Error(checkedAt, "Timeout durante il controllo.");
            var split = result.Output.Split("__INFRAORGIT_OS__", 2);
            var info = OsReleaseInfo.Parse(split[0]);
            if (info is null)
                return Unsupported(checkedAt, "Distro non riconosciuta.");
            var product = EndOfLifeCatalog.ProductFor(info.Id);
            if (product is null)
                return Unsupported(
                    checkedAt, $"Distro {info.Id} non supportata.", info);
            var catalogResult = await catalog.LookupAsync(info, ltsOnly, ct);
            var toolTarget = split.Length > 1
                ? ParseUpgradeTool(split[1])
                : null;
            var target = catalogResult?.TargetVersion;
            var targetLts = catalogResult?.TargetLts;
            if (target is null && toolTarget is not null)
            {
                var isLts = IsUbuntuLts(toolTarget);
                if (!ltsOnly || isLts)
                {
                    target = toolTarget;
                    targetLts = isLts;
                }
                else
                {
                    return new ServerOsUpdateResult(
                        info.PrettyName, info.VersionId, null, null,
                        catalogResult?.CurrentEol, "NonLts",
                        $"Disponibile solo {toolTarget} (non-LTS)", checkedAt);
                }
            }
            if (catalogResult is null && target is null)
                return new ServerOsUpdateResult(
                    info.PrettyName, info.VersionId, null, null, null, "Error",
                    "Catalogo aggiornamenti non disponibile.", checkedAt);
            var detail = catalogResult?.Detail
                         ?? (catalogResult is null ? "EOL non disponibile." : null);
            var status = target is not null
                ? "Ok"
                : detail is not null ? "NonLts" : "None";
            return new ServerOsUpdateResult(
                info.PrettyName,
                info.VersionId,
                target,
                targetLts,
                catalogResult?.CurrentEol,
                status,
                Trim(detail),
                checkedAt);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception, "Controllo OS non riuscito per il server {ServerId}", server.Id);
            return Error(checkedAt, exception.Message);
        }
    }

    private static string? ParseUpgradeTool(string output)
    {
        var match = UpgradeTool().Match(output);
        if (match.Success) return match.Groups[1].Value.Trim();
        if (output.Contains("No new release found", StringComparison.OrdinalIgnoreCase))
            return null;
        return null;
    }

    private static ServerOsUpdateResult Error(DateTime checkedAt, string detail) =>
        new(null, null, null, null, null, "Error", Trim(detail), checkedAt);

    private static ServerOsUpdateResult Unsupported(
        DateTime checkedAt, string detail, OsReleaseInfo? info = null) =>
        new(info?.PrettyName, info?.VersionId, null, null, null, "Unsupported",
            Trim(detail), checkedAt);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length > 300 ? value[..300] : value;

    [GeneratedRegex(@"New release '([^']+)' available", RegexOptions.IgnoreCase)]
    private static partial Regex UpgradeTool();

    /// <summary>Ubuntu LTS releases use an even year and the April cycle.</summary>
    private static bool IsUbuntuLts(string value)
    {
        var parts = value.Split('.');
        return parts.Length >= 2
               && int.TryParse(parts[0], out var year)
               && year % 2 == 0
               && parts[1] == "04";
    }
}

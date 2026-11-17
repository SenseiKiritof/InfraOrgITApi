namespace InfraOrgIt.Api.Infrastructure.Ssh;

/// <summary>
/// Wraps a script for sudo. The password, when present, is written to the
/// command stdin (sudo -S); it must never appear in the command text or logs.
/// </summary>
public static class SudoCommand
{
    public static string Wrap(string script, bool withPassword) =>
        $"sudo {(withPassword ? "-S -p ''" : "-n")} -- /bin/sh -c {Quote(script)}";

    public static string Quote(string value) =>
        $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}

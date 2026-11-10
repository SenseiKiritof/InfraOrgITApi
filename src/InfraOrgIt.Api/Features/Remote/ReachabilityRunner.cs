using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Remote;

/// <summary>
/// Runs reachability tests on a set of servers, writes the audit row and
/// returns the per-test results. Shared by the HTTP endpoint and MCP.
/// </summary>
public sealed class ReachabilityRunner(
    ISshConnector ssh,
    IOptions<ServerAccessOptions> options,
    InfraOrgItDbContext db)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    public async Task<List<ReachabilityResultDto>> RunAsync(
        ClaimsPrincipal user,
        IReadOnlyCollection<Server> servers,
        IReadOnlyCollection<int>? testIds,
        CancellationToken ct)
    {
        var globals = await db.ReachabilityTests.AsNoTracking()
            .Where(x => x.ServerId == null)
            .OrderBy(x => x.Hostname)
            .ToListAsync(ct);

        var username = user.Identity?.Name ?? "";
        var results = new List<ReachabilityResultDto>();

        foreach (var server in servers)
        {
            var tests = globals
                .Concat(server.ReachabilityTests)
                .Where(test => testIds is null || testIds.Contains(test.Id))
                .ToList();
            if (tests.Count == 0)
                continue;

            var startedAt = DateTime.UtcNow;
            var rows = new List<ReachabilityResultDto>();
            try
            {
                await using var session = await ssh.ConnectAsync(server, ct);
                await db.SaveChangesAsync(ct);

                foreach (var test in tests)
                {
                    var result = await session.RunAsync(
                        Script(test), TestTimeout, ct);
                    rows.Add(new ReachabilityResultDto(
                        server.Id,
                        server.Name,
                        test.Hostname,
                        test.Port,
                        result.ExitCode == 0,
                        result.ExitCode == 0 ? null : result.Output.Trim()));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                rows.Add(new ReachabilityResultDto(
                    server.Id, server.Name, null, null, false, exception.Message));
            }

            results.AddRange(rows);
            var summary = string.Join("\n", rows.Select(row =>
                $"{row.Hostname}:{row.Port} {(row.Reachable ? "OK" : "KO")} {row.Error}".TrimEnd()));
            CommandAudit.Add(
                db, username, server, "Reachability", "Reachability",
                "", startedAt, new SshCommandResult(rows.All(x => x.Reachable) ? 0 : 1, summary),
                options.Value);
            await db.SaveChangesAsync(ct);
        }

        return results.OrderBy(x => x.ServerName).ThenBy(x => x.Hostname).ToList();
    }

    private static string Script(ReachabilityTest test) =>
        $"timeout 5 bash -c '</dev/tcp/{test.Hostname}/{test.Port}'";
}

using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Remote;

public static class DatabaseQueryEndpoints
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/server-applications/{id:int}/query", RunAsync)
            .RequireAuthorization()
            .RequireRateLimiting("remote-run");
    }

    private static async Task<IResult> RunAsync(
        int id,
        DatabaseQueryInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        ISshConnector ssh,
        IOptions<ServerAccessOptions> accessOptions,
        CancellationToken ct)
    {
        var deploy = await db.ServerApplications.AsNoTracking()
                         .Include(x => x.Server!).ThenInclude(x => x.ProviderEnvironment)
                         .Include(x => x.ApplicationVersion!).ThenInclude(x => x.Application)
                         .SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Deployment", id);
        var server = deploy.Server!;
        var application = deploy.ApplicationVersion!.Application!;
        await access.RequireOperatorAsync(
            user, [server.ProviderEnvironmentId], ct);

        var engine = DatabaseEngines.Resolve(application.Type)
                     ?? throw Guard.Unprocessable(
                         $"Il tipo {application.Type} non è un database interrogabile.");
        var script = DatabaseQueryScript.Build(engine, input.Database, input.Query ?? "");

        var username = user.Identity?.Name ?? "";
        var execution = CommandAudit.Start(
            db, username, server, "Query", $"{application.Name} · {engine}",
            script, DateTime.UtcNow);
        db.CommandExecutions.Add(execution);
        await db.SaveChangesAsync(ct);

        ISshSession? session = null;
        try
        {
            session = await ssh.ConnectAsync(server, ct);
            var result = await session.RunAsync(script, QueryTimeout, ct);
            CommandAudit.Complete(execution, result, accessOptions.Value);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new DatabaseQueryResult(
                engine, result.ExitCode, result.Output, result.TimedOut));
        }
        catch (Exception exception)
        {
            CommandAudit.Complete(
                execution,
                new SshCommandResult(null, exception.Message),
                accessOptions.Value);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (session is not null) await session.DisposeAsync();
        }
    }
}

public sealed record DatabaseQueryInput(string? Query, string? Database);

public sealed record DatabaseQueryResult(
    string Engine,
    int? ExitCode,
    string Output,
    bool TimedOut);

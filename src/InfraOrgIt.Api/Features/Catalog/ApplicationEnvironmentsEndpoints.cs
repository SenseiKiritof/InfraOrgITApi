using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Catalog;

public static class ApplicationEnvironmentsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/applications/{id:int}/environments")
            .RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapPut("/{environmentId:int}", UpsertAsync);
    }

    private static async Task<IResult> ListAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        await EnsureApplicationAsync(db, id, ct);

        var environments = await db.EnvironmentDefinitions.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var rows = await db.ApplicationEnvironments.AsNoTracking()
            .Where(x => x.ApplicationId == id)
            .ToDictionaryAsync(x => x.EnvironmentDefinitionId, ct);

        var items = environments
            .Select(x =>
            {
                rows.TryGetValue(x.Id, out var row);
                return new ApplicationEnvironmentDto(
                    x.Id, x.Name, row?.Hostname, row?.IgnoreSslErrors ?? false);
            })
            .ToList();

        return Results.Ok(items);
    }

    private static async Task<IResult> UpsertAsync(
        int id,
        int environmentId,
        ApplicationEnvironmentInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        await EnsureApplicationAsync(db, id, ct);
        if (!await db.EnvironmentDefinitions.AnyAsync(x => x.Id == environmentId, ct))
            throw Guard.NotFound("Ambiente", environmentId);

        var hostname = ValidateHostname(input.Hostname);
        var entity = await db.ApplicationEnvironments.SingleOrDefaultAsync(
            x => x.ApplicationId == id && x.EnvironmentDefinitionId == environmentId, ct);

        if (entity is null)
        {
            db.ApplicationEnvironments.Add(new ApplicationEnvironment
            {
                ApplicationId = id,
                EnvironmentDefinitionId = environmentId,
                Hostname = hostname,
                IgnoreSslErrors = input.IgnoreSslErrors
            });
        }
        else
        {
            entity.Hostname = hostname;
            entity.IgnoreSslErrors = input.IgnoreSslErrors;
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task EnsureApplicationAsync(
        InfraOrgItDbContext db, int applicationId, CancellationToken ct)
    {
        if (!await db.Applications.AnyAsync(x => x.Id == applicationId, ct))
            throw Guard.NotFound("Applicazione", applicationId);
    }

    private static string? ValidateHostname(string? value)
    {
        var text = Guard.OptionalText(value, "Hostname", 300);
        if (text is null)
            return null;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw Guard.Unprocessable(
                "Il campo Hostname deve essere un URL http/https, es. http://host:8080.");

        return text.TrimEnd('/');
    }
}

public sealed record ApplicationEnvironmentInput(string? Hostname, bool IgnoreSslErrors);

public sealed record ApplicationEnvironmentDto(
    int EnvironmentId,
    string Name,
    string? Hostname,
    bool IgnoreSslErrors);

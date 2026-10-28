using System.Linq.Expressions;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Catalog;

/// <summary>Saved call history, always scoped to the calling user.</summary>
public static class ApiCallsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/api-calls").RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var applicationId = PageRequest.IntQuery(http, "applicationId");
        var environmentId = PageRequest.IntQuery(http, "environmentId");
        var username = user.Identity?.Name ?? "";

        var query = db.ApplicationApiCalls.AsNoTracking()
            .Where(x => x.Username == username);
        if (applicationId is not null)
            query = query.Where(x => x.ApplicationVersionApi != null &&
                                     x.ApplicationVersionApi.ApplicationVersion!.ApplicationId == applicationId);
        if (environmentId is not null)
            query = query.Where(x => x.EnvironmentDefinitionId == environmentId);

        query = page.Sort switch
        {
            "createdAt" => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            "durationMs" => query.OrderBy(x => x.DurationMs).ThenBy(x => x.Id),
            "-durationMs" => query.OrderByDescending(x => x.DurationMs).ThenByDescending(x => x.Id),
            "sizeBytes" => query.OrderBy(x => x.ResponseSizeBytes).ThenBy(x => x.Id),
            "-sizeBytes" => query.OrderByDescending(x => x.ResponseSizeBytes).ThenByDescending(x => x.Id),
            "status" => query.OrderBy(x => x.Status).ThenBy(x => x.Id),
            "-status" => query.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(ToSummary)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ApplicationApiCallSummaryDto>(
            items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var username = user.Identity?.Name ?? "";
        var dto = await db.ApplicationApiCalls.AsNoTracking()
            .Where(x => x.Id == id && x.Username == username)
            .Select(ToDetail)
            .SingleOrDefaultAsync(ct);

        return dto is null ? throw Guard.NotFound("Chiamata API", id) : Results.Ok(dto);
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var username = user.Identity?.Name ?? "";
        var entity = await db.ApplicationApiCalls
                         .SingleOrDefaultAsync(x => x.Id == id && x.Username == username, ct)
                     ?? throw Guard.NotFound("Chiamata API", id);

        db.ApplicationApiCalls.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static readonly Expression<Func<ApplicationApiCall, ApplicationApiCallSummaryDto>> ToSummary =
        x => new ApplicationApiCallSummaryDto(
            x.Id,
            x.ApplicationVersionApi == null
                ? null
                : x.ApplicationVersionApi.ApplicationVersion!.ApplicationId,
            x.ApplicationVersionApi == null
                ? null
                : x.ApplicationVersionApi.ApplicationVersion!.Application!.Name,
            x.ApplicationVersionApiId,
            x.Method,
            x.Url,
            x.EnvironmentDefinitionId,
            x.EnvironmentDefinition!.Name,
            x.Status,
            x.DurationMs,
            x.ResponseSizeBytes,
            x.Truncated,
            x.CreatedAt);

    private static readonly Expression<Func<ApplicationApiCall, ApplicationApiCallDetailDto>> ToDetail =
        x => new ApplicationApiCallDetailDto(
            x.Id,
            x.ApplicationVersionApi == null
                ? null
                : x.ApplicationVersionApi.ApplicationVersion!.ApplicationId,
            x.ApplicationVersionApi == null
                ? null
                : x.ApplicationVersionApi.ApplicationVersion!.Application!.Name,
            x.ApplicationVersionApiId,
            x.Method,
            x.Url,
            x.EnvironmentDefinitionId,
            x.EnvironmentDefinition!.Name,
            x.Status,
            x.DurationMs,
            x.ResponseSizeBytes,
            x.Truncated,
            x.CreatedAt,
            x.QueryJson,
            x.RequestBody,
            x.ResponseBody,
            x.Binary);
}

public sealed record ApplicationApiCallSummaryDto(
    int Id,
    int? ApplicationId,
    string? ApplicationName,
    int? ApplicationVersionApiId,
    string Method,
    string Url,
    int EnvironmentId,
    string EnvironmentName,
    int? Status,
    long DurationMs,
    long SizeBytes,
    bool Truncated,
    DateTime CreatedAt);

public sealed record ApplicationApiCallDetailDto(
    int Id,
    int? ApplicationId,
    string? ApplicationName,
    int? ApplicationVersionApiId,
    string Method,
    string Url,
    int EnvironmentId,
    string EnvironmentName,
    int? Status,
    long DurationMs,
    long SizeBytes,
    bool Truncated,
    DateTime CreatedAt,
    string? QueryJson,
    string? RequestBody,
    string? ResponseBody,
    bool Binary);

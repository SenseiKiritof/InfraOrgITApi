using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Mcp;

public static class McpGrantsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/mcp/grants").RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var items = await db.McpGrants.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new McpGrantDto(
                x.Id,
                x.UserId,
                x.User != null ? x.User.Username : null,
                x.GroupId,
                x.Group != null ? x.Group.Name : null,
                x.Action,
                x.EnvironmentDefinitionId,
                x.EnvironmentDefinition != null
                    ? x.EnvironmentDefinition.Name
                    : null,
                x.CreatedAt))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        McpGrantInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);

        if ((input.UserId is null) == (input.GroupId is null))
            throw Guard.Unprocessable(
                "Indica esattamente uno tra UserId e GroupId.");
        if (string.IsNullOrWhiteSpace(input.Action) ||
            !McpGrantService.Actions.Contains(input.Action))
            throw Guard.Unprocessable(
                $"Action ammessa: {string.Join(", ", McpGrantService.Actions)}.");

        if (input.UserId is int userId &&
            !await db.Users.AnyAsync(x => x.Id == userId, ct))
            throw Guard.NotFound("Utente", userId);
        if (input.GroupId is int groupId &&
            !await db.Groups.AnyAsync(x => x.Id == groupId, ct))
            throw Guard.NotFound("Gruppo", groupId);
        if (input.EnvironmentDefinitionId is int environmentId &&
            !await db.EnvironmentDefinitions.AnyAsync(x => x.Id == environmentId, ct))
            throw Guard.NotFound("Ambiente", environmentId);

        var entity = new McpGrant
        {
            UserId = input.UserId,
            GroupId = input.GroupId,
            Action = input.Action,
            EnvironmentDefinitionId = input.EnvironmentDefinitionId,
            CreatedAt = DateTime.UtcNow
        };
        db.McpGrants.Add(entity);
        await db.SaveChangesAsync(ct);
        return Results.Ok(entity.Id);
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.McpGrants.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Grant MCP", id);
        db.McpGrants.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

public sealed record McpGrantInput(
    int? UserId,
    int? GroupId,
    string? Action,
    int? EnvironmentDefinitionId = null);

public sealed record McpGrantDto(
    int Id,
    int? UserId,
    string? Username,
    int? GroupId,
    string? GroupName,
    string Action,
    int? EnvironmentDefinitionId,
    string? EnvironmentName,
    DateTime CreatedAt);

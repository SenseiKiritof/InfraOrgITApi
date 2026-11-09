using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Auth;

public static class ApiTokensEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/api-tokens").RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapDelete("/{id:int}", RevokeAsync);
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var userId = await CurrentUserIdAsync(user, db, ct);
        var items = await db.ApiTokens.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Id)
            .Select(x => new ApiTokenDto(
                x.Id, x.Name, x.Scopes, x.CreatedAt, x.ExpiresAt,
                x.LastUsedAt, x.RevokedAt))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        ApiTokenInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        ApiTokenService tokens,
        CancellationToken ct)
    {
        var userId = await CurrentUserIdAsync(user, db, ct);
        var name = Guard.Text(input.Name, "Name", 200);
        var scopes = ApiTokenService.NormalizeScopes(input.Scopes);
        if (input.ExpiresInDays is < 1 or > 3650)
            throw Guard.Unprocessable("ExpiresInDays deve essere tra 1 e 3650.");

        var created = await tokens.CreateAsync(
            userId, name, scopes, input.ExpiresInDays, ct);
        return Results.Ok(new ApiTokenCreatedDto(
            created.Id, created.Name, created.Scopes, created.Token,
            created.CreatedAt, created.ExpiresAt));
    }

    private static async Task<IResult> RevokeAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var userId = await CurrentUserIdAsync(user, db, ct);
        var entity = await db.ApiTokens
                         .SingleOrDefaultAsync(
                             x => x.Id == id && x.UserId == userId, ct)
                     ?? throw Guard.NotFound("Token", id);

        entity.RevokedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<int> CurrentUserIdAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var username = user.Identity?.Name ?? "";
        var id = await db.Users
            .Where(x => x.Username == username)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        return id ?? throw Guard.Forbidden("Utente non provisionato.");
    }
}

public sealed record ApiTokenInput(
    string? Name,
    string? Scopes = null,
    int? ExpiresInDays = null);

public sealed record ApiTokenDto(
    int Id,
    string Name,
    string Scopes,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    DateTime? RevokedAt);

public sealed record ApiTokenCreatedDto(
    int Id,
    string Name,
    string Scopes,
    string Token,
    DateTime CreatedAt,
    DateTime? ExpiresAt);

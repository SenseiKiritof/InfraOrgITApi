using System.Security.Cryptography;
using System.Text;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed record ApiTokenIdentity(
    int TokenId,
    int UserId,
    string Username,
    IReadOnlyList<string> Scopes);

public sealed record ApiTokenCreated(
    int Id,
    string Name,
    string Scopes,
    string Token,
    DateTime CreatedAt,
    DateTime? ExpiresAt);

/// <summary>
/// Personal access tokens for non-browser clients (MCP, IDE, automation).
/// The raw value is returned once; only its SHA-256 hash is stored.
/// </summary>
public sealed class ApiTokenService(InfraOrgItDbContext db)
{
    public const string Prefix = "itk_";
    public static readonly string[] AllowedScopes =
        ["mcp:read", "mcp:operate", "tunnels:operate"];

    public async Task<ApiTokenCreated> CreateAsync(
        int userId,
        string name,
        IReadOnlyCollection<string> scopes,
        int? expiresInDays,
        CancellationToken ct)
    {
        var token = Prefix + WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
        var entity = new ApiToken
        {
            UserId = userId,
            Name = name,
            TokenHash = Hash(token),
            Scopes = string.Join(' ', scopes),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresInDays is null
                ? null
                : DateTime.UtcNow.AddDays(expiresInDays.Value)
        };
        db.ApiTokens.Add(entity);
        await db.SaveChangesAsync(ct);

        return new ApiTokenCreated(
            entity.Id,
            entity.Name,
            entity.Scopes,
            token,
            entity.CreatedAt,
            entity.ExpiresAt);
    }

    public async Task<ApiTokenIdentity?> ValidateAsync(
        string raw,
        CancellationToken ct)
    {
        if (!raw.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        var hash = Hash(raw);
        var entity = await db.ApiTokens
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, ct);

        if (entity?.User is null ||
            entity.RevokedAt is not null ||
            entity.ExpiresAt < DateTime.UtcNow)
            return null;

        entity.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new ApiTokenIdentity(
            entity.Id,
            entity.UserId,
            entity.User.Username,
            entity.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))
            .ToLowerInvariant();

    public static IReadOnlyList<string> NormalizeScopes(string? value)
    {
        var scopes = (value ?? "mcp:read")
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (scopes.Length == 0)
            throw Guard.Unprocessable("Indica almeno uno scope.");
        var unknown = scopes.Where(scope => !AllowedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (unknown.Length > 0)
            throw Guard.Unprocessable(
                $"Scope non ammessi: {string.Join(", ", unknown)}. Ammessi: {string.Join(", ", AllowedScopes)}.");
        return scopes;
    }
}

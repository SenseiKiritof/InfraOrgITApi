using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Catalog;

public static class ApplicationApiKeysEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/applications/{applicationId:int}/api-keys")
            .RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(
        int applicationId,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        await EnsureApplicationAsync(db, applicationId, ct);

        var items = await db.ApplicationApiKeys.AsNoTracking()
            .Where(x => x.ApplicationId == applicationId)
            .OrderBy(x => x.Name)
            .Select(x => new ApplicationApiKeyDto(
                x.Id, x.ApplicationId, x.Name, x.Location,
                x.EncryptedValue != "", x.KeyVersion, x.CreatedAt, x.UpdatedAt, x.Version))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        int applicationId,
        ApplicationApiKeyInput input,
        ClaimsPrincipal user,
        CredentialCipher cipher,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        await EnsureApplicationAsync(db, applicationId, ct);

        var name = Guard.Text(input.Name, "Name", 200);
        var location = Location(input.Location);
        var value = Guard.OptionalText(input.Value, "Value", 1000);
        await EnsureUniqueAsync(db, applicationId, name, location, null, ct);

        var (ciphertext, keyVersion) = value is null
            ? ("", 0)
            : cipher.Encrypt(value);

        var entity = new ApplicationApiKey
        {
            ApplicationId = applicationId,
            Name = name,
            Location = location,
            EncryptedValue = ciphertext,
            KeyVersion = keyVersion
        };
        db.ApplicationApiKeys.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(ToDto(entity));
    }

    private static async Task<IResult> UpdateAsync(
        int applicationId,
        int id,
        ApplicationApiKeyInput input,
        ClaimsPrincipal user,
        CredentialCipher cipher,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);

        var entity = await db.ApplicationApiKeys
                         .SingleOrDefaultAsync(x => x.Id == id && x.ApplicationId == applicationId, ct)
                     ?? throw Guard.NotFound("API key", id);

        var name = Guard.Text(input.Name, "Name", 200);
        var location = Location(input.Location);
        await EnsureUniqueAsync(db, applicationId, name, location, id, ct);

        entity.Name = name;
        entity.Location = location;

        // An empty value keeps the stored secret: the client never receives it.
        var value = Guard.OptionalText(input.Value, "Value", 1000);
        if (value is not null)
        {
            var (ciphertext, keyVersion) = cipher.Encrypt(value);
            entity.EncryptedValue = ciphertext;
            entity.KeyVersion = keyVersion;
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        int applicationId,
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);

        var entity = await db.ApplicationApiKeys
                         .SingleOrDefaultAsync(x => x.Id == id && x.ApplicationId == applicationId, ct)
                     ?? throw Guard.NotFound("API key", id);

        db.ApplicationApiKeys.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    internal static async Task<List<ApplicationApiKey>> LoadAsync(
        InfraOrgItDbContext db, int applicationId, CancellationToken ct) =>
        await db.ApplicationApiKeys.AsNoTracking()
            .Where(x => x.ApplicationId == applicationId && x.EncryptedValue != "")
            .ToListAsync(ct);

    private static async Task EnsureApplicationAsync(
        InfraOrgItDbContext db, int applicationId, CancellationToken ct)
    {
        if (!await db.Applications.AnyAsync(x => x.Id == applicationId, ct))
            throw Guard.NotFound("Applicazione", applicationId);
    }

    private static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, int applicationId, string name, string location,
        int? id, CancellationToken ct)
    {
        var exists = await db.ApplicationApiKeys.AnyAsync(
            x => x.ApplicationId == applicationId && x.Name == name && x.Location == location
                 && (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict($"Esiste già una API key {name} ({location}).");
    }

    private static string Location(string? value)
    {
        var location = Guard.Text(value, "Location", 10);
        return location is "Header" or "Query"
            ? location
            : throw Guard.Unprocessable("Il campo Location ammette Header o Query.");
    }

    private static ApplicationApiKeyDto ToDto(ApplicationApiKey entity) => new(
        entity.Id, entity.ApplicationId, entity.Name, entity.Location,
        entity.EncryptedValue != "", entity.KeyVersion,
        entity.CreatedAt, entity.UpdatedAt, entity.Version);
}

public sealed record ApplicationApiKeyInput(
    string? Name,
    string? Location,
    string? Value);

public sealed record ApplicationApiKeyDto(
    int Id,
    int ApplicationId,
    string Name,
    string Location,
    bool HasValue,
    int KeyVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

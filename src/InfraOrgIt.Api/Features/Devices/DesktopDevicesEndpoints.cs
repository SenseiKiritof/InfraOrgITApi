using System.Security.Claims;
using System.Security.Cryptography;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Devices;

public static class DesktopDevicesEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/devices").RequireAuthorization();
        group.MapGet("", ListAsync);
        group.MapPost("", RegisterAsync);
        group.MapDelete("/{id:int}", RevokeAsync);
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        CancellationToken ct)
    {
        var admin = await access.IsSystemAdminAsync(user, ct);
        var actor = await ActorResolver.ResolveAsync(user, db, ct);
        var query = db.DesktopDevices.AsNoTracking();
        if (!admin) query = query.Where(x => x.UserId == actor.Id);

        var items = await query
            .OrderBy(x => x.Name)
            .Select(x => new DesktopDeviceDto(
                x.Id,
                x.UserId,
                x.User != null ? x.User.Username : "",
                x.Name,
                x.Fingerprint,
                x.CreatedAt,
                x.LastSeenAt,
                x.RevokedAt))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> RegisterAsync(
        DesktopDeviceInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        var actor = await ActorResolver.ResolveAsync(user, db, ct);
        var name = Guard.Text(
            string.IsNullOrWhiteSpace(input.Name) ? "Desktop" : input.Name,
            "Name", 100);
        var pem = Guard.Text(input.PublicKeyPem, "PublicKeyPem", 20_000);
        var fingerprint = Fingerprint(input.Fingerprint);
        ValidateKey(pem, fingerprint);

        var existing = await db.DesktopDevices.SingleOrDefaultAsync(
            x => x.UserId == actor.Id && x.Fingerprint == fingerprint, ct);
        var now = DateTime.UtcNow;
        if (existing is not null)
        {
            if (existing.RevokedAt is not null)
                throw Guard.Forbidden("Dispositivo revocato: registra una nuova chiave.");
            existing.Name = name;
            existing.LastSeenAt = now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(Dto(existing, actor.Username));
        }

        var deviceCount = await db.DesktopDevices
            .CountAsync(x => x.UserId == actor.Id && x.RevokedAt == null, ct);
        if (deviceCount >= 10)
            throw Guard.Unprocessable("Massimo 10 dispositivi attivi per utente.");

        var device = new DesktopDevice
        {
            UserId = actor.Id,
            Name = name,
            PublicKeyPem = pem,
            Fingerprint = fingerprint,
            LastSeenAt = now
        };
        db.DesktopDevices.Add(device);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditEntries.Change(
            user, "device.register", "DesktopDevice", device.Id,
            detail: new { device.Name, device.Fingerprint }), ct);
        return Results.Ok(Dto(device, actor.Username));
    }

    private static async Task<IResult> RevokeAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        IAuditService audit,
        CancellationToken ct)
    {
        var actor = await ActorResolver.ResolveAsync(user, db, ct);
        var admin = await access.IsSystemAdminAsync(user, ct);
        var device = await db.DesktopDevices.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Dispositivo", id);
        if (!admin && device.UserId != actor.Id)
            throw Guard.Forbidden("Dispositivo di un altro utente.");
        if (device.RevokedAt is null)
        {
            device.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(AuditEntries.Change(
                user, "device.revoke", "DesktopDevice", device.Id,
                detail: new { device.Name, device.Fingerprint }), ct);
        }
        return Results.NoContent();
    }

    private static void ValidateKey(string pem, string fingerprint)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            if (rsa.KeySize is < 2048 or > 4096)
                throw new CryptographicException("Chiave fuori intervallo.");
            var computed = Convert.ToBase64String(
                    SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()))
                .TrimEnd('=');
            if (!CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(computed),
                    System.Text.Encoding.ASCII.GetBytes(fingerprint)))
                throw Guard.Unprocessable("Il fingerprint non corrisponde alla chiave.");
        }
        catch (CryptographicException)
        {
            throw Guard.Unprocessable("Chiave pubblica non valida (RSA >= 2048).");
        }
    }

    private static string Fingerprint(string? value) =>
        Guard.Text(value, "Fingerprint", 100)
            .Replace("SHA256:", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('=');

    private static DesktopDeviceDto Dto(DesktopDevice device, string username) =>
        new(device.Id, device.UserId, username, device.Name, device.Fingerprint,
            device.CreatedAt, device.LastSeenAt, device.RevokedAt);
}

public sealed record DesktopDeviceInput(
    string? Name,
    string? PublicKeyPem,
    string? Fingerprint);

public sealed record DesktopDeviceDto(
    int Id,
    int UserId,
    string Username,
    string Name,
    string Fingerprint,
    DateTime CreatedAt,
    DateTime LastSeenAt,
    DateTime? RevokedAt);

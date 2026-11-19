using System.Security.Claims;
using System.Text.RegularExpressions;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Catalog;

public static partial class ApplicationTypesEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/application-types").RequireAuthorization();
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var items = await db.ApplicationTypes.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new ApplicationTypeDto(
                x.Id, x.Name, x.Label, x.SortOrder,
                x.Applications.Count,
                x.CreatedAt, x.UpdatedAt, x.Version))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        ApplicationTypeInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var name = Guard.Text(input.Name, "Name", 20);
        if (!NamePattern().IsMatch(name))
            throw Guard.Unprocessable(
                "Name ammette solo lettere e cifre e deve iniziare con una lettera.");
        await EnsureUniqueAsync(db, name, null, ct);

        var entity = new ApplicationType
        {
            Name = name,
            Label = Guard.Text(input.Label, "Label", 50),
            SortOrder = input.SortOrder
        };
        db.ApplicationTypes.Add(entity);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditEntries.Change(
            user, "applicationType.create", "ApplicationType", entity.Id,
            detail: new { entity.Name }), ct);
        return Results.Ok(ToDto(entity, 0));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ApplicationTypeInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.ApplicationTypes.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Tipo applicazione", id);

        var name = Guard.Text(input.Name, "Name", 20);
        if (!NamePattern().IsMatch(name))
            throw Guard.Unprocessable(
                "Name ammette solo lettere e cifre e deve iniziare con una lettera.");
        var label = Guard.Text(input.Label, "Label", 50);
        var previousName = entity.Name;
        var renamed = !string.Equals(entity.Name, name, StringComparison.Ordinal);
        if (renamed)
        {
            if (string.Equals(entity.Name, "Backend", StringComparison.OrdinalIgnoreCase))
                throw Guard.Conflict("Backend è il tipo predefinito e non può essere rinominato.");
            await EnsureUniqueAsync(db, name, id, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var replacement = new ApplicationType
            {
                Name = name,
                Label = label,
                SortOrder = input.SortOrder
            };
            db.ApplicationTypes.Add(replacement);
            // Il nuovo codice deve esistere prima di aggiornare le applicazioni.
            await db.SaveChangesAsync(ct);
            var linked = await db.Applications.Where(x => x.Type == previousName).ToListAsync(ct);
            foreach (var application in linked)
                application.Type = name;
            await db.SaveChangesAsync(ct);
            db.ApplicationTypes.Remove(entity);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await audit.RecordAsync(AuditEntries.Change(
                user, "applicationType.rename", "ApplicationType", replacement.Id,
                detail: new { from = previousName, to = name }), ct);
            var renamedApplications = await db.Applications.CountAsync(x => x.Type == name, ct);
            return Results.Ok(ToDto(replacement, renamedApplications));
        }

        entity.Label = label;
        entity.SortOrder = input.SortOrder;
        await db.SaveChangesAsync(ct);
        var applications = await db.Applications.CountAsync(x => x.Type == entity.Name, ct);
        await audit.RecordAsync(AuditEntries.Change(
            user, "applicationType.update", "ApplicationType", entity.Id,
            detail: new { entity.Name, entity.Label }), ct);
        return Results.Ok(ToDto(entity, applications));
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.ApplicationTypes.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Tipo applicazione", id);

        if (string.Equals(entity.Name, "Backend", StringComparison.OrdinalIgnoreCase))
            throw Guard.Conflict("Backend è il tipo predefinito e non può essere rimosso.");
        var applications = await db.Applications.CountAsync(x => x.Type == entity.Name, ct);
        if (applications > 0)
            throw Guard.Conflict(
                $"Tipo usato da {applications} applicazioni: aggiornarle prima di rimuoverlo.");

        db.ApplicationTypes.Remove(entity);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditEntries.Change(
            user, "applicationType.delete", "ApplicationType", entity.Id,
            detail: new { entity.Name }), ct);
        return Results.NoContent();
    }

    private static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, string name, int? id, CancellationToken ct)
    {
        var lowered = name.ToLower();
        var exists = await db.ApplicationTypes.AnyAsync(
            x => x.Name.ToLower() == lowered && (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict($"Esiste già un tipo applicazione chiamato {name}.");
    }

    private static ApplicationTypeDto ToDto(ApplicationType entity, int applications) =>
        new(entity.Id, entity.Name, entity.Label, entity.SortOrder, applications,
            entity.CreatedAt, entity.UpdatedAt, entity.Version);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$")]
    private static partial Regex NamePattern();
}

public sealed record ApplicationTypeInput(string? Name, string? Label, int SortOrder);

public sealed record ApplicationTypeDto(
    int Id,
    string Name,
    string Label,
    int SortOrder,
    int ApplicationsCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

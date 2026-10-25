using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Remote;

public static class CommandTemplateEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/command-templates").RequireAuthorization();
        group.MapGet("", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var page = PageRequest.From(http);
        var query = db.CommandTemplates.AsNoTracking();
        if (page.Q is not null)
            query = query.Where(x => x.Name.Contains(page.Q) ||
                                     x.Description.Contains(page.Q) ||
                                     x.Script.Contains(page.Q));

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(x => x.Name)
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(x => new
            {
                x.Id, x.Name, x.Description, x.Script,
                x.CreatedAt, x.UpdatedAt, x.Version, PlaceholderCount = 0
            })
            .ToListAsync(ct);
        var items = rows.Select(x => new CommandTemplateDto(
            x.Id, x.Name, x.Description, x.Script,
            CommandRenderer.Placeholders(x.Script).Count,
            x.CreatedAt, x.UpdatedAt, x.Version)).ToList();

        return Results.Ok(new PagedResult<CommandTemplateDto>(
            items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var row = await db.CommandTemplates.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Name, x.Description, x.Script, x.CreatedAt, x.UpdatedAt, x.Version })
            .SingleOrDefaultAsync(ct);
        return row is null
            ? throw Guard.NotFound("Comando", id)
            : Results.Ok(new CommandTemplateDto(
                row.Id, row.Name, row.Description, row.Script,
                CommandRenderer.Placeholders(row.Script).Count,
                row.CreatedAt, row.UpdatedAt, row.Version));
    }

    private static async Task<IResult> CreateAsync(
        CommandTemplateInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var name = Guard.Text(input.Name, "Name", 200);
        await EnsureUniqueAsync(db, name, null, ct);

        var entity = new CommandTemplate
        {
            Name = name,
            Description = Guard.Text(input.Description, "Description", 500, required: false),
            Script = Guard.Text(input.Script, "Script", 100_000)
        };
        db.CommandTemplates.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new CommandTemplateDto(
            entity.Id, entity.Name, entity.Description, entity.Script,
            CommandRenderer.Placeholders(entity.Script).Count,
            entity.CreatedAt, entity.UpdatedAt, entity.Version));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        CommandTemplateInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.CommandTemplates.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Comando", id);

        var name = Guard.Text(input.Name, "Name", 200);
        await EnsureUniqueAsync(db, name, id, ct);

        entity.Name = name;
        entity.Description = Guard.Text(input.Description, "Description", 500, required: false);
        entity.Script = Guard.Text(input.Script, "Script", 100_000);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.CommandTemplates.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Comando", id);

        var actions = await db.ApplicationActions.CountAsync(x => x.CommandTemplateId == id, ct);
        if (actions > 0)
            throw Guard.Conflict($"Comando usato da {actions} azioni: rimuoverle prima.");

        db.CommandTemplates.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, string name, int? id, CancellationToken ct)
    {
        var exists = await db.CommandTemplates.AnyAsync(
            x => x.Name == name && (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict($"Esiste già un comando chiamato {name}.");
    }
}

public sealed record CommandTemplateInput(string? Name, string? Description, string? Script);

public sealed record CommandTemplateDto(
    int Id,
    string Name,
    string Description,
    string Script,
    int PlaceholderCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

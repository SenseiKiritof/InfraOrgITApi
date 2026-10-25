using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Topology;

public static class DiagramEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/diagrams").RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapGet("/default", GetDefaultAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
        group.MapGet("/{id:int}/graph", GetGraphAsync);
        group.MapPut("/{id:int}/nodes", ReplaceNodesAsync);
        group.MapPut("/{id:int}/edges", ReplaceEdgesAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var query = db.Diagrams.AsNoTracking().Include(x => x.ScopeItems).AsQueryable();

        if (page.Q is not null)
            query = query.Where(x => x.Name.Contains(page.Q) ||
                                     (x.Description != null && x.Description.Contains(page.Q)));

        query = page.Sort switch
        {
            "name" => query.OrderBy(x => x.Name),
            "-name" => query.OrderByDescending(x => x.Name),
            _ => query.OrderBy(x => x.Id)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<DiagramDto>(
            items.Select(DiagramService.ToDto).ToList(), total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var diagram = await db.Diagrams.AsNoTracking()
                          .Include(x => x.ScopeItems)
                          .SingleOrDefaultAsync(x => x.Id == id, ct)
                      ?? throw Guard.NotFound("Diagramma", id);
        return Results.Ok(DiagramService.ToDto(diagram));
    }

    private static async Task<IResult> GetDefaultAsync(
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var diagram = await db.Diagrams.AsNoTracking()
                          .Include(x => x.ScopeItems)
                          .SingleOrDefaultAsync(x => x.IsDefault, ct)
                      ?? throw Guard.NotFound("Diagramma predefinito", 0);
        return Results.Ok(DiagramService.ToDto(diagram));
    }

    private static async Task<IResult> CreateAsync(
        DiagramInput input,
        ClaimsPrincipal user,
        DiagramService service,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var name = Guard.Text(input.Name, "Name", 200);
        var description = Guard.OptionalText(input.Description, "Description", 500);
        await service.ValidateScopesAsync(input.ScopeItems, ct);
        await service.RequireWriteAsync(user, [], input, false, ct, isNew: true);

        var entity = new Diagram
        {
            Name = name,
            Description = description,
            IsDefault = input.IsDefault
        };

        foreach (var scope in Deduplicate(input.ScopeItems))
        {
            entity.ScopeItems.Add(new DiagramScopeItem
            {
                ScopeType = scope.ScopeType,
                EntityId = scope.EntityId
            });
        }

        if (entity.IsDefault)
            await service.ClearDefaultAsync(null, ct);
        db.Diagrams.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(DiagramService.ToDto(entity));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        DiagramInput input,
        ClaimsPrincipal user,
        DiagramService service,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.Diagrams
                         .Include(x => x.ScopeItems)
                         .SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Diagramma", id);

        var name = Guard.Text(input.Name, "Name", 200);
        var description = Guard.OptionalText(input.Description, "Description", 500);
        await service.ValidateScopesAsync(input.ScopeItems, ct);
        await service.RequireWriteAsync(user, entity.ScopeItems, input, entity.IsDefault, ct);

        entity.Name = name;
        entity.Description = description;

        var incoming = Deduplicate(input.ScopeItems).ToList();
        var incomingKeys = incoming.Select(x => (x.ScopeType, x.EntityId)).ToHashSet();

        var removed = entity.ScopeItems
            .Where(x => !incomingKeys.Contains((x.ScopeType, x.EntityId)))
            .ToList();
        db.DiagramScopeItems.RemoveRange(removed);
        foreach (var scope in removed)
            entity.ScopeItems.Remove(scope);

        var existingKeys = entity.ScopeItems
            .Select(x => (x.ScopeType, x.EntityId))
            .ToHashSet();
        foreach (var scope in incoming)
        {
            if (existingKeys.Add((scope.ScopeType, scope.EntityId)))
            {
                entity.ScopeItems.Add(new DiagramScopeItem
                {
                    ScopeType = scope.ScopeType,
                    EntityId = scope.EntityId
                });
            }
        }

        if (input.IsDefault && !entity.IsDefault)
            await service.ClearDefaultAsync(id, ct);
        entity.IsDefault = input.IsDefault;

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal user,
        DiagramService service,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.Diagrams
                         .Include(x => x.ScopeItems)
                         .SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Diagramma", id);

        await service.RequireWriteAsync(user, entity.ScopeItems, AsInput(entity), entity.IsDefault, ct);
        db.Diagrams.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetGraphAsync(
        int id,
        DiagramService service,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var diagram = await LoadAsync(db, id, ct);
        return Results.Ok(await service.GetGraphAsync(diagram, ct));
    }

    private static async Task<IResult> ReplaceNodesAsync(
        int id,
        List<DiagramNodeInput>? nodes,
        ClaimsPrincipal user,
        DiagramService service,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var diagram = await LoadAsync(db, id, ct);
        await service.RequireWriteAsync(user, diagram.ScopeItems, AsInput(diagram), diagram.IsDefault, ct);
        await service.ReplaceNodesAsync(diagram, nodes, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReplaceEdgesAsync(
        int id,
        List<DiagramEdgeInput>? edges,
        ClaimsPrincipal user,
        DiagramService service,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var diagram = await LoadAsync(db, id, ct);
        await service.RequireWriteAsync(user, diagram.ScopeItems, AsInput(diagram), diagram.IsDefault, ct);
        await service.ReplaceEdgesAsync(diagram, edges, ct);
        return Results.NoContent();
    }

    private static async Task<Diagram> LoadAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.Diagrams
            .Include(x => x.ScopeItems)
            .Include(x => x.Nodes)
            .Include(x => x.Edges)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw Guard.NotFound("Diagramma", id);

    private static DiagramInput AsInput(Diagram diagram) => new(
        diagram.Name,
        diagram.Description,
        diagram.IsDefault,
        diagram.ScopeItems
            .Select(x => new DiagramScopeInput(x.ScopeType, x.EntityId))
            .ToList());

    private static IEnumerable<(string ScopeType, int EntityId)> Deduplicate(
        IReadOnlyCollection<DiagramScopeInput>? scopes) =>
        (scopes ?? [])
            .Select(x => (
                ScopeType: Guard.Enum<DiagramScopeType>(x.ScopeType, "ScopeType"),
                x.EntityId))
            .Distinct();
}

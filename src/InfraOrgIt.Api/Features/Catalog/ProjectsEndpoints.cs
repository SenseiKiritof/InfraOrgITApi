using System.Linq.Expressions;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Catalog;

public static class ProjectsEndpoints
{
    public static void Map(WebApplication app)
    {
        var groups = new[]
        {
            app.MapGroup("/api/projects").RequireAuthorization(),
            app.MapGroup("/api/applications").RequireAuthorization()
        };

        groups[0].MapGet("", ListAsync);
        groups[0].MapGet("/{id:int}", GetAsync);
        groups[0].MapPost("", CreateAsync);
        groups[0].MapPut("/{id:int}", UpdateAsync);
        groups[0].MapDelete("/{id:int}", DeleteAsync);
        groups[0].MapGet("/{id:int}/applications", ListApplicationsAsync);
        groups[0].MapPost("/{id:int}/applications", AddApplicationAsync);
        groups[0].MapDelete("/{id:int}/applications/{applicationId:int}", RemoveApplicationAsync);

        groups[1].MapGet("/{id:int}/projects", ListProjectsAsync);
        groups[1].MapPost("/{id:int}/projects", AddProjectAsync);
        groups[1].MapDelete("/{id:int}/projects/{projectId:int}", RemoveProjectAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var page = PageRequest.From(http);
        var query = db.Projects.AsNoTracking();

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
            .Select(ToDto)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ProjectDto>(items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(int id, InfraOrgItDbContext db, CancellationToken ct)
    {
        var dto = await db.Projects.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Progetto", id) : Results.Ok(dto);
    }

    private static async Task<IResult> CreateAsync(
        ProjectInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);

        var entity = new Project
        {
            Name = Guard.Text(input.Name, "Name", 200),
            Description = Guard.OptionalText(input.Description, "Description", 500)
        };
        await EnsureUniqueAsync(db, entity.Name, null, ct);

        db.Projects.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await LoadAsync(db, entity.Id, ct));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ProjectInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.Projects.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Progetto", id);

        var name = Guard.Text(input.Name, "Name", 200);
        await EnsureUniqueAsync(db, name, id, ct);

        entity.Name = name;
        entity.Description = Guard.OptionalText(input.Description, "Description", 500);
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
        var entity = await db.Projects.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Progetto", id);

        db.Projects.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListApplicationsAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(x => x.Id == id, ct))
            throw Guard.NotFound("Progetto", id);

        var items = await db.Applications.AsNoTracking()
            .Where(x => x.Projects.Any(p => p.Id == id))
            .OrderBy(x => x.Name)
            .Select(ToSummary)
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> AddApplicationAsync(
        int id,
        ProjectApplicationInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);

        var project = await db.Projects.Include(x => x.Applications)
                          .SingleOrDefaultAsync(x => x.Id == id, ct)
                      ?? throw Guard.NotFound("Progetto", id);
        var application = await db.Applications.SingleOrDefaultAsync(x => x.Id == input.ApplicationId, ct)
                          ?? throw Guard.NotFound("Applicazione", input.ApplicationId);

        if (project.Applications.Any(x => x.Id == application.Id))
            return Results.NoContent();

        project.Applications.Add(application);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveApplicationAsync(
        int id,
        int applicationId,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);

        var project = await db.Projects.Include(x => x.Applications)
                          .SingleOrDefaultAsync(x => x.Id == id, ct)
                      ?? throw Guard.NotFound("Progetto", id);
        var application = project.Applications.SingleOrDefault(x => x.Id == applicationId)
                          ?? throw Guard.NotFound("Applicazione collegata", applicationId);

        project.Applications.Remove(application);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListProjectsAsync(
        int id,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        if (!await db.Applications.AnyAsync(x => x.Id == id, ct))
            throw Guard.NotFound("Applicazione", id);

        var items = await db.Projects.AsNoTracking()
            .Where(x => x.Applications.Any(a => a.Id == id))
            .OrderBy(x => x.Name)
            .Select(x => new ProjectSummaryDto(x.Id, x.Name, x.Description))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> AddProjectAsync(
        int id,
        ApplicationProjectInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);

        var application = await db.Applications.Include(x => x.Projects)
                              .SingleOrDefaultAsync(x => x.Id == id, ct)
                          ?? throw Guard.NotFound("Applicazione", id);
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == input.ProjectId, ct)
                      ?? throw Guard.NotFound("Progetto", input.ProjectId);

        if (application.Projects.Any(x => x.Id == project.Id))
            return Results.NoContent();

        application.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveProjectAsync(
        int id,
        int projectId,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);

        var application = await db.Applications.Include(x => x.Projects)
                              .SingleOrDefaultAsync(x => x.Id == id, ct)
                          ?? throw Guard.NotFound("Applicazione", id);
        var project = application.Projects.SingleOrDefault(x => x.Id == projectId)
                      ?? throw Guard.NotFound("Progetto collegato", projectId);

        application.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, string name, int? id, CancellationToken ct)
    {
        var exists = await db.Projects.AnyAsync(
            x => x.Name == name && (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict($"Esiste già un progetto chiamato {name}.");
    }

    private static async Task<ProjectDto> LoadAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.Projects.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleAsync(ct);

    internal static readonly Expression<Func<Project, ProjectDto>> ToDto = x => new ProjectDto(
        x.Id,
        x.Name,
        x.Description,
        x.Applications.Count,
        x.CreatedAt,
        x.UpdatedAt,
        x.Version);

    private static readonly Expression<Func<Application, ApplicationSummaryDto>> ToSummary =
        x => new ApplicationSummaryDto(x.Id, x.Name, x.Description, x.BasePath, x.ListeningOnPort);
}

public sealed record ProjectInput(string? Name, string? Description);

public sealed record ProjectDto(
    int Id,
    string Name,
    string? Description,
    int ApplicationCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

public sealed record ProjectSummaryDto(int Id, string Name, string? Description);

public sealed record ProjectApplicationInput(int ApplicationId);

public sealed record ApplicationProjectInput(int ProjectId);

using System.Security.Claims;
using System.Text.Json;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Remote;

public static class ApplicationActionsEndpoints
{
    private static readonly string[] BuiltInNames =
    [
        "ServerName", "Hostname", "IpAddress", "ApplicationName",
        "BasePath", "Version", "ExposedPort"
    ];

    public static void Map(WebApplication app)
    {
        var applications = app.MapGroup("/api/applications").RequireAuthorization();
        applications.MapGet("/{id:int}/actions", ListForApplicationAsync);
        applications.MapPost("/{id:int}/actions", CreateForApplicationAsync);

        var actions = app.MapGroup("/api/application-actions").RequireAuthorization();
        actions.MapPut("/{id:int}", UpdateAsync);
        actions.MapDelete("/{id:int}", DeleteAsync);

        var deploys = app.MapGroup("/api/server-applications").RequireAuthorization();
        deploys.MapGet("/{id:int}/actions", ListForDeployAsync);
        deploys.MapPost("/{id:int}/actions/{actionId:int}/run", RunAsync)
            .RequireRateLimiting("remote-run");
    }

    private static async Task<IResult> ListForApplicationAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        if (!await db.Applications.AnyAsync(x => x.Id == id, ct))
            throw Guard.NotFound("Applicazione", id);

        var actions = await LoadActionsAsync(db, id, ct);
        return Results.Ok(actions.Select(ToConfigDto).ToList());
    }

    private static async Task<IResult> CreateForApplicationAsync(
        int id,
        ApplicationActionInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        if (!await db.Applications.AnyAsync(x => x.Id == id, ct))
            throw Guard.NotFound("Applicazione", id);

        var entity = await ValidateAsync(input, db, ct);
        entity.ApplicationId = id;
        db.ApplicationActions.Add(entity);
        await db.SaveChangesAsync(ct);

        entity = await db.ApplicationActions.AsNoTracking()
            .Include(x => x.CommandTemplate)
            .SingleAsync(x => x.Id == entity.Id, ct);
        return Results.Ok(ToConfigDto(entity));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ApplicationActionInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.ApplicationActions.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Azione", id);

        var validated = await ValidateAsync(input, db, ct);
        entity.Label = validated.Label;
        entity.Order = validated.Order;
        entity.CommandTemplateId = validated.CommandTemplateId;
        entity.CustomScript = validated.CustomScript;
        entity.ParameterValuesJson = validated.ParameterValuesJson;
        entity.RequiresConfirmation = validated.RequiresConfirmation;
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
        var entity = await db.ApplicationActions.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Azione", id);

        db.ApplicationActions.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListForDeployAsync(
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var deploy = await LoadDeployAsync(db, id, ct);
        await access.RequireOperatorAsync(user, [deploy.Server!.ProviderEnvironmentId], ct);

        var actions = await LoadActionsAsync(db, deploy.ApplicationVersion!.ApplicationId, ct);
        return Results.Ok(actions.Select(x => new ApplicationActionButtonDto(
            x.Id, x.Label, x.Order, x.RequiresConfirmation)).ToList());
    }

    private static async Task<IResult> RunAsync(
        int id,
        int actionId,
        ApplicationActionRunInput? input,
        HttpContext http,
        ClaimsPrincipal user,
        ApplicationActionRunner runner,
        CancellationToken ct)
    {
        var result = await runner.RunAsync(
            user, id, actionId, http.Request.Headers["Idempotency-Key"].ToString(),
            input?.Confirm ?? false, ct);
        return Results.Ok(result);
    }

    private static async Task<ApplicationAction> ValidateAsync(
        ApplicationActionInput input,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var label = Guard.Text(input.Label, "Label", 100);
        var hasTemplate = input.CommandTemplateId is not null;
        var hasCustom = !string.IsNullOrWhiteSpace(input.CustomScript);
        if (hasTemplate == hasCustom)
            throw Guard.Unprocessable("Indicare un comando base oppure uno script custom.");

        string script;
        if (hasTemplate)
        {
            var template = await db.CommandTemplates.AsNoTracking()
                               .SingleOrDefaultAsync(x => x.Id == input.CommandTemplateId, ct)
                           ?? throw Guard.NotFound("Comando", input.CommandTemplateId!.Value);
            script = template.Script;
        }
        else
        {
            script = input.CustomScript!;
        }

        var values = input.ParameterValues ?? [];
        if (values.Count > 50)
            throw Guard.Unprocessable("Troppi parametri: massimo 50.");

        foreach (var pair in values)
        {
            if (pair.Key.Length is 0 or > 100 || pair.Value.Length > 500)
                throw Guard.Unprocessable("Parametro non valido: nome massimo 100, valore massimo 500.");
        }

        CommandRenderer.CheckValues(script, values);
        var missing = CommandRenderer.Placeholders(script)
            .Where(name => !BuiltInNames.Contains(name) && !values.ContainsKey(name))
            .ToList();
        if (missing.Count > 0)
            throw Guard.Unprocessable($"Segnaposto senza valore: {string.Join(", ", missing)}.");

        return new ApplicationAction
        {
            Label = label,
            Order = input.Order,
            CommandTemplateId = hasTemplate ? input.CommandTemplateId : null,
            CustomScript = hasTemplate ? null : script,
            ParameterValuesJson = JsonSerializer.Serialize(values),
            RequiresConfirmation = input.RequiresConfirmation
        };
    }

    private static async Task<List<ApplicationAction>> LoadActionsAsync(
        InfraOrgItDbContext db, int applicationId, CancellationToken ct) =>
        await db.ApplicationActions.AsNoTracking()
            .Include(x => x.CommandTemplate)
            .Where(x => x.ApplicationId == applicationId)
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

    private static async Task<ServerApplication> LoadDeployAsync(
        InfraOrgItDbContext db, int id, CancellationToken ct) =>
        await db.ServerApplications
            .Include(x => x.Server)!.ThenInclude(x => x!.ProviderEnvironment)
            .Include(x => x.Server)!.ThenInclude(x => x!.Credential)
            .Include(x => x.ApplicationVersion)!.ThenInclude(x => x!.Application)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw Guard.NotFound("Applicazione deployata", id);



    private static ApplicationActionConfigDto ToConfigDto(ApplicationAction action) => new(
        action.Id,
        action.ApplicationId,
        action.Label,
        action.Order,
        action.CommandTemplateId,
        action.CommandTemplate?.Name,
        action.CustomScript,
        ApplicationActionRunner.ParseParameterValues(action.ParameterValuesJson),
        action.RequiresConfirmation);
}

public sealed record ApplicationActionInput(
    string? Label,
    int Order,
    int? CommandTemplateId,
    string? CustomScript,
    Dictionary<string, string>? ParameterValues,
    bool RequiresConfirmation);

/// <summary>
/// Body of the action run endpoint. <c>confirm</c> must be true when the
/// action has <c>requiresConfirmation</c>; the API returns 422 otherwise
/// (PT-13).
/// </summary>
public sealed record ApplicationActionRunInput(bool Confirm = false);

public sealed record ApplicationActionConfigDto(
    int Id,
    int ApplicationId,
    string Label,
    int Order,
    int? CommandTemplateId,
    string? CommandTemplateName,
    string? CustomScript,
    Dictionary<string, string> ParameterValues,
    bool RequiresConfirmation);

public sealed record ApplicationActionButtonDto(
    int Id,
    string Label,
    int Order,
    bool RequiresConfirmation);

public sealed record CommandResultDto(int? ExitCode, string Output, bool TimedOut);

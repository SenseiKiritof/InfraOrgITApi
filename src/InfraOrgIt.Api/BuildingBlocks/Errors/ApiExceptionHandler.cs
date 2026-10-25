using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.BuildingBlocks.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            await WriteAsync(context, StatusCodes.Status409Conflict, "concurrent_update",
                "La risorsa è stata modificata da un altro utente: ricaricare e riprovare.");
            return true;
        }

        if (exception is DbUpdateException dbUpdate && IsDuplicateEntry(dbUpdate))
        {
            logger.LogWarning(dbUpdate, "Violazione di un vincolo di unicità");
            await WriteAsync(context, StatusCodes.Status409Conflict, "conflict",
                "Esiste già una risorsa con questi valori.");
            return true;
        }

        if (exception is not ApiException api)
            return false;

        logger.LogWarning(api, "ApiException {Code} ({Status})", api.Code, api.StatusCode);
        await WriteAsync(context, api.StatusCode, api.Code, api.Message);
        return true;
    }

    private static Task WriteAsync(HttpContext context, int statusCode, string code, string detail) =>
        Results.Problem(
                statusCode: statusCode,
                title: "Richiesta non valida",
                detail: detail,
                extensions: new Dictionary<string, object?> { ["code"] = code })
            .ExecuteAsync(context);

    private static bool IsDuplicateEntry(DbUpdateException exception) =>
        exception.ToString().Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase);
}

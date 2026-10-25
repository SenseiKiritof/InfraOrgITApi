using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

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

        if (exception is DbUpdateException foreignKey && IsForeignKeyViolation(foreignKey))
        {
            logger.LogWarning(foreignKey, "Violazione di un vincolo di chiave esterna");
            await WriteAsync(context, StatusCodes.Status409Conflict, "conflict",
                "La risorsa è usata da altri dati: rimuoverli prima.");
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
        HasMySqlError(exception, 1062) ||
        exception.ToString().Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase);

    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        HasMySqlError(exception, 1451) ||
        HasMySqlError(exception, 1452) ||
        exception.ToString().Contains("foreign key constraint", StringComparison.OrdinalIgnoreCase);

    private static bool HasMySqlError(Exception? exception, int number) =>
        exception switch
        {
            MySqlException mysql => mysql.Number == number || HasMySqlError(mysql.InnerException, number),
            null => false,
            _ => HasMySqlError(exception.InnerException, number)
        };
}

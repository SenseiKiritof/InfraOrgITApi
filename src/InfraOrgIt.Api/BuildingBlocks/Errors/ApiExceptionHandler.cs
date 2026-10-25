using Microsoft.AspNetCore.Diagnostics;

namespace InfraOrgIt.Api.BuildingBlocks.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ApiException api)
            return false;

        logger.LogWarning(api, "ApiException {Code} ({Status})", api.Code, api.StatusCode);

        await Results.Problem(
                statusCode: api.StatusCode,
                title: "Richiesta non valida",
                detail: api.Message,
                extensions: new Dictionary<string, object?> { ["code"] = api.Code })
            .ExecuteAsync(context);

        return true;
    }
}

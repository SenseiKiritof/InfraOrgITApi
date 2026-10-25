namespace InfraOrgIt.Api.BuildingBlocks.Errors;

public sealed class ApiException(int statusCode, string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

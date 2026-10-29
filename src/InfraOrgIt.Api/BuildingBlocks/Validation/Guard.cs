using System.Net;
using System.Net.Sockets;
using InfraOrgIt.Api.BuildingBlocks.Errors;

namespace InfraOrgIt.Api.BuildingBlocks.Validation;

public static class Guard
{
    public static string Text(string? value, string field, int maxLength, bool required = true)
    {
        var text = value?.Trim() ?? "";
        if (required && text.Length == 0)
            throw Unprocessable($"Il campo {field} è obbligatorio.");
        if (text.Length > maxLength)
            throw Unprocessable($"Il campo {field} supera {maxLength} caratteri.");
        return text;
    }

    public static string? OptionalText(string? value, string field, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        if (text.Length > maxLength)
            throw Unprocessable($"Il campo {field} supera {maxLength} caratteri.");
        return text;
    }

    public static int Port(int value, string field)
    {
        if (value is < 1 or > 65_535)
            throw Unprocessable($"Il campo {field} deve essere tra 1 e 65535.");
        return value;
    }

    public static string? OptionalIpAddress(string? value, string field)
    {
        var text = OptionalText(value, field, 45);
        if (text is null)
            return null;

        // TryParse alone reads "1" as 0.0.0.1: require four dotted parts for IPv4.
        if (!IPAddress.TryParse(text, out var address) ||
            (address.AddressFamily == AddressFamily.InterNetwork &&
             text.Split('.').Length != 4))
            throw Unprocessable($"Il campo {field} non è un indirizzo IP valido.");
        return text;
    }

    public static int? OptionalPositive(int? value, string field)
    {
        if (value is < 1)
            throw Unprocessable($"Il campo {field} deve essere maggiore di zero.");
        return value;
    }

    public static string? OptionalEnum<TEnum>(string? value, string field)
        where TEnum : struct, System.Enum =>
        string.IsNullOrWhiteSpace(value) ? null : Enum<TEnum>(value, field);

    public static string Enum<TEnum>(string? value, string field) where TEnum : struct, System.Enum
    {
        var text = value?.Trim() ?? "";
        if (!System.Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed))
            throw Unprocessable($"Il campo {field} non è tra i valori ammessi: {string.Join(", ", System.Enum.GetNames<TEnum>())}.");
        return parsed.ToString();
    }

    public static ApiException NotFound(string entity, int id) =>
        new(StatusCodes.Status404NotFound, "not_found", $"{entity} {id} non trovato.");

    public static ApiException Conflict(string message) =>
        new(StatusCodes.Status409Conflict, "conflict", message);

    public static ApiException Unprocessable(string message) =>
        new(StatusCodes.Status422UnprocessableEntity, "validation_failed", message);

    public static ApiException Forbidden(string message) =>
        new(StatusCodes.Status403Forbidden, "forbidden", message);
}

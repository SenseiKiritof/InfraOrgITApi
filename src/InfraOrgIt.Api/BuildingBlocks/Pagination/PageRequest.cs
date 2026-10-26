using InfraOrgIt.Api.BuildingBlocks.Errors;

namespace InfraOrgIt.Api.BuildingBlocks.Pagination;

public sealed record PageRequest(int Page, int PageSize, string? Sort, string? Q)
{
    public static PageRequest From(HttpContext http)
    {
        var query = http.Request.Query;
        return new PageRequest(
            ParseInt(query["page"], 1, 1, 100_000),
            ParseInt(query["pageSize"], 50, 1, 200),
            Text(query["sort"]),
            Text(query["q"]));
    }

    public static int? IntQuery(HttpContext http, string name)
    {
        var raw = http.Request.Query[name].ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (!int.TryParse(raw, out var value))
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                "validation_failed",
                $"Il parametro {name} deve essere un intero.");
        return value;
    }

    private static int ParseInt(string? raw, int fallback, int min, int max)
    {
        if (!int.TryParse(raw, out var value))
            return fallback;
        return Math.Clamp(value, min, max);
    }

    private static string? Text(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

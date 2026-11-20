using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace InfraOrgIt.Api.Features.Inventory;

public sealed record EolCycle(
    [property: JsonPropertyName("cycle")] string Cycle,
    [property: JsonPropertyName("lts")] bool Lts,
    [property: JsonPropertyName("eol")] string? Eol);

public sealed record OsCatalogResult(
    string Product,
    string? TargetVersion,
    bool TargetLts,
    DateOnly? CurrentEol,
    string? Detail);

public interface IEndOfLifeCatalog
{
    Task<OsCatalogResult?> LookupAsync(
        OsReleaseInfo info, bool ltsOnly, CancellationToken ct);
}

/// <summary>
/// endoflife.date lookup with a 24 h cache. The API is queried once per
/// product, not once per server. Failures return null and leave the current
/// version in place.
/// </summary>
public sealed class EndOfLifeCatalog(
    IHttpClientFactory factory,
    IMemoryCache cache,
    ILogger<EndOfLifeCatalog> logger) : IEndOfLifeCatalog
{
    public static string? ProductFor(string id) => id switch
    {
        "ubuntu" => "ubuntu",
        "debian" => "debian",
        "rhel" => "rhel",
        "almalinux" => "almalinux",
        "rocky" => "rockylinux",
        "ol" or "oracle" or "oraclelinux" => "oracle-linux",
        "sles" => "sles",
        _ => null
    };

    public async Task<OsCatalogResult?> LookupAsync(
        OsReleaseInfo info, bool ltsOnly, CancellationToken ct)
    {
        var product = ProductFor(info.Id);
        if (product is null) return null;
        var cycles = await CyclesAsync(product, ct);
        if (cycles is null || cycles.Count == 0) return null;

        var version = info.VersionId;
        var current = FindCycle(cycles, version);
        var newer = cycles
            .Where(cycle => Compare(cycle.Cycle, version) > 0)
            .OrderBy(cycle => cycle.Cycle, Comparer<string>.Create(Compare))
            .ToList();
        var candidates = ltsOnly
            ? newer.Where(cycle => IsLts(product, cycle)).ToList()
            : newer;
        var target = candidates.FirstOrDefault();
        string? detail = null;
        if (target is null && ltsOnly && newer.Count > 0)
            detail = $"Disponibile solo {newer[0].Cycle} (non-LTS)";
        return new OsCatalogResult(
            product,
            target?.Cycle,
            target is not null && IsLts(product, target),
            ParseEol(current?.Eol),
            detail);
    }

    /// <summary>RHEL, SLES and Debian stable are always LTS for this product.</summary>
    private static bool IsLts(string product, EolCycle cycle) =>
        product is "rhel" or "almalinux" or "rockylinux" or "oracle-linux" or "sles" or "debian"
        || cycle.Lts;

    private static EolCycle? FindCycle(IReadOnlyList<EolCycle> cycles, string version)
    {
        var exact = cycles.FirstOrDefault(cycle =>
            string.Equals(cycle.Cycle, version, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var major = version.Split('.')[0];
        exact = cycles.FirstOrDefault(cycle =>
            string.Equals(cycle.Cycle, major, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        return cycles
            .Where(cycle =>
                cycle.Cycle.Split('.')[0] == major &&
                Compare(cycle.Cycle, version) <= 0)
            .OrderByDescending(cycle => Compare(cycle.Cycle, version))
            .FirstOrDefault();
    }

    private async Task<IReadOnlyList<EolCycle>?> CyclesAsync(
        string product, CancellationToken ct)
    {
        var key = $"endoflife:{product}";
        if (cache.TryGetValue(key, out IReadOnlyList<EolCycle>? cached) && cached is not null)
            return cached;
        try
        {
            var client = factory.CreateClient("endoflife");
            var cycles = await client.GetFromJsonAsync<List<EolCycle>>(
                $"{product}.json", ct);
            if (cycles is null) return null;
            cache.Set(key, cycles, TimeSpan.FromHours(24));
            return cycles;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Lookup endoflife.date non riuscito per {Product}", product);
            return null;
        }
    }

    private static DateOnly? ParseEol(string? value) =>
        DateOnly.TryParse(value, out var date) ? date : null;

    private static int Compare(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var index = 0; index < Math.Max(a.Length, b.Length); index += 1)
        {
            var first = index < a.Length && int.TryParse(a[index], out var one) ? one : 0;
            var second = index < b.Length && int.TryParse(b[index], out var two) ? two : 0;
            if (first != second) return first.CompareTo(second);
        }
        return 0;
    }
}

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed class FrontendOptions
{
    public string BaseUrl { get; set; } = "";
    public string[] AllowedOrigins { get; set; } = [];

    public string[] EffectiveOrigins => (AllowedOrigins.Length != 0 ? AllowedOrigins : [BaseUrl])
        .Where(o => !string.IsNullOrWhiteSpace(o))
        .Select(o => o.Trim().TrimEnd('/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

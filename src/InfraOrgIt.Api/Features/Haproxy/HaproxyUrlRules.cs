using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;

namespace InfraOrgIt.Api.Features.Haproxy;

/// <summary>
/// URL rules for endpoints the API connects to: http(s) only, no credentials,
/// query or fragment. Direct transports must target the linked server, or the
/// caller must be SystemAdmin (SSRF override).
/// </summary>
public static class HaproxyUrlRules
{
    public static Uri Parse(string value, string field)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.Query.Length > 0 ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw Guard.Unprocessable(
                $"{field} deve essere un URL http o https senza credenziali, query o frammenti.");
        return uri;
    }

    public static bool HostMatchesServer(Uri uri, Server server) =>
        Same(uri.Host, server.IpAddress) ||
        Same(uri.Host, server.Hostname) ||
        Same(uri.Host, server.PublicIpAddress);

    private static bool Same(string host, string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) &&
        string.Equals(Normalize(host), Normalize(candidate),
            StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        value.Trim().Trim('[', ']').TrimEnd('.');
}

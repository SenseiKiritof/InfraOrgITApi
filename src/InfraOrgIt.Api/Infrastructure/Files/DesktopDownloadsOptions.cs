using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace InfraOrgIt.Api.Infrastructure.Files;

public sealed class DesktopDownloadsOptions
{
    public const string SectionName = "DesktopDownloads";

    /// <summary>
    /// One-time signed download URLs for the desktop bridge. When disabled,
    /// capabilities report no signed URL and ticket creation is rejected.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Lifetime of a signed ticket in seconds.</summary>
    public int TicketLifetimeSeconds { get; set; } = 120;

    /// <summary>
    /// Optional absolute public origin (for example behind a reverse proxy).
    /// When empty the ticket URL uses the current request scheme and host.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}

public static class DownloadTicketTokens
{
    private const string Prefix = "dt_";

    public static string Create() =>
        Prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>SHA-256 HEX of the raw token; the database never stores the raw value.</summary>
    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))
            .ToLowerInvariant();
}

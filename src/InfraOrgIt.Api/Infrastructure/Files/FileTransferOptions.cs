using InfraOrgIt.Api.BuildingBlocks.Errors;

namespace InfraOrgIt.Api.Infrastructure.Files;

public sealed class FileTransferOptions
{
    public const string SectionName = "FileTransfer";

    /// <summary>
    /// Plain FTP is cleartext (PT-16). It stays available in development for
    /// legacy targets; deploy sets it false and uses SFTP or FTPS.
    /// </summary>
    public bool AllowPlainFtp { get; set; } = true;
}

/// <summary>Shared guard for both the save endpoint and the connector.</summary>
public static class FileTransferPolicy
{
    public const string PlainFtpProtocol = "Ftp";

    public static void EnsureProtocolAllowed(string? protocol, bool allowPlainFtp)
    {
        if (protocol == PlainFtpProtocol && !allowPlainFtp)
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "plain_ftp_disabled",
                "Il trasferimento FTP non cifrato è disattivato. Usare SFTP o FTPS.");
    }
}

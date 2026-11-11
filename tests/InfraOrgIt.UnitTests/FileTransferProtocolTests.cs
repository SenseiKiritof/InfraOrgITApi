using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Features.Remote;
using InfraOrgIt.Api.Infrastructure.Files;
using Microsoft.AspNetCore.Http;

namespace InfraOrgIt.UnitTests;

public sealed class FileTransferProtocolTests
{
    [Theory]
    [InlineData("Sftp", "Sftp")]
    [InlineData("ftp", "Ftp")]
    [InlineData("FTPS", "Ftps")]
    public void Accepts_supported_protocols(string input, string expected)
    {
        Assert.Equal(
            expected,
            Guard.Enum<FileTransferEndpoints.FileTransferProtocol>(input, "Protocol"));
    }

    [Fact]
    public void Rejects_unknown_protocols()
    {
        var exception = Assert.Throws<ApiException>(() =>
            Guard.Enum<FileTransferEndpoints.FileTransferProtocol>("Smtp", "Protocol"));
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, exception.StatusCode);
        Assert.Equal("validation_failed", exception.Code);
        Assert.Contains("Ftps", exception.Message);
    }

    [Theory]
    [InlineData(FileTransferEndpoints.FileTransferProtocol.Sftp, 22)]
    [InlineData(FileTransferEndpoints.FileTransferProtocol.Ftp, 21)]
    [InlineData(FileTransferEndpoints.FileTransferProtocol.Ftps, 21)]
    public void Default_port_matches_the_protocol(
        FileTransferEndpoints.FileTransferProtocol protocol, int expected)
    {
        Assert.Equal(expected, FileTransferEndpoints.DefaultPort(protocol.ToString()));
    }

    [Fact]
    public void Plain_ftp_is_rejected_by_the_shared_policy()
    {
        FileTransferPolicy.EnsureProtocolAllowed(
            FileTransferPolicy.PlainFtpProtocol, allowPlainFtp: true);

        var exception = Assert.Throws<ApiException>(() =>
            FileTransferPolicy.EnsureProtocolAllowed(
                FileTransferPolicy.PlainFtpProtocol, allowPlainFtp: false));
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, exception.StatusCode);
        Assert.Equal("plain_ftp_disabled", exception.Code);
    }

    [Theory]
    [InlineData("Sftp")]
    [InlineData("Ftps")]
    public void Other_protocols_are_not_blocked_by_the_plain_ftp_switch(
        string protocol)
    {
        FileTransferPolicy.EnsureProtocolAllowed(protocol, allowPlainFtp: false);
    }
}

using FluentFTP;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.Infrastructure.Files;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.UnitTests;

public sealed class FileTransferConnectorTests
{
    [Fact]
    public void Ftps_uses_explicit_tls_on_control_and_data_connections()
    {
        var config = FileTransferConnector.CreateFtpConfig(explicitTls: true);

        Assert.Equal(FtpEncryptionMode.Explicit, config.EncryptionMode);
        Assert.True(config.DataConnectionEncryption);
        Assert.False(config.ValidateAnyCertificate);
    }

    [Fact]
    public void Plain_ftp_remains_cleartext()
    {
        var config = FileTransferConnector.CreateFtpConfig(explicitTls: false);

        Assert.Equal(FtpEncryptionMode.None, config.EncryptionMode);
        Assert.False(config.DataConnectionEncryption);
        Assert.False(config.ValidateAnyCertificate);
    }

    [Fact]
    public async Task Plain_ftp_is_rejected_when_the_switch_is_off()
    {
        var connector = new FileTransferConnector(
            null!,
            null!,
            Options.Create(new FileTransferOptions { AllowPlainFtp = false }),
            NullLogger<FileTransferConnector>.Instance);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            connector.ConnectAsync(
                new Server { Name = "legacy", FileTransferProtocol = "Ftp" },
                CancellationToken.None));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, exception.StatusCode);
        Assert.Equal("plain_ftp_disabled", exception.Code);
    }
}

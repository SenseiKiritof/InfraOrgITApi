using FluentFTP;
using InfraOrgIt.Api.Infrastructure.Files;

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
}

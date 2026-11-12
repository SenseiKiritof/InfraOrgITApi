using FluentFTP;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Infrastructure.Files;

/// <summary>Sceglie il protocollo configurato sul server: SFTP (via catena SSH), FTP o FTPS.</summary>
public sealed class FileTransferConnector(
    ISshConnector ssh,
    ServerCredentialResolver credentials,
    IOptions<FileTransferOptions> options,
    ILogger<FileTransferConnector> logger) : IFileTransferConnector
{
    public async Task<IFileTransferSession> ConnectAsync(
        Server server,
        CancellationToken ct)
    {
        FileTransferPolicy.EnsureProtocolAllowed(
            server.FileTransferProtocol, options.Value.AllowPlainFtp);

        var credential = server.FileTransferCredential
                         ?? server.Credential
                         ?? throw new ApiException(
                             StatusCodes.Status422UnprocessableEntity,
                             "credential_missing",
                             $"Il server {server.Name} non ha una credenziale per il trasferimento file.");

        var port = server.FileTransferPort ??
                   (server.FileTransferProtocol == "Sftp" ? 22 : 21);

        switch (server.FileTransferProtocol)
        {
            case "Sftp":
                return await ssh.OpenSftpAsync(server, credential, port, ct);
            case "Ftp":
                return new FtpFileSession(
                    await ConnectFtpAsync(server, credential, port, false, ct));
            case "Ftps":
                return new FtpFileSession(
                    await ConnectFtpAsync(server, credential, port, true, ct));
            case null or "":
                throw new ApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "file_transfer_not_configured",
                    $"Il server {server.Name} non ha un accesso SFTP, FTP o FTPS configurato.");
            default:
                throw new ApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "file_transfer_protocol",
                    $"Protocollo di trasferimento non supportato: {server.FileTransferProtocol}.");
        }
    }

    /// <summary>
    /// Explicit TLS (AUTH TLS) with certificate validation kept on. Plain FTP
    /// stays cleartext for legacy servers.
    /// </summary>
    internal static FtpConfig CreateFtpConfig(bool explicitTls) => new()
    {
        EncryptionMode = explicitTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None,
        DataConnectionEncryption = explicitTls,
        // Never accept an invalid certificate silently.
        ValidateAnyCertificate = false,
        ConnectTimeout = 10_000,
        DataConnectionConnectTimeout = 10_000,
        DataConnectionReadTimeout = 30_000
    };

    private async Task<AsyncFtpClient> ConnectFtpAsync(
        Server server,
        ServerCredential credential,
        int port,
        bool explicitTls,
        CancellationToken ct)
    {
        if (credential.AuthType != "Password")
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "ftp_requires_password",
                "Il protocollo FTP/FTPS richiede una credenziale con password.");

        var (username, secret, _) = credentials.Resolve(credential);
        var host = string.IsNullOrWhiteSpace(server.IpAddress)
            ? server.Hostname
            : server.IpAddress;
        var client = new AsyncFtpClient(
            host, username, secret, port, CreateFtpConfig(explicitTls));
        client.ValidateCertificate += (_, args) =>
            args.Accept = args.PolicyErrors == System.Net.Security.SslPolicyErrors.None;
        try
        {
            await client.Connect(ct);
            return client;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            client.Dispose();
            logger.LogWarning(
                exception,
                "Connessione {Protocol} fallita per il server {ServerId} ({Credential})",
                explicitTls ? "FTPS" : "FTP",
                server.Id,
                credential.Name);
            throw new ApiException(
                StatusCodes.Status502BadGateway,
                explicitTls ? "ftps_unreachable" : "ftp_unreachable",
                $"Connessione {(explicitTls ? "FTPS" : "FTP")} a {server.Name} fallita: {exception.Message}");
        }
    }

    private sealed class FtpFileSession(AsyncFtpClient client) : IFileTransferSession
    {
        // FTP/FTPS non espongono una home affidabile dopo il login: si parte
        // dalla radice del server come prima.
        public string HomePath => "/";

        public async Task<IReadOnlyList<RemoteFileEntry>> ListAsync(
            string path,
            CancellationToken ct)
        {
            var normalized = RemotePath.Normalize(path);
            var items = await client.GetListing(normalized, ct);
            return items
                .Where(item => item.Type is FtpObjectType.File
                    or FtpObjectType.Directory)
                .Select(item => new RemoteFileEntry(
                    item.Name,
                    RemotePath.Combine(normalized, item.Name),
                    item.Type == FtpObjectType.Directory,
                    item.Type == FtpObjectType.Directory ? 0 : item.Size,
                    item.Modified == DateTime.MinValue ? null : item.Modified.ToUniversalTime()))
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task DownloadAsync(
            string path,
            Stream destination,
            CancellationToken ct) =>
            await client.DownloadStream(
                destination, RemotePath.Normalize(path), token: ct);

        public async Task UploadAsync(
            string path,
            Stream source,
            CancellationToken ct) =>
            await client.UploadStream(
                source,
                RemotePath.Normalize(path),
                FtpRemoteExists.Overwrite,
                false,
                token: ct);

        public async Task CreateDirectoryAsync(string path, CancellationToken ct) =>
            await client.CreateDirectory(RemotePath.Normalize(path), ct);

        public async Task RenameAsync(
            string path,
            string newPath,
            bool isDirectory,
            CancellationToken ct)
        {
            if (isDirectory)
                await client.MoveDirectory(
                    RemotePath.Normalize(path),
                    RemotePath.Normalize(newPath),
                    FtpRemoteExists.Overwrite,
                    ct);
            else
                await client.MoveFile(
                    RemotePath.Normalize(path),
                    RemotePath.Normalize(newPath),
                    FtpRemoteExists.Overwrite,
                    ct);
        }

        public async Task DeleteAsync(
            string path,
            bool isDirectory,
            CancellationToken ct)
        {
            if (isDirectory)
                await client.DeleteDirectory(RemotePath.Normalize(path), ct);
            else
                await client.DeleteFile(RemotePath.Normalize(path), ct);
        }

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

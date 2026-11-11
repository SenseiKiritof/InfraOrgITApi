using InfraOrgIt.Api.Infrastructure.Persistence.Entities;

namespace InfraOrgIt.Api.Infrastructure.Files;

public sealed record RemoteFileEntry(
    string Name,
    string Path,
    bool IsDirectory,
    long Size,
    DateTime? ModifiedUtc);

/// <summary>Session opened on a server: SFTP over SSH, plain FTP or FTPS.</summary>
public interface IFileTransferSession : IAsyncDisposable
{
    Task<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken ct);
    Task DownloadAsync(string path, Stream destination, CancellationToken ct);
    Task UploadAsync(string path, Stream source, CancellationToken ct);
    Task CreateDirectoryAsync(string path, CancellationToken ct);
    Task RenameAsync(string path, string newPath, bool isDirectory, CancellationToken ct);
    Task DeleteAsync(string path, bool isDirectory, CancellationToken ct);
}

public interface IFileTransferConnector
{
    Task<IFileTransferSession> ConnectAsync(Server server, CancellationToken ct);
}

public static class RemotePath
{
    /// <summary>Percorso POSIX assoluto e normalizzato, senza "..".</summary>
    public static string Normalize(string? path)
    {
        var raw = string.IsNullOrWhiteSpace(path) ? "/" : path.Replace('\\', '/');
        var segments = new List<string>();
        foreach (var segment in raw.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return "/" + string.Join('/', segments);
    }

    public static string Combine(string directory, string name) =>
        Normalize($"{(directory.EndsWith('/') ? directory : directory + "/")}{name}");

    public static string Name(string path)
    {
        var normalized = Normalize(path);
        var index = normalized.LastIndexOf('/');
        return index < 0 ? normalized : normalized[(index + 1)..];
    }
}

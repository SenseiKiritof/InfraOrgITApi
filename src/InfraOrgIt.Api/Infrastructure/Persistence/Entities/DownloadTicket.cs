namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// One-time signed download ticket for the desktop bridge. Only the SHA-256
/// hash of the raw token is stored; the path is already normalized.
/// </summary>
public sealed class DownloadTicket : EntityBase
{
    public string TokenHash { get; set; } = "";
    public int UserId { get; set; }
    public int ServerId { get; set; }
    public string Path { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }

    public User? User { get; set; }
    public Server? Server { get; set; }
}

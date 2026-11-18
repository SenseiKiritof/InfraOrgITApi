namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// Desktop installation enrolled by a user. The public key encrypts downloaded
/// credential payloads; the private key never leaves the device. Revoked rows
/// are kept for audit.
/// </summary>
public sealed class DesktopDevice : EntityBase
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Name { get; set; } = "";
    public string PublicKeyPem { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public DateTime LastSeenAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

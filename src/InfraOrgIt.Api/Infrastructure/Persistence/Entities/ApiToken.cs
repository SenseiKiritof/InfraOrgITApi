namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

// Personal access token for non-browser clients (MCP, IDE, automation).
// Only the SHA-256 hash is stored; the raw value is shown once on creation.
public sealed class ApiToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public string Scopes { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public User? User { get; set; }
}

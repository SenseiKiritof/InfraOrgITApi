namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class SessionRecord
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

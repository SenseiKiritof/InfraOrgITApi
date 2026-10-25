namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ServerCredential : EntityBase
{
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
    public string AuthType { get; set; } = "Password";
    public string EncryptedSecret { get; set; } = "";
    public string? EncryptedPassphrase { get; set; }
    public int KeyVersion { get; set; }
    public bool NeedsReauth { get; set; }
    public List<Server> Servers { get; set; } = [];
}

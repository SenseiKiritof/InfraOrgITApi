namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class Server : EntityBase
{
    public int ProviderEnvironmentId { get; set; }
    public string Name { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string Hostname { get; set; } = "";
    public int SshPort { get; set; } = 22;

    // FK verso ServerCredentials aggiunta in Fase 5, quando l'entità esiste.
    public int? CredentialId { get; set; }
    public string? HostKeyFingerprint { get; set; }

    public ProviderEnvironment? ProviderEnvironment { get; set; }
    public List<ServerApplication> ServerApplications { get; set; } = [];
}

namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class Server : EntityBase
{
    public int ProviderEnvironmentId { get; set; }
    public string Name { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string Hostname { get; set; } = "";
    public int SshPort { get; set; } = 22;

    public int? CredentialId { get; set; }
    public string? HostKeyFingerprint { get; set; }

    public int? JumpServerId { get; set; }

    public ProviderEnvironment? ProviderEnvironment { get; set; }
    public ServerCredential? Credential { get; set; }
    public Server? JumpServer { get; set; }
    public List<ServerApplication> ServerApplications { get; set; } = [];
    public List<ReachabilityTest> ReachabilityTests { get; set; } = [];
}

namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class Server : EntityBase
{
    public int ProviderEnvironmentId { get; set; }
    public string Name { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string Hostname { get; set; } = "";
    public int SshPort { get; set; } = 22;

    public string? PublicIpAddress { get; set; }
    public int? CpuCores { get; set; }
    public int? MemoryGb { get; set; }
    public int? DiskGb { get; set; }
    public string? OperatingSystem { get; set; }
    public string? HaNode { get; set; }
    public string? ManagedBy { get; set; }

    public int? CredentialId { get; set; }
    public string? HostKeyFingerprint { get; set; }

    public int? JumpServerId { get; set; }

    public ProviderEnvironment? ProviderEnvironment { get; set; }
    public ServerCredential? Credential { get; set; }
    public Server? JumpServer { get; set; }
    public List<ServerApplication> ServerApplications { get; set; } = [];
    public List<ReachabilityTest> ReachabilityTests { get; set; } = [];
}

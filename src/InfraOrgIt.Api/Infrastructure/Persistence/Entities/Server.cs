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
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public string? OsUpdateTarget { get; set; }
    public bool? OsUpdateTargetLts { get; set; }
    public DateTime? OsEolDate { get; set; }
    public string? OsUpdateStatus { get; set; }
    public string? OsUpdateDetail { get; set; }
    public DateTime? OsUpdateCheckedAt { get; set; }
    /// <summary>null eredita il default globale; true solo LTS; false anche non-LTS.</summary>
    public bool? OsUpdateLtsOnly { get; set; }
    public string? HaNode { get; set; }
    public string? ManagedBy { get; set; }

    public int? CredentialId { get; set; }
    public string? HostKeyFingerprint { get; set; }

    /// <summary>Sftp, Ftp; null quando il trasferimento file non è configurato.</summary>
    public string? FileTransferProtocol { get; set; }
    public int? FileTransferPort { get; set; }
    public int? FileTransferCredentialId { get; set; }

    public int? JumpServerId { get; set; }

    public ProviderEnvironment? ProviderEnvironment { get; set; }
    public ServerCredential? Credential { get; set; }
    public ServerCredential? FileTransferCredential { get; set; }
    public Server? JumpServer { get; set; }
    public List<ServerApplication> ServerApplications { get; set; } = [];
    public List<ReachabilityTest> ReachabilityTests { get; set; } = [];
    public List<TunnelService> TunnelServices { get; set; } = [];
}

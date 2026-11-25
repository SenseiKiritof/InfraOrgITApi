namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// HAProxy node discovered on a server. Discovery only creates a candidate:
/// the collector and Data Plane API calls start after the user enables
/// monitoring.
/// </summary>
public sealed class HaproxyInstance : EntityBase
{
    public int ServerId { get; set; }
    public int? ServerApplicationId { get; set; }
    public int? ClusterId { get; set; }
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    /// <summary>Direct, SshForward or DesktopRelay.</summary>
    public string Transport { get; set; } = "SshForward";
    public string ApiHost { get; set; } = "127.0.0.1";
    public int ApiPort { get; set; } = 5555;
    /// <summary>auto, v2, v3 or stats.</summary>
    public string ApiVersion { get; set; } = "auto";
    public string? Username { get; set; }
    public string? EncryptedPassword { get; set; }
    public int? PasswordKeyVersion { get; set; }
    public string? StatsUsername { get; set; }
    public string? EncryptedStatsPassword { get; set; }
    public int? StatsPasswordKeyVersion { get; set; }
    public string? CaPem { get; set; }
    public bool AllowSelfSigned { get; set; }
    /// <summary>Undecided, Enabled or Disabled.</summary>
    public string MonitoringChoice { get; set; } = "Undecided";
    public DateTime? MonitoringChangedAt { get; set; }
    public string? MonitoringChangedBy { get; set; }
    /// <summary>Auto or Manual.</summary>
    public string DiscoverySource { get; set; } = "Auto";
    /// <summary>Found, Inactive, CredentialsRequired, Unreachable or Ready.</summary>
    public string DiscoveryStatus { get; set; } = "Found";
    public string? DetectedVersion { get; set; }
    public string? DetectedConfigPath { get; set; }
    public string? EffectiveApiVersion { get; set; }
    public DateTime? LastRefreshAt { get; set; }
    public string? LastRefreshStatus { get; set; }
    public string? LastRefreshDetail { get; set; }
    public int? ConfigurationVersion { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? LastTestAt { get; set; }
    public string? LastTestStatus { get; set; }
    public string? LastTestDetail { get; set; }

    public Server? Server { get; set; }
    public ServerApplication? ServerApplication { get; set; }
    public HaproxyCluster? Cluster { get; set; }
    public List<HaproxyRouteSnapshot> Routes { get; set; } = [];
    public List<HaproxyServerSnapshot> Servers { get; set; } = [];
    public List<HaproxyCertificateSnapshot> Certificates { get; set; } = [];
}

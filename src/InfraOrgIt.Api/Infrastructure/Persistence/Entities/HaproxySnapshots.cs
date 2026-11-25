namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>Last known live route from HAProxy; rebuilt on every refresh.</summary>
public sealed class HaproxyRouteSnapshot : EntityBase
{
    public int HaproxyInstanceId { get; set; }
    public string Frontend { get; set; } = "";
    public string Bind { get; set; } = "";
    public string Host { get; set; } = "";
    public string Path { get; set; } = "/";
    public string Backend { get; set; } = "";
    public bool IsDefault { get; set; }
    public string? RawCondition { get; set; }
    public int? ConfigurationVersion { get; set; }
    public DateTime CollectedAt { get; set; }

    public HaproxyInstance? HaproxyInstance { get; set; }
}

/// <summary>Last known live backend server from HAProxy; rebuilt on every refresh.</summary>
public sealed class HaproxyServerSnapshot : EntityBase
{
    public int HaproxyInstanceId { get; set; }
    public string Backend { get; set; } = "";
    public string Server { get; set; } = "";
    public string Address { get; set; } = "";
    public string Status { get; set; } = "";
    public string? CheckStatus { get; set; }
    public int? Weight { get; set; }
    public DateTime CollectedAt { get; set; }

    public HaproxyInstance? HaproxyInstance { get; set; }
}

/// <summary>Certificate metadata, never the private key.</summary>
public sealed class HaproxyCertificateSnapshot : EntityBase
{
    public int HaproxyInstanceId { get; set; }
    public string StorageName { get; set; } = "";
    public string Path { get; set; } = "";
    public string Domains { get; set; } = "";
    public string Issuer { get; set; } = "";
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }
    public long Size { get; set; }
    public DateTime CollectedAt { get; set; }

    public HaproxyInstance? HaproxyInstance { get; set; }
}

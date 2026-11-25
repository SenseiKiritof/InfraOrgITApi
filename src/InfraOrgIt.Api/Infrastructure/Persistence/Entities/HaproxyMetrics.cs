namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// One HAProxy stats snapshot. Counters are cumulative; the query layer turns
/// them into deltas. ResolutionMinutes is 1 or 5.
/// </summary>
public sealed class HaproxyMetricSample : EntityBase
{
    public int HaproxyInstanceId { get; set; }
    public int ResolutionMinutes { get; set; }
    public DateTime Bucket { get; set; }
    public string Kind { get; set; } = "";
    public string Proxy { get; set; } = "";
    public string Server { get; set; } = "";
    public string Status { get; set; } = "";
    public string? CheckStatus { get; set; }
    public long Req { get; set; }
    public long H1xx { get; set; }
    public long H2xx { get; set; }
    public long H3xx { get; set; }
    public long H4xx { get; set; }
    public long H5xx { get; set; }
    public long HOther { get; set; }
    public long Bin { get; set; }
    public long Bout { get; set; }
    public long Econ { get; set; }
    public long Eresp { get; set; }
    public long Dreq { get; set; }
    public long Dresp { get; set; }
    public long CliAbrt { get; set; }
    public long SrvAbrt { get; set; }
    public long ConnTot { get; set; }
    public long Dcon { get; set; }
    public long Dses { get; set; }
    public long Lbtot { get; set; }
    public long Wretr { get; set; }
    public long Wredis { get; set; }
    public long Scur { get; set; }
    public long Smax { get; set; }
    public long Slim { get; set; }
    public long Qcur { get; set; }
    public long Qmax { get; set; }
    public long Rate { get; set; }
    public long RateMax { get; set; }
    public long ReqRate { get; set; }
    public long ReqRateMax { get; set; }
    public long ConnRate { get; set; }
    public long ConnRateMax { get; set; }
    public long Rtime { get; set; }
    public long Ctime { get; set; }
    public long Ttime { get; set; }
    public long Qtime { get; set; }
    public long Downtime { get; set; }
    public long Lastsess { get; set; }
    public long Weight { get; set; }
    public long CheckHealth { get; set; }
    public long Chkfail { get; set; }
    public long Chkdown { get; set; }

    public HaproxyInstance? HaproxyInstance { get; set; }
}

public sealed class HaproxyProcessMetricSample : EntityBase
{
    public int HaproxyInstanceId { get; set; }
    public int ResolutionMinutes { get; set; }
    public DateTime Bucket { get; set; }
    public long Uptime { get; set; }
    public long CurrConns { get; set; }
    public long CumConns { get; set; }
    public long CurrSslConns { get; set; }
    public long CumSslConns { get; set; }
    public long MaxConn { get; set; }
    public long SessRate { get; set; }
    public long ConnRate { get; set; }
    public long Tasks { get; set; }
    public long RunQueue { get; set; }
    public long IdlePct { get; set; }
    public long MaxMemoryMb { get; set; }
    public long Threads { get; set; }
    public long ActivePeers { get; set; }
    public long ConnectedPeers { get; set; }
    public long FailedResolutions { get; set; }
    public long DroppedLogs { get; set; }
    public long BytesOutRate { get; set; }

    public HaproxyInstance? HaproxyInstance { get; set; }
}

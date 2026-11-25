namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class HaproxyProtocolMetricSample : EntityBase
{
    public int HaproxyInstanceId { get; set; }
    public int ResolutionMinutes { get; set; }
    public DateTime Bucket { get; set; }
    public string Kind { get; set; } = "";
    public string Proxy { get; set; } = "";
    public string Server { get; set; } = "";
    public long H2Req { get; set; }
    public long H2Sess { get; set; }
    public long H3Req { get; set; }
    public long H3Sess { get; set; }
    public long H2OpenConnections { get; set; }
    public long H2BackendOpenStreams { get; set; }
    public long H2ConnErrors { get; set; }
    public long H2StreamErrors { get; set; }
    public long QuicHandshakeFail { get; set; }
    public long QuicDroppedPkt { get; set; }
    public long QuicLostPkt { get; set; }
    public long QuicSentPkt { get; set; }
    public long QuicRetryError { get; set; }
    public long QuicTooShortDgram { get; set; }
    public long QuicRxbufFull { get; set; }
    public long QuicSocketFull { get; set; }
    public long H3VersionFallback { get; set; }
    public long H3ProtocolErrors { get; set; }
    public long QuicTransportErrors { get; set; }
    public long QpackEncoderErrors { get; set; }
    public long QpackDecoderErrors { get; set; }
    public long SslFailedHandshake { get; set; }

    public HaproxyInstance? HaproxyInstance { get; set; }
}

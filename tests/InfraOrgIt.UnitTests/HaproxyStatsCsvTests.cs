using InfraOrgIt.Api.Features.Haproxy;

namespace InfraOrgIt.UnitTests;

public sealed class HaproxyStatsCsvTests
{
    [Fact]
    public void Parses_protocol_metrics_and_aggregates_errors()
    {
        const string csv = """
            # pxname,svname,h2req,h2sess,h3req,h3sess,h2_open_connections,h2_backend_open_streams,h2_detected_conn_protocol_errors,h2_detected_strm_protocol_errors,quic_hdshk_fail,quic_dropped_pkt,quic_lost_pkt,quic_sent_pkt,quic_retry_error,quic_too_short_dgram,quic_rxbuf_full,quic_socket_full,h3_version_fallback,h3_request_cancelled,h3_general_protocol_error,quic_transp_err_connection_refused,quic_transp_err_crypto_error,qpack_encoder_stream_error,qpack_decoder_stream_error,ssl_failed_handshake
            public,FRONTEND,10,2,20,3,4,5,1,2,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21
            """;

        var rows = HaproxyStatsCsv.ParseProtocol(csv);

        var row = Assert.Single(rows);
        Assert.Equal("frontend", row.Kind);
        Assert.Equal("public", row.Proxy);
        Assert.Equal(10, row.H2Req);
        Assert.Equal(20, row.H3Req);
        Assert.Equal(6, row.QuicHandshakeFail);
        Assert.Equal(15 + 16, row.H3ProtocolErrors);
        Assert.Equal(17 + 18, row.QuicTransportErrors);
        Assert.Equal(21, row.SslFailedHandshake);
    }
}

using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;

namespace InfraOrgIt.Api.Features.Haproxy;

public sealed record HaproxyProtocolRow(
    string Kind,
    string Proxy,
    string Server,
    long H2Req,
    long H2Sess,
    long H3Req,
    long H3Sess,
    long H2OpenConnections,
    long H2BackendOpenStreams,
    long H2ConnErrors,
    long H2StreamErrors,
    long QuicHandshakeFail,
    long QuicDroppedPkt,
    long QuicLostPkt,
    long QuicSentPkt,
    long QuicRetryError,
    long QuicTooShortDgram,
    long QuicRxbufFull,
    long QuicSocketFull,
    long H3VersionFallback,
    long H3ProtocolErrors,
    long QuicTransportErrors,
    long QpackEncoderErrors,
    long QpackDecoderErrors,
    long SslFailedHandshake);

/// <summary>Reads the HAProxy stats page in CSV form and applies server actions.</summary>
public sealed class HaproxyStatsClient(
    HttpClient http,
    ISshSession? ssh) : IAsyncDisposable
{
    public async Task<string> GetCsvAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync(";csv", ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Stats HAProxy HTTP {(int)response.StatusCode}.");
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task SetServerStateAsync(
        string backend,
        string server,
        string state,
        CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(
        [
            new("s", server),
            new("action", state),
            new("b", backend)
        ]);
        using var response = await http.PostAsync("", content, ct);
        var result = response.Headers.Location?.OriginalString
            .Split(";st=").ElementAtOrDefault(1);
        if (result != "DONE")
            throw new HttpRequestException(
                "Pagina stats HAProxy: esito " +
                (result ?? ((int)response.StatusCode).ToString()) + ".");
    }

    public async ValueTask DisposeAsync()
    {
        http.Dispose();
        if (ssh is not null) await ssh.DisposeAsync();
    }
}

public sealed class HaproxyStatsClientFactory(
    ISshConnector ssh,
    CredentialCipher cipher)
{
    public async Task<HaproxyStatsClient> CreateAsync(
        HaproxyInstance instance,
        Server server,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instance.StatsBaseUrl))
            throw new InvalidOperationException("URL stats non configurato.");
        var baseUri = new Uri(instance.StatsBaseUrl.TrimEnd('/') + "/");
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        ConfigureTls(handler, instance);
        ISshSession? session = null;
        if (string.Equals(instance.Transport, "SshForward",
                StringComparison.OrdinalIgnoreCase))
        {
            session = await ssh.ConnectAsync(server, ct);
            handler.ConnectCallback = async (_, token) =>
                await session.OpenForwardAsync(
                    baseUri.Host, baseUri.Port, token);
        }
        else if (!string.Equals(instance.Transport, "Direct",
                     StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Trasporto {instance.Transport} non supportato per stats.");
        }

        var client = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(10)
        };
        var password = Decrypt(instance.EncryptedStatsPassword,
            instance.StatsPasswordKeyVersion);
        if (!string.IsNullOrEmpty(instance.StatsUsername) || password.Length > 0)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{instance.StatsUsername ?? ""}:{password}"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", token);
        }
        return new HaproxyStatsClient(client, session);
    }

    private string Decrypt(string? ciphertext, int? keyVersion)
    {
        if (string.IsNullOrEmpty(ciphertext)) return "";
        if (keyVersion is null)
            throw new InvalidOperationException(
                "Versione chiave della password stats mancante.");
        return cipher.Decrypt(ciphertext, keyVersion.Value);
    }

    private static void ConfigureTls(
        SocketsHttpHandler handler,
        HaproxyInstance instance)
    {
        var ca = new X509Certificate2Collection();
        if (!string.IsNullOrWhiteSpace(instance.CaPem))
            ca.ImportFromPem(instance.CaPem);
        handler.SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
            {
                if (cert is null ||
                    (errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                    return false;
                if (ca.Count == 0) return instance.AllowSelfSigned;
                if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
                    return false;
                using var custom = new X509Chain();
                custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                custom.ChainPolicy.CustomTrustStore.AddRange(ca);
                if (chain is not null)
                    foreach (var element in chain.ChainElements)
                        custom.ChainPolicy.ExtraStore.Add(element.Certificate);
                custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return custom.Build(new X509Certificate2(cert));
            }
        };
    }
}

public static class HaproxyStatsCsv
{
    public static IReadOnlyList<HaproxyStatsRow> ParseStats(string text) =>
        ParseRows(text)
            .Select(row => new HaproxyStatsRow(
                row.Kind,
                row.Proxy,
                row.Server,
                Trim(row.Text("status"), 50),
                Trim(row.Text("check_status"), 50),
                Trim(row.Text("addr"), 300),
                row.Number("stot"),
                row.Number("req_tot"),
                row.Number("hrsp_1xx"),
                row.Number("hrsp_2xx"),
                row.Number("hrsp_3xx"),
                row.Number("hrsp_4xx"),
                row.Number("hrsp_5xx"),
                row.Number("hrsp_other"),
                row.Number("bin"),
                row.Number("bout"),
                row.Number("econ"),
                row.Number("eresp"),
                row.Number("dreq"),
                row.Number("dresp"),
                row.Number("cli_abrt"),
                row.Number("srv_abrt"),
                row.Number("conn_tot"),
                row.Number("dcon"),
                row.Number("dses"),
                row.Number("lbtot"),
                row.Number("wretr"),
                row.Number("wredis"),
                row.Number("scur"),
                row.Number("smax"),
                row.Number("slim"),
                row.Number("qcur"),
                row.Number("qmax"),
                row.Number("rate"),
                row.Number("rate_max"),
                row.Number("req_rate"),
                row.Number("req_rate_max"),
                row.Number("conn_rate"),
                row.Number("conn_rate_max"),
                row.Number("rtime"),
                row.Number("ctime"),
                row.Number("ttime"),
                row.Number("qtime"),
                row.Number("downtime"),
                row.Number("lastsess"),
                row.Number("weight"),
                row.Number("check_health"),
                row.Number("chkfail"),
                row.Number("chkdown")))
            .ToList();

    public static IReadOnlyList<HaproxyProtocolRow> ParseProtocol(string text) =>
        ParseRows(text)
            .Select(row => new HaproxyProtocolRow(
                row.Kind,
                row.Proxy,
                row.Server,
                row.Number("h2req"),
                row.Number("h2sess"),
                row.Number("h3req"),
                row.Number("h3sess"),
                row.Number("h2_open_connections"),
                row.Number("h2_backend_open_streams"),
                row.Number("h2_detected_conn_protocol_errors"),
                row.Number("h2_detected_strm_protocol_errors"),
                row.Number("quic_hdshk_fail"),
                row.Number("quic_dropped_pkt"),
                row.Number("quic_lost_pkt"),
                row.Number("quic_sent_pkt"),
                row.Number("quic_retry_error"),
                row.Number("quic_too_short_dgram"),
                row.Number("quic_rxbuf_full"),
                row.Number("quic_socket_full"),
                row.Number("h3_version_fallback"),
                row.Sum("h3_", "h3req", "h3sess", "h3_data", "h3_headers",
                    "h3_settings", "h3_max_push_id", "h3_push_promise",
                    "h3_goaway", "h3_cancel_push", "h3_version_fallback",
                    "h3_no_error"),
                row.Sum("quic_transp_err_", "quic_transp_err_no_error"),
                row.Number("qpack_encoder_stream_error"),
                row.Number("qpack_decoder_stream_error"),
                row.Number("ssl_failed_handshake")))
            .ToList();

    private sealed record Row(
        string Kind,
        string Proxy,
        string Server,
        IReadOnlyDictionary<string, string> Fields)
    {
        public string Text(string name) =>
            Fields.TryGetValue(name, out var value) ? value : "";

        public long Number(string name) =>
            long.TryParse(Text(name), out var value) ? value : 0;

        public long Sum(string prefix, params string[] excluded)
        {
            var skip = new HashSet<string>(excluded,
                StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var (name, text) in Fields)
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    !skip.Contains(name) &&
                    long.TryParse(text, out var value))
                    total += value;
            return total;
        }
    }

    private static List<Row> ParseRows(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 2) return [];
        var header = Split(lines[0].TrimStart('#', ' '));
        var rows = new List<Row>();
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var values = Split(line);
            var fields = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Count && i < values.Count; i++)
                fields[header[i].Trim()] = values[i].Trim();
            var proxy = fields.GetValueOrDefault("pxname", "");
            var name = fields.GetValueOrDefault("svname", "");
            if (proxy.Length == 0 || name.Length == 0) continue;
            var kind = name == "FRONTEND" ? "frontend"
                : name == "BACKEND" ? "backend"
                : "server";
            rows.Add(new Row(
                kind,
                Trim(proxy, 100),
                kind == "server" ? Trim(name, 200) : "",
                fields));
        }
        return rows;
    }

    private static List<string> Split(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else current.Append(c);
        }
        values.Add(current.ToString());
        return values;
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

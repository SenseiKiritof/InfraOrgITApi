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

/// <summary>Reads the HAProxy stats page in CSV form for protocol counters.</summary>
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
    public static IReadOnlyList<HaproxyProtocolRow> ParseProtocol(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 2) return [];
        var header = Split(lines[0].TrimStart('#', ' '));
        var index = header
            .Select((name, position) => (name: name.Trim(), position))
            .GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().position,
                StringComparer.OrdinalIgnoreCase);
        var rows = new List<HaproxyProtocolRow>();
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var values = Split(line);
            string Value(string name) =>
                index.TryGetValue(name, out var position) &&
                position < values.Count ? values[position].Trim() : "";
            long Number(string name) =>
                long.TryParse(Value(name), out var value) ? value : 0;
            var proxy = Value("pxname");
            var name = Value("svname");
            if (proxy.Length == 0 || name.Length == 0) continue;
            var kind = name == "FRONTEND" ? "frontend"
                : name == "BACKEND" ? "backend"
                : "server";
            rows.Add(new HaproxyProtocolRow(
                kind,
                Trim(proxy, 100),
                kind == "server" ? Trim(name, 200) : "",
                Number("h2req"),
                Number("h2sess"),
                Number("h3req"),
                Number("h3sess"),
                Number("h2_open_connections"),
                Number("h2_backend_open_streams"),
                Number("h2_detected_conn_protocol_errors"),
                Number("h2_detected_strm_protocol_errors"),
                Number("quic_hdshk_fail"),
                Number("quic_dropped_pkt"),
                Number("quic_lost_pkt"),
                Number("quic_sent_pkt"),
                Number("quic_retry_error"),
                Number("quic_too_short_dgram"),
                Number("quic_rxbuf_full"),
                Number("quic_socket_full"),
                Number("h3_version_fallback"),
                Sum(index, values, "h3_", "h3req", "h3sess", "h3_data",
                    "h3_headers", "h3_settings", "h3_max_push_id",
                    "h3_push_promise", "h3_goaway", "h3_cancel_push",
                    "h3_version_fallback"),
                Sum(index, values, "quic_transp_err_"),
                Number("qpack_encoder_stream_error"),
                Number("qpack_decoder_stream_error"),
                Number("ssl_failed_handshake")));
        }
        return rows;
    }

    private static long Sum(
        IReadOnlyDictionary<string, int> index,
        IReadOnlyList<string> values,
        string prefix,
        params string[] excluded)
    {
        var skip = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var (name, position) in index)
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                skip.Contains(name) || position >= values.Count)
                continue;
            if (long.TryParse(values[position], out var value)) total += value;
        }
        return total;
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

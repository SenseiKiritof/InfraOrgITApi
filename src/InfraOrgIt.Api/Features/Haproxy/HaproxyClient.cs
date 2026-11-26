using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;

namespace InfraOrgIt.Api.Features.Haproxy;

public sealed record HaproxyConnectionTest(
    string ApiVersion,
    string? HaproxyVersion,
    int StatsRows,
    DateTime At);

public sealed record HaproxyRouteResult(
    string Frontend,
    string Bind,
    string Host,
    string Path,
    string Backend,
    bool IsDefault,
    string? RawCondition);

public sealed record HaproxyServerResult(
    string Backend,
    string Server,
    string Address,
    string Status,
    string? CheckStatus,
    int? Weight);

public sealed record HaproxyRefreshResult(
    IReadOnlyList<HaproxyRouteResult> Routes,
    IReadOnlyList<HaproxyServerResult> Servers,
    IReadOnlyList<HaproxyCertificateResult> Certificates,
    int? ConfigurationVersion);

public sealed record HaproxyCertificateResult(
    string StorageName,
    string Path,
    string Domains,
    string Issuer,
    DateTime? NotBefore,
    DateTime? NotAfter,
    long Size);

public sealed record HaproxyStatsRow(
    string Kind,
    string Proxy,
    string Server,
    string Status,
    string CheckStatus,
    string Address,
    long Stot,
    long ReqTot,
    long H1xx,
    long H2xx,
    long H3xx,
    long H4xx,
    long H5xx,
    long HOther,
    long Bin,
    long Bout,
    long Econ,
    long Eresp,
    long Dreq,
    long Dresp,
    long CliAbrt,
    long SrvAbrt,
    long ConnTot,
    long Dcon,
    long Dses,
    long Lbtot,
    long Wretr,
    long Wredis,
    long Scur,
    long Smax,
    long Slim,
    long Qcur,
    long Qmax,
    long Rate,
    long RateMax,
    long ReqRate,
    long ReqRateMax,
    long ConnRate,
    long ConnRateMax,
    long Rtime,
    long Ctime,
    long Ttime,
    long Qtime,
    long Downtime,
    long Lastsess,
    long Weight,
    long CheckHealth,
    long Chkfail,
    long Chkdown);

public sealed record HaproxyProcessInfo(
    string Version,
    long Uptime,
    long CurrConns,
    long CumConns,
    long CurrSslConns,
    long CumSslConns,
    long MaxConn,
    long SessRate,
    long ConnRate,
    long Tasks,
    long RunQueue,
    long IdlePct,
    long MaxMemoryMb,
    long Threads,
    long ActivePeers,
    long ConnectedPeers,
    long FailedResolutions,
    long DroppedLogs,
    long BytesOutRate);

/// <summary>
/// Per-instance HTTP client for the Data Plane API. Direct uses normal HTTPS;
/// SshForward tunnels the HTTP connection through the server SSH session.
/// </summary>
public sealed class HaproxyClient : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient http;
    private readonly ISshSession? ssh;
    private readonly HaproxyInstance instance;

    internal HaproxyClient(
        HttpClient http,
        HaproxyInstance instance,
        ISshSession? ssh = null)
    {
        this.http = http;
        this.instance = instance;
        this.ssh = ssh;
    }

    public async Task<HaproxyConnectionTest> TestAsync(CancellationToken ct)
    {
        var version = await DetectVersionAsync(ct);
        var haproxyVersion = await RuntimeVersionAsync(version, ct);
        var stats = await GetArrayAsync(version,
            "services/haproxy/stats/native?type=server", ct);
        var at = DateTime.UtcNow;
        instance.EffectiveApiVersion = version;
        instance.LastTestAt = at;
        instance.LastTestStatus = "Ok";
        instance.LastTestDetail = null;
        return new HaproxyConnectionTest(version, haproxyVersion, stats.Count, at);
    }

    public async Task<HaproxyRefreshResult> RefreshAsync(CancellationToken ct)
    {
        var version = await DetectVersionAsync(ct);
        instance.EffectiveApiVersion = version;
        var configVersion = await ConfigurationVersionAsync(version, ct);
        var routes = await RoutesAsync(version, configVersion, ct);
        var servers = await ServersAsync(version, ct);
        var certificates = await CertificatesAsync(version, ct);
        return new HaproxyRefreshResult(
            routes, servers, certificates, configVersion);
    }

    public async Task<int?> GetVersionAsync(CancellationToken ct)
    {
        var version = await DetectVersionAsync(ct);
        return await ConfigurationVersionAsync(version, ct);
    }

    public async Task UploadCertificateAsync(
        string name,
        string pem,
        bool replace,
        CancellationToken ct)
    {
        var version = await DetectVersionAsync(ct);
        var configVersion = await ConfigurationVersionAsync(version, ct)
                            ?? throw new InvalidOperationException(
                                "Configuration-Version non disponibile.");
        var encoded = Uri.EscapeDataString(name);
        HttpResponseMessage response;
        if (replace)
        {
            using var content = new StringContent(pem, Encoding.UTF8, "text/plain");
            response = await http.PutAsync(
                $"{version}/services/haproxy/storage/ssl_certificates/{encoded}?version={configVersion}",
                content, ct);
        }
        else
        {
            using var form = new MultipartFormDataContent();
            using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(pem));
            content.Headers.ContentType =
                new MediaTypeHeaderValue("application/x-pem-file");
            form.Add(content, "file_upload", name);
            response = await http.PostAsync(
                $"{version}/services/haproxy/storage/ssl_certificates?version={configVersion}",
                form, ct);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Data Plane API HTTP {(int)response.StatusCode} sul certificato {name}: " +
                    await response.Content.ReadAsStringAsync(ct));
        }
    }

    public async Task DeleteCertificateAsync(string name, CancellationToken ct)
    {
        var version = await DetectVersionAsync(ct);
        var configVersion = await ConfigurationVersionAsync(version, ct)
                            ?? throw new InvalidOperationException(
                                "Configuration-Version non disponibile.");
        using var response = await http.DeleteAsync(
            $"{version}/services/haproxy/storage/ssl_certificates/{Uri.EscapeDataString(name)}?version={configVersion}",
            ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Data Plane API HTTP {(int)response.StatusCode} sul certificato {name}.");
    }

    public async Task SetServerStateAsync(
        string backend,
        string server,
        string state,
        CancellationToken ct)
    {
        var version = await DetectVersionAsync(ct);
        var path = version == "v2"
            ? $"services/haproxy/runtime/servers/{Uri.EscapeDataString(server)}?backend={Uri.EscapeDataString(backend)}"
            : $"services/haproxy/runtime/backends/{Uri.EscapeDataString(backend)}/servers/{Uri.EscapeDataString(server)}";
        var body = JsonSerializer.SerializeToUtf8Bytes(new { admin_state = state });
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await http.PutAsync(
            $"{version}/{path}", content, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Data Plane API HTTP {(int)response.StatusCode} su {backend}/{server}: " +
                await response.Content.ReadAsStringAsync(ct));
    }

    private async Task<List<HaproxyCertificateResult>> CertificatesAsync(
        string version,
        CancellationToken ct)
    {
        var items = await GetArrayAsync(version,
            "services/haproxy/storage/ssl_certificates", ct);
        var result = new List<HaproxyCertificateResult>();
        foreach (var item in items)
        {
            var name = Text(item, "storage_name");
            if (name.Length == 0) continue;
            var path = Text(item, "file");
            var domains = "";
            var issuer = "";
            DateTime? notBefore = null;
            DateTime? notAfter = null;
            long size = 0;
            try
            {
                var meta = await GetJsonAsync(version,
                    $"services/haproxy/storage/ssl_certificates/{Uri.EscapeDataString(name)}",
                    ct);
                if (meta is not null)
                {
                    domains = Text(meta.Value, "domains");
                    issuer = Text(meta.Value, "issuers");
                    notBefore = Date(meta.Value, "not_before");
                    notAfter = Date(meta.Value, "not_after");
                    if (meta.Value.TryGetProperty("size", out var sizeValue) &&
                        sizeValue.TryGetInt64(out var parsedSize))
                        size = parsedSize;
                    path = Text(meta.Value, "file");
                }
            }
            catch (Exception exception) when (exception is HttpRequestException
                                                  or JsonException)
            {
                // metadata is optional; keep the list entry
            }
            result.Add(new HaproxyCertificateResult(
                Trim(name, 200),
                Trim(path, 500),
                Trim(domains, 1000),
                Trim(issuer, 500),
                notBefore,
                notAfter,
                size));
        }
        return result;
    }

    private static DateTime? Date(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(value.GetString(), out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    public async Task<IReadOnlyList<HaproxyStatsRow>> StatsAsync(
        CancellationToken ct)
    {
        var version = await DetectVersionAsync(ct);
        instance.EffectiveApiVersion = version;
        var stats = await GetArrayAsync(version,
            "services/haproxy/stats/native", ct);
        var rows = new List<HaproxyStatsRow>();
        foreach (var row in stats)
        {
            var kind = Text(row, "type").ToLowerInvariant();
            if (kind is not ("frontend" or "backend" or "server")) continue;
            if (!row.TryGetProperty("stats", out var values)) continue;
            var proxy = kind == "server"
                ? Text(row, "backend_name")
                : Text(row, "name");
            var server = kind == "server" ? Text(row, "name") : "";
            if (proxy.Length == 0 && server.Length == 0) continue;
            long L(string name) =>
                values.TryGetProperty(name, out var value) &&
                value.TryGetInt64(out var number)
                    ? number
                    : 0;
            rows.Add(new HaproxyStatsRow(
                kind,
                Trim(proxy, 100),
                Trim(server, 200),
                Trim(Text(values, "status"), 50),
                Trim(Text(values, "check_status"), 50),
                Trim(Text(values, "addr"), 300),
                L("stot"),
                L("req_tot"),
                L("hrsp_1xx"),
                L("hrsp_2xx"),
                L("hrsp_3xx"),
                L("hrsp_4xx"),
                L("hrsp_5xx"),
                L("hrsp_other"),
                L("bin"),
                L("bout"),
                L("econ"),
                L("eresp"),
                L("dreq"),
                L("dresp"),
                L("cli_abrt"),
                L("srv_abrt"),
                L("conn_tot"),
                L("dcon"),
                L("dses"),
                L("lbtot"),
                L("wretr"),
                L("wredis"),
                L("scur"),
                L("smax"),
                L("slim"),
                L("qcur"),
                L("qmax"),
                L("rate"),
                L("rate_max"),
                L("req_rate"),
                L("req_rate_max"),
                L("conn_rate"),
                L("conn_rate_max"),
                L("rtime"),
                L("ctime"),
                L("ttime"),
                L("qtime"),
                L("downtime"),
                L("lastsess"),
                L("weight"),
                L("check_health"),
                L("chkfail"),
                L("chkdown")));
        }
        return rows;
    }

    public async Task<HaproxyProcessInfo> ProcessAsync(CancellationToken ct)
    {
        var version = await DetectVersionAsync(ct);
        var root = await GetJsonAsync(version,
            "services/haproxy/runtime/info", ct);
        if (root is null) throw new InvalidOperationException(
            "runtime/info non disponibile.");
        var info = root.Value;
        if (info.ValueKind == JsonValueKind.Array && info.GetArrayLength() > 0)
            info = info[0];
        if (!info.TryGetProperty("info", out var details))
            throw new InvalidOperationException("runtime/info senza info.");
        long L(string name) =>
            details.TryGetProperty(name, out var value) &&
            value.TryGetInt64(out var number)
                ? number
                : 0;
        return new HaproxyProcessInfo(
            Text(details, "version"),
            L("uptime"),
            L("curr_conns"),
            L("cum_conns"),
            L("curr_ssl_conns"),
            L("cum_ssl_conns"),
            L("max_conn"),
            L("sess_rate"),
            L("conn_rate"),
            L("tasks"),
            L("run_queue"),
            L("idle_pct"),
            L("mem_max_mb"),
            L("nbthread"),
            L("active_peers"),
            L("connected_peers"),
            L("failed_resolutions"),
            L("dropped_logs"),
            L("bytes_out_rate"));
    }

    private async Task<string> DetectVersionAsync(CancellationToken ct)
    {
        var requested = instance.ApiVersion.Trim().ToLowerInvariant();
        if (requested is "v2" or "v3")
        {
            var detail = await ProbeAsync(requested, ct);
            if (detail is null) return requested;
            throw new InvalidOperationException(
                $"La Data Plane API non risponde su {requested}: {detail}.");
        }

        if (instance.EffectiveApiVersion is "v2" or "v3")
        {
            var cached = await ProbeAsync(instance.EffectiveApiVersion, ct);
            if (cached is null) return instance.EffectiveApiVersion;
        }

        var v3 = await ProbeAsync("v3", ct);
        if (v3 is null) return "v3";
        var v2 = await ProbeAsync("v2", ct);
        if (v2 is null) return "v2";
        throw new InvalidOperationException(
            $"Data Plane API non raggiungibile: v3 {v3}; v2 {v2}.");
    }

    private async Task<string?> ProbeAsync(string version, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"{version}/info", ct);
            return response.IsSuccessStatusCode
                ? null
                : $"HTTP {(int)response.StatusCode}";
        }
        catch (HttpRequestException exception)
        {
            return exception.Message;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return "timeout";
        }
    }

    private async Task<string?> RuntimeVersionAsync(
        string version,
        CancellationToken ct)
    {
        try
        {
            var root = await GetJsonAsync(version,
                "services/haproxy/runtime/info", ct);
            if (root is null) return null;
            var info = root.Value;
            if (info.ValueKind == JsonValueKind.Array && info.GetArrayLength() > 0)
                info = info[0];
            if (!info.TryGetProperty("info", out var detail)) return null;
            return Text(detail, "version");
        }
        catch (Exception exception) when (exception is HttpRequestException
                                              or TaskCanceledException
                                              or JsonException)
        {
            return null;
        }
    }

    private async Task<int?> ConfigurationVersionAsync(
        string version,
        CancellationToken ct)
    {
        try
        {
            var root = await GetJsonAsync(version,
                "services/haproxy/configuration/version", ct);
            if (root is null) return null;
            if (root.Value.ValueKind == JsonValueKind.Number &&
                root.Value.TryGetInt32(out var value))
                return value;
            if (root.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "version", "_version" })
                    if (root.Value.TryGetProperty(name, out var property) &&
                        property.TryGetInt32(out value))
                        return value;
            }
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                              or TaskCanceledException
                                              or JsonException)
        {
            return null;
        }
    }

    private async Task<List<HaproxyRouteResult>> RoutesAsync(
        string version,
        int? configVersion,
        CancellationToken ct)
    {
        var result = new List<HaproxyRouteResult>();
        var frontends = await GetArrayAsync(version,
            "services/haproxy/configuration/frontends", ct);
        foreach (var frontend in frontends)
        {
            var name = Text(frontend, "name");
            if (name.Length == 0) continue;
            var defaultBackend = Text(frontend, "default_backend");
            var encoded = Uri.EscapeDataString(name);
            var binds = await GetArrayAsync(version,
                $"services/haproxy/configuration/frontends/{encoded}/binds", ct);
            var bind = string.Join(",", binds
                .Select(x => Text(x, "port"))
                .Where(x => x.Length > 0)
                .Distinct());
            var acls = await GetArrayAsync(version,
                $"services/haproxy/configuration/frontends/{encoded}/acls", ct);
            var hosts = new Dictionary<string, List<string>>(
                StringComparer.Ordinal);
            foreach (var acl in acls)
            {
                if (!string.Equals(Text(acl, "criterion"), "var(txn.host)",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                var value = Text(acl, "value");
                var match = Regex.Match(value, @"^-m\s+str\s+(.+)$",
                    RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                var aclName = Text(acl, "acl_name");
                if (aclName.Length == 0) continue;
                hosts[aclName] = match.Groups[1].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var rules = await GetArrayAsync(version,
                $"services/haproxy/configuration/frontends/{encoded}/backend_switching_rules",
                ct);
            foreach (var rule in rules)
            {
                var backend = Text(rule, "name");
                var condition = Text(rule, "cond");
                var test = Text(rule, "cond_test");
                var match = Regex.Match(test,
                    @"^(?<acl>[A-Za-z0-9_.-]+)(?:\s*\{\s*path_beg\s+(?<path>\S+)\s*\})?$");
                if (string.Equals(condition, "if", StringComparison.OrdinalIgnoreCase)
                    && match.Success
                    && hosts.TryGetValue(match.Groups["acl"].Value, out var values))
                {
                    var path = match.Groups["path"].Success
                        ? match.Groups["path"].Value
                        : "/";
                    foreach (var host in values)
                        AddRoute(result, name, bind, host, path, backend,
                            false, null, configVersion);
                }
                else
                {
                    AddRoute(result, name, bind, "", "", backend, false,
                        test, configVersion);
                }
            }
            if (defaultBackend.Length > 0)
                AddRoute(result, name, bind, "", "", defaultBackend, true,
                    "default_backend", configVersion);
        }

        return result
            .GroupBy(x => (x.Frontend, x.Host, x.Path, x.Backend))
            .Select(x => x.First())
            .ToList();
    }

    private static void AddRoute(
        List<HaproxyRouteResult> routes,
        string frontend,
        string bind,
        string host,
        string path,
        string backend,
        bool isDefault,
        string? raw,
        int? configVersion)
    {
        routes.Add(new HaproxyRouteResult(
            Trim(frontend, 100),
            Trim(bind, 200),
            Trim(host, 200),
            Trim(path.Length == 0 ? "/" : path, 255),
            Trim(backend, 100),
            isDefault,
            raw is null ? null : Trim(raw, 500)));
    }

    private async Task<List<HaproxyServerResult>> ServersAsync(
        string version,
        CancellationToken ct)
    {
        var stats = await GetArrayAsync(version,
            "services/haproxy/stats/native", ct);
        var result = new List<HaproxyServerResult>();
        foreach (var row in stats)
        {
            if (!string.Equals(Text(row, "type"), "server",
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (!row.TryGetProperty("stats", out var values)) continue;
            var backend = Text(row, "backend_name");
            var name = Text(row, "name");
            if (backend.Length == 0 || name.Length == 0) continue;
            result.Add(new HaproxyServerResult(
                Trim(backend, 100),
                Trim(name, 200),
                Trim(Text(values, "addr"), 300),
                Trim(Text(values, "status"), 50),
                Trim(Text(values, "check_status"), 50),
                values.TryGetProperty("weight", out var weight) &&
                weight.TryGetInt32(out var value) ? value : null));
        }
        return result
            .GroupBy(x => (x.Backend, x.Server))
            .Select(x => x.First())
            .ToList();
    }

    private async Task<List<JsonElement>> GetArrayAsync(
        string version,
        string path,
        CancellationToken ct)
    {
        var root = await GetJsonAsync(version, path, ct);
        if (root is null) return [];
        var value = root.Value;
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().ToList();
        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("stats", out var stats) &&
            stats.ValueKind == JsonValueKind.Array)
            return stats.EnumerateArray().ToList();
        return [];
    }

    private async Task<JsonElement?> GetJsonAsync(
        string version,
        string path,
        CancellationToken ct)
    {
        var target = version == "v2" ? V2Path(path) : path;
        using var response = await http.GetAsync(
            $"{version}/{target}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Data Plane API HTTP {(int)response.StatusCode} su {path}");
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement.Clone();
        return version == "v2" ? UnwrapV2(root) : root;
    }

    private static JsonElement? UnwrapV2(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("data", out var data))
            return data.Clone();
        if (root.ValueKind == JsonValueKind.Array &&
            root.GetArrayLength() == 1)
        {
            var single = root[0];
            if (single.ValueKind == JsonValueKind.Object &&
                (single.TryGetProperty("info", out _) ||
                 single.TryGetProperty("stats", out _) ||
                 single.TryGetProperty("runtimeAPI", out _)))
                return single.Clone();
        }
        return root;
    }

    private static string V2Path(string path)
    {
        var match = Regex.Match(path,
            @"^services/haproxy/configuration/frontends/([^/]+)/(acls|backend_switching_rules|binds)$");
        if (!match.Success) return path;
        var name = Uri.UnescapeDataString(match.Groups[1].Value);
        var section = match.Groups[2].Value;
        var parent = section is "acls" or "backend_switching_rules"
            ? $"parent_name={Uri.EscapeDataString(name)}&parent_type=frontend"
            : $"frontend={Uri.EscapeDataString(name)}";
        return $"services/haproxy/configuration/{section}?{parent}";
    }

    public async ValueTask DisposeAsync()
    {
        http.Dispose();
        if (ssh is not null) await ssh.DisposeAsync();
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : value.ToString()
            : "";

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

public sealed class HaproxyClientFactory(
    ISshConnector ssh,
    CredentialCipher cipher)
{
    public async Task<HaproxyClient> CreateAsync(
        HaproxyInstance instance,
        Server server,
        CancellationToken ct)
    {
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
                    instance.ApiHost, instance.ApiPort, token);
        }
        else if (!string.Equals(instance.Transport, "Direct",
                     StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Trasporto {instance.Transport} non ancora supportato.");
        }

        var password = Decrypt(instance.EncryptedPassword,
            instance.PasswordKeyVersion);
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(instance.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 16 * 1024 * 1024
        };
        if (!string.IsNullOrEmpty(instance.Username) || password.Length > 0)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{instance.Username ?? ""}:{password}"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", token);
        }
        return new HaproxyClient(client, instance, session);
    }

    private string Decrypt(string? ciphertext, int? keyVersion)
    {
        if (string.IsNullOrEmpty(ciphertext)) return "";
        if (keyVersion is null)
            throw new InvalidOperationException(
                "Versione chiave della password Data Plane API mancante.");
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

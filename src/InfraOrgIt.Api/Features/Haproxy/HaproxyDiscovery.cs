using System.Globalization;

namespace InfraOrgIt.Api.Features.Haproxy;

public sealed record HaproxyDiscoveryInfo(
    bool HasHaproxy,
    bool Active,
    bool EnabledAtBoot,
    string? Binary,
    string? Version,
    string? ConfigPath,
    int DataPlaneProcesses,
    IReadOnlyList<int> ListeningPorts)
{
    /// <summary>Loopback port exposed by dataplaneapi, if it can be inferred.</summary>
    public int? ApiPort
    {
        get
        {
            foreach (var port in new[] { 5555, 5556, 8404 })
                if (ListeningPorts.Contains(port))
                    return port;
            return DataPlaneProcesses > 0 ? 5555 : null;
        }
    }
}

/// <summary>
/// Read-only SSH discovery for HAProxy and its Data Plane API. It never reads
/// passwords or writes to the server.
/// </summary>
public static class HaproxyDiscovery
{
    public const string Script = """
        echo "__INFRAORGIT_HAPROXY__"
        bin="$(command -v haproxy 2>/dev/null || true)"
        echo "bin=$bin"
        if [ -n "$bin" ]; then
          "$bin" -v 2>&1 | head -1 | sed 's/^/version=/' || true
        fi
        echo "active=$(systemctl is-active haproxy 2>/dev/null || true)"
        echo "enabled=$(systemctl is-enabled haproxy 2>/dev/null || true)"
        if [ -r /etc/haproxy/haproxy.cfg ]; then
          echo "config=/etc/haproxy/haproxy.cfg"
        else
          echo "config="
        fi
        echo "dataplane=$(ps -eo args= 2>/dev/null | grep -c '[d]ataplaneapi' || true)"
        echo "__PORTS__"
        ss -ltnH 2>/dev/null | awk '{print $4}' | sed 's/.*://' | sort -un
        """;

    public static HaproxyDiscoveryInfo Parse(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n');
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ports = new List<int>();
        var inPorts = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line == "__PORTS__")
            {
                inPorts = true;
                continue;
            }
            if (inPorts)
            {
                if (int.TryParse(line.Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var port)
                    && port is > 0 and <= 65535
                    && !ports.Contains(port))
                    ports.Add(port);
                continue;
            }
            var split = line.IndexOf('=');
            if (split <= 0) continue;
            values[line[..split]] = line[(split + 1)..].Trim();
        }

        string Value(string key) => values.TryGetValue(key, out var value) ? value : "";
        var bin = Value("bin");
        var config = Value("config");
        var dataplane = int.TryParse(Value("dataplane"), out var processes)
            ? processes
            : 0;
        return new HaproxyDiscoveryInfo(
            HasHaproxy: bin.Length > 0 || config.Length > 0,
            Active: string.Equals(Value("active"), "active",
                StringComparison.OrdinalIgnoreCase),
            EnabledAtBoot: string.Equals(Value("enabled"), "enabled",
                StringComparison.OrdinalIgnoreCase),
            Binary: bin.Length > 0 ? bin : null,
            Version: Trim(Value("version"), 200),
            ConfigPath: Trim(config, 300),
            DataPlaneProcesses: dataplane,
            ListeningPorts: ports);
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Trim();
        return clean.Length > max ? clean[..max] : clean;
    }
}

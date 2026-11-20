namespace InfraOrgIt.Api.Features.Inventory;

/// <summary>Subset of <c>/etc/os-release</c> used by the OS update checker.</summary>
public sealed record OsReleaseInfo(
    string Id,
    string Name,
    string VersionId,
    string Codename,
    string PrettyName)
{
    public string Major => VersionId.Split('.')[0];

    public static OsReleaseInfo? Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var index = line.IndexOf('=');
            if (index <= 0) continue;
            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim().Trim('"').Trim('\'');
            if (key.Length > 0) values[key] = value;
        }
        if (!values.TryGetValue("ID", out var id) || string.IsNullOrWhiteSpace(id))
            return null;
        values.TryGetValue("NAME", out var name);
        values.TryGetValue("VERSION_ID", out var version);
        values.TryGetValue("VERSION_CODENAME", out var codename);
        values.TryGetValue("PRETTY_NAME", out var pretty);
        return new OsReleaseInfo(
            id.ToLowerInvariant(),
            name ?? id,
            version ?? "",
            codename ?? "",
            pretty ?? name ?? id);
    }
}

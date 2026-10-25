using System.Text.RegularExpressions;
using InfraOrgIt.Api.BuildingBlocks.Validation;

namespace InfraOrgIt.Api.Features.Remote;

public static partial class CommandRenderer
{
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"[$`\\""\r\n]")]
    private static partial Regex ShellActive();

    public static void CheckValues(
        string script,
        IReadOnlyDictionary<string, string> values)
    {
        var used = Placeholder().Matches(script)
            .Select(match => match.Groups[1].Value)
            .ToHashSet();

        var unsafeNames = values
            .Where(pair => used.Contains(pair.Key) && ShellActive().IsMatch(pair.Value))
            .Select(pair => pair.Key)
            .ToList();

        if (unsafeNames.Count > 0)
            throw Guard.Unprocessable(
                $"Caratteri non ammessi nei valori di: {string.Join(", ", unsafeNames)}.");
    }

    public static string Render(
        string script,
        IReadOnlyDictionary<string, string> values)
    {
        var missing = Placeholder().Matches(script)
            .Select(match => match.Groups[1].Value)
            .Where(name => !values.ContainsKey(name))
            .Distinct()
            .ToList();

        if (missing.Count > 0)
            throw Guard.Unprocessable($"Segnaposto senza valore: {string.Join(", ", missing)}.");

        return Placeholder().Replace(
            script,
            match => "'" + values[match.Groups[1].Value].Replace("'", "'\\''") + "'");
    }

    public static IReadOnlyList<string> Placeholders(string script) =>
        Placeholder().Matches(script)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();
}

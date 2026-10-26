using InfraOrgIt.LegacyImport;

var options = Parse(args);
if (options is null)
{
    Console.Error.WriteLine(
        "Uso: InfraOrgIt.LegacyImport --input <fixture.json> [--report <report.json>] " +
        "[--apply --connection <mysql>] [--old-key <base64>] [--new-key <base64>] [--no-redact-audit]");
    return 2;
}

var importer = new Importer();
var report = await importer.RunAsync(options, CancellationToken.None);
var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
{
    WriteIndented = true
});
await File.WriteAllTextAsync(options.ReportPath, json);

Console.WriteLine($"{(report.DryRun ? "DRY-RUN" : "APPLY")} · {report.Tables.Sum(x => x.Read)} righe lette");
foreach (var table in report.Tables)
    Console.WriteLine($"  {table.Table}: lette {table.Read}, inserite {table.Inserted}, saltate {table.Skipped}, fallite {table.Failed}");
Console.WriteLine($"  credenziali NeedsReauth: {report.NeedsReauth}; audit redatti: {report.RedactedAudit}");
foreach (var warning in report.Warnings)
    Console.WriteLine($"  warning: {warning}");
foreach (var error in report.Errors)
    Console.Error.WriteLine($"  errore: {error}");

return report.Errors.Count == 0 ? 0 : 1;

static ImportOptions? Parse(string[] args)
{
    string? input = null;
    var report = "import-report.json";
    string? connection = null;
    var apply = false;
    var oldKey = Environment.GetEnvironmentVariable("LEGACY_IMPORT_OLD_KEY");
    var newKey = Environment.GetEnvironmentVariable("LEGACY_IMPORT_NEW_KEY");
    var redact = true;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--input":
                input = Next(args, ref i);
                break;
            case "--report":
                report = Next(args, ref i) ?? report;
                break;
            case "--connection":
                connection = Next(args, ref i);
                break;
            case "--apply":
                apply = true;
                break;
            case "--old-key":
                oldKey = Next(args, ref i);
                break;
            case "--new-key":
                newKey = Next(args, ref i);
                break;
            case "--no-redact-audit":
                redact = false;
                break;
        }
    }

    return string.IsNullOrWhiteSpace(input)
        ? null
        : new ImportOptions(input, report, connection, apply, oldKey, newKey, redact);
}

static string? Next(string[] args, ref int index) =>
    index + 1 < args.Length ? args[++index] : null;

using InfraOrgIt.LegacyImport;

var options = Parse(args);
if (options is null)
{
    Console.Error.WriteLine(
        "Uso: InfraOrgIt.LegacyImport [--input <fixture.json>] [--report <report.json>] " +
        "[--apply] [--allow-reauth] [--new-key-version <n>] [--no-redact-audit]\n" +
        "Senza --input legge il MySQL legacy da LEGACY_SOURCE_CONNECTION.\n" +
        "Env: LEGACY_SOURCE_CONNECTION, LEGACY_IMPORT_CONNECTION, LEGACY_IMPORT_OLD_KEY, " +
        "LEGACY_IMPORT_NEW_KEY, LEGACY_IMPORT_NEW_KEY_VERSION.");
    return 2;
}

ImportReport report;
try
{
    report = await new Importer().RunAsync(options, CancellationToken.None);
}
catch (Exception exception)
{
    report = new ImportReport(
        options.InputPath,
        !options.Apply,
        DateTime.UtcNow,
        [],
        [$"Errore fatale: {exception.Message}"],
        [],
        0,
        0,
        false,
        options.NewKeyVersion);
}

var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
{
    WriteIndented = true
});

try
{
    var path = Path.GetFullPath(options.ReportPath);
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);
    await File.WriteAllTextAsync(path, json);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Report non scritto: {exception.Message}");
    return 3;
}

Console.WriteLine($"{(report.DryRun ? "DRY-RUN" : "APPLY")} · {report.Tables.Sum(x => x.Read)} righe lette");
foreach (var table in report.Tables)
    Console.WriteLine($"  {table.Table}: lette {table.Read}, inserite {table.Inserted}, saltate {table.Skipped}, fallite {table.Failed}");
Console.WriteLine($"  credenziali NeedsReauth: {report.NeedsReauth}; audit redatti: {report.RedactedAudit}");
Console.WriteLine($"  nuova chiave credenziali: versione {report.NewKeyVersion}");
if (report.RolledBack)
    Console.WriteLine("  apply annullato: nessuna riga scritta");
foreach (var warning in report.Warnings)
    Console.WriteLine($"  warning: {warning}");
foreach (var error in report.Errors)
    Console.Error.WriteLine($"  errore: {error}");

return report.Errors.Count == 0 ? 0 : 1;

static ImportOptions? Parse(string[] args)
{
    string? input = null;
    var source = Environment.GetEnvironmentVariable("LEGACY_SOURCE_CONNECTION");
    var report = "import-report.json";
    var apply = false;
    var allowReauth = false;
    var newKeyVersion = ReadInt("LEGACY_IMPORT_NEW_KEY_VERSION", 1);
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
            case "--apply":
                apply = true;
                break;
            case "--allow-reauth":
                allowReauth = true;
                break;
            case "--new-key-version":
                var text = Next(args, ref i);
                if (!int.TryParse(text, out newKeyVersion))
                    return null;
                break;
            case "--no-redact-audit":
                redact = false;
                break;
            default:
                Console.Error.WriteLine($"Argomento non riconosciuto: {args[i]}");
                return null;
        }
    }

    return string.IsNullOrWhiteSpace(input) && string.IsNullOrWhiteSpace(source)
        ? null
        : new ImportOptions(
            input ?? "mysql:legacy",
            report,
            Environment.GetEnvironmentVariable("LEGACY_IMPORT_CONNECTION"),
            apply,
            Environment.GetEnvironmentVariable("LEGACY_IMPORT_OLD_KEY"),
            Environment.GetEnvironmentVariable("LEGACY_IMPORT_NEW_KEY"),
            redact,
            newKeyVersion,
            allowReauth,
            source);
}

static string? Next(string[] args, ref int index) =>
    index + 1 < args.Length ? args[++index] : null;

static int ReadInt(string name, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

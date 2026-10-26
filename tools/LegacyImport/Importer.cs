using System.Globalization;
using System.Text.Json;
using MySql.Data.MySqlClient;

namespace InfraOrgIt.LegacyImport;

public sealed record ImportOptions(
    string InputPath,
    string ReportPath,
    string? ConnectionString,
    bool Apply,
    string? OldKey,
    string? NewKey,
    bool RedactAudit,
    int NewKeyVersion = 1,
    bool AllowReauth = false);

public sealed record TableReport(string Table, int Read, int Inserted, int Skipped, int Failed);

public sealed record ImportReport(
    string Input,
    bool DryRun,
    DateTime GeneratedAt,
    List<TableReport> Tables,
    List<string> Errors,
    List<string> Warnings,
    int NeedsReauth,
    int RedactedAudit,
    bool RolledBack,
    int NewKeyVersion);

public sealed class Importer
{
    private static readonly (string Table, string Column, string ParentTable)[] ForeignKeys =
    [
        ("ProviderEnvironments", "ProviderId", "Providers"),
        ("ProviderEnvironments", "EnvironmentDefinitionId", "Environments"),
        ("Menus", "ParentId", "Menus"),
        ("MenuRole", "MenuId", "Menus"),
        ("MenuRole", "RolesId", "Roles"),
        ("GroupUser", "GroupsId", "Groups"),
        ("GroupUser", "UsersId", "Users"),
        ("RoleUser", "RolesId", "Roles"),
        ("RoleUser", "UsersId", "Users"),
        ("ProviderEnvironmentGroupRoles", "ProviderEnvironmentId", "ProviderEnvironments"),
        ("ProviderEnvironmentGroupRoles", "GroupId", "Groups"),
        ("ProviderEnvironmentGroupRoles", "RoleId", "Roles"),
        ("ApplicationVersions", "ApplicationId", "Applications"),
        ("ApplicationVersionApis", "ApplicationVersionId", "ApplicationVersions"),
        ("ApplicationApiQueryParams", "ApplicationApiId", "ApplicationVersionApis"),
        ("ApplicationProject", "ApplicationsId", "Applications"),
        ("ApplicationProject", "ProjectsId", "Projects"),
        ("ApplicationActions", "ApplicationId", "Applications"),
        ("ApplicationActions", "CommandTemplateId", "CommandTemplates"),
        ("Servers", "ProviderEnvironmentId", "ProviderEnvironments"),
        ("Servers", "CredentialId", "ServerCredentials"),
        ("ProxyConnections", "SourceServerId", "Servers"),
        ("ProxyConnections", "TargetServerId", "Servers"),
        ("ServerApplications", "ServerId", "Servers"),
        ("ServerApplications", "ApplicationVersionId", "ApplicationVersions"),
        ("ServerApplications", "ProxyRouteId", "ProxyRoutes"),
        ("ProxyRoutes", "ProxyConnectionId", "ProxyConnections"),
        ("ProxyRoutes", "TargetServerApplicationId", "ServerApplications"),
        ("ProxyRoutes", "TargetProxyConnectionId", "ProxyConnections"),
        ("ReachabilityTests", "ServerId", "Servers"),
        ("DiagramScopeItems", "DiagramId", "Diagrams"),
        ("DiagramNodes", "DiagramId", "Diagrams"),
        ("DiagramEdges", "DiagramId", "Diagrams"),
        ("DiagramEdges", "ProxyConnectionId", "ProxyConnections")
    ];

    private static readonly (string Table, string[] Columns)[] Unique =
    [
        ("Providers", ["Name"]),
        ("EnvironmentDefinitions", ["Name"]),
        ("ProviderEnvironments", ["ProviderId", "EnvironmentDefinitionId"]),
        ("Applications", ["Name"]),
        ("Projects", ["Name"]),
        ("CommandTemplates", ["Name"]),
        ("Roles", ["Name"]),
        ("Groups", ["Name"]),
        ("Users", ["Username"]),
        ("ApplicationVersions", ["ApplicationId", "VersionNumber"]),
        ("ProxyRoutes", ["ProxyConnectionId", "Route"]),
        ("ProviderEnvironmentGroupRoles", ["ProviderEnvironmentId", "GroupId", "RoleId"]),
        ("DiagramScopeItems", ["DiagramId", "ScopeType", "EntityId"]),
        ("DiagramNodes", ["DiagramId", "NodeType", "EntityId"]),
        ("DiagramEdges", ["DiagramId", "ProxyConnectionId", "TargetNodeType", "TargetEntityId"]),
        ("ServerApplications", ["ProxyRouteId"])
    ];

    private sealed record ExistingRow(bool Found, string? Mismatch);

    private sealed record BuiltRow(Dictionary<string, object?> Values, bool NeedsReauth, bool Redacted);

    public async Task<ImportReport> RunAsync(ImportOptions options, CancellationToken ct)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var reports = new List<TableReport>();
        var needsReauth = 0;
        var redacted = 0;
        var rolledBack = false;

        Dictionary<string, List<JsonElement>> input;
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(options.InputPath, ct));
            input = ReadInput(document.RootElement);
        }
        catch (Exception exception)
        {
            errors.Add($"Input non leggibile: {exception.Message}");
            return NewReport(options, reports, errors, warnings, needsReauth, redacted, rolledBack);
        }

        ValidateCipherKeys(options, errors);
        Validate(input, errors);
        AddMissingTablesWarning(input, warnings);

        foreach (var plan in ImportPlans.All)
            reports.Add(new TableReport(plan.Legacy, Rows(input, plan.Legacy).Count, 0, 0, 0));

        if (!options.Apply)
        {
            needsReauth = CountNeedsReauth(input, options, warnings);
            if (options.RedactAudit)
                redacted = Rows(input, "CommandExecutions").Count;

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
                warnings.Add(
                    "Dry-run senza LEGACY_IMPORT_CONNECTION: collisioni con il target non verificate.");
            else
                await CheckCollisionsAsync(options.ConnectionString, input, errors, ct);

            return NewReport(options, reports, errors, warnings, needsReauth, redacted, rolledBack);
        }

        var requiredReauth = CountNeedsReauth(input, options, warnings);
        if (requiredReauth > 0 && !options.AllowReauth)
            errors.Add(
                $"{requiredReauth} credenziali richiedono NeedsReauth: fornisci le chiavi o usa --allow-reauth.");
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            errors.Add("LEGACY_IMPORT_CONNECTION è obbligatoria con --apply.");
        if (errors.Count > 0)
        {
            needsReauth = requiredReauth;
            return NewReport(options, reports, errors, warnings, needsReauth, redacted, rolledBack);
        }

        var counters = new Dictionary<string, (int Inserted, int Skipped, int Failed)>(StringComparer.Ordinal);
        try
        {
            await using var connection = new MySqlConnection(options.ConnectionString);
            await connection.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);

            var oldCipher = new LegacyCipher(options.OldKey);
            var newCipher = new LegacyCipher(options.NewKey);

            try
            {
                foreach (var plan in ImportPlans.All)
                {
                    var rows = Rows(input, plan.Legacy);
                    if (rows.Count == 0)
                        continue;

                    var inserted = 0;
                    var skipped = 0;
                    var failed = 0;

                    foreach (var row in rows)
                    {
                        var key = Key(row, plan);
                        var existing = await InspectAsync(connection, transaction, plan, row, key, ct);
                        if (existing.Found)
                        {
                            if (existing.Mismatch is null)
                            {
                                skipped++;
                            }
                            else
                            {
                                failed++;
                                errors.Add(
                                    $"{plan.Legacy} {key}: Id già usato da una riga diversa " +
                                    $"({existing.Mismatch}); riga non importata.");
                            }

                            continue;
                        }

                        try
                        {
                            var built = BuildValues(plan, row, options, oldCipher, newCipher);
                            await InsertAsync(connection, transaction, plan, built.Values, ct);
                            inserted++;
                            if (built.NeedsReauth)
                                needsReauth++;
                            if (built.Redacted)
                                redacted++;
                        }
                        catch (Exception exception)
                        {
                            failed++;
                            errors.Add($"{plan.Legacy}: riga {key} non importata: {exception.Message}");
                        }
                    }

                    counters[plan.Legacy] = (inserted, skipped, failed);
                }

                reports = reports
                    .Select(report => counters.TryGetValue(report.Table, out var counter)
                        ? report with
                        {
                            Inserted = counter.Inserted,
                            Skipped = counter.Skipped,
                            Failed = counter.Failed
                        }
                        : report)
                    .ToList();

                if (errors.Count > 0)
                {
                    await transaction.RollbackAsync(ct);
                    rolledBack = true;
                }
                else
                {
                    await ApplyDeferredAsync(connection, transaction, input, ct);
                    await transaction.CommitAsync(ct);
                }
            }
            catch (Exception exception)
            {
                try
                {
                    await transaction.RollbackAsync(ct);
                }
                catch
                {
                    // The connection may already be gone; the report still records the failure.
                }

                rolledBack = true;
                errors.Add($"Apply annullato: {exception.Message}");
            }
        }
        catch (Exception exception)
        {
            errors.Add($"Connessione o transazione fallita: {exception.Message}");
        }

        if (rolledBack)
        {
            reports = reports.Select(report => report with { Inserted = 0 }).ToList();
            needsReauth = 0;
            redacted = 0;
        }

        return NewReport(options, reports, errors, warnings, needsReauth, redacted, rolledBack);
    }

    private static ImportReport NewReport(
        ImportOptions options,
        List<TableReport> reports,
        List<string> errors,
        List<string> warnings,
        int needsReauth,
        int redacted,
        bool rolledBack) => new(
        options.InputPath,
        !options.Apply,
        DateTime.UtcNow,
        reports,
        errors,
        warnings,
        needsReauth,
        redacted,
        rolledBack,
        options.NewKeyVersion);

    private static Dictionary<string, List<JsonElement>> ReadInput(JsonElement root)
    {
        var result = new Dictionary<string, List<JsonElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
                continue;
            result[property.Name] = property.Value.EnumerateArray().Select(row => row.Clone()).ToList();
        }

        return result;
    }

    private static List<JsonElement> Rows(
        Dictionary<string, List<JsonElement>> input,
        string table) =>
        input.TryGetValue(table, out var rows) ? rows : [];

    private static void ValidateCipherKeys(ImportOptions options, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(options.OldKey) &&
            LegacyCipher.ValidateKey(options.OldKey) is { } oldError)
            errors.Add($"LEGACY_IMPORT_OLD_KEY: {oldError}");
        if (!string.IsNullOrWhiteSpace(options.NewKey) &&
            LegacyCipher.ValidateKey(options.NewKey) is { } newError)
            errors.Add($"LEGACY_IMPORT_NEW_KEY: {newError}");
        if (options.NewKeyVersion < 1)
            errors.Add("--new-key-version deve essere maggiore o uguale a 1.");
    }

    private static void AddMissingTablesWarning(
        Dictionary<string, List<JsonElement>> input,
        List<string> warnings)
    {
        var missing = ImportPlans.All
            .Where(plan => !input.ContainsKey(plan.Legacy))
            .Select(plan => plan.Legacy)
            .ToList();
        if (missing.Count > 0)
            warnings.Add($"Tabelle assenti nella fixture: {string.Join(", ", missing)}.");
    }

    private static int CountNeedsReauth(
        Dictionary<string, List<JsonElement>> input,
        ImportOptions options,
        List<string> warnings)
    {
        var rows = Rows(input, "ServerCredentials");
        if (rows.Count == 0)
            return 0;

        if (LegacyCipher.ValidateKey(options.OldKey) is not null ||
            LegacyCipher.ValidateKey(options.NewKey) is not null)
            return 0;

        if (string.IsNullOrWhiteSpace(options.OldKey) || string.IsNullOrWhiteSpace(options.NewKey))
        {
            warnings.Add(
                $"{rows.Count} credenziali: chiave vecchia o nuova assente, verranno importate come NeedsReauth.");
            return rows.Count;
        }

        var cipher = new LegacyCipher(options.OldKey);
        var count = 0;
        foreach (var row in rows)
        {
            var id = Get(row, "Id")?.GetRawText() ?? "?";
            var secret = Get(row, "EncryptedSecret")?.GetString() ?? "";
            if (string.IsNullOrEmpty(secret))
            {
                count++;
                warnings.Add($"ServerCredentials {id}: segreto assente, NeedsReauth.");
                continue;
            }

            try
            {
                cipher.Decrypt(secret);
            }
            catch
            {
                count++;
                warnings.Add(
                    $"ServerCredentials {id}: segreto non decifrabile con la vecchia chiave, NeedsReauth.");
            }
        }

        return count;
    }

    private static void Validate(
        Dictionary<string, List<JsonElement>> input,
        List<string> errors)
    {
        foreach (var plan in ImportPlans.All)
        {
            var rows = Rows(input, plan.Legacy);
            if (rows.Count == 0)
                continue;

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var key = Key(row, plan);
                if (!keys.Add(key))
                    errors.Add($"{plan.Legacy}: chiave duplicata {key}");

                foreach (var (targetColumn, transform) in plan.Transforms)
                {
                    if (!plan.Columns.TryGetValue(targetColumn, out var source))
                        continue;
                    var value = Get(row, source);
                    if (value is null)
                        continue;

                    try
                    {
                        transform(value.Value);
                    }
                    catch (InvalidOperationException exception)
                    {
                        errors.Add($"{plan.Legacy} {key}: {exception.Message}");
                    }
                }

                ValidateRow(plan, row, key, errors);
            }

            foreach (var (table, columns) in Unique.Where(x => x.Table == plan.Target))
            {
                var sourceColumns = columns.Select(column => SourceColumn(plan, column)).ToArray();
                var values = rows.Select(row => string.Join("|", sourceColumns.Select(column =>
                        Get(row, column)?.GetRawText() ?? "")))
                    .Where(value => value.Replace("|", "").Length > 0)
                    .ToList();
                var duplicates = values.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key);
                foreach (var duplicate in duplicates)
                    errors.Add($"{plan.Legacy}: vincolo unico violato ({string.Join(",", columns)}): {duplicate}");
            }
        }

        foreach (var (table, column, parent) in ForeignKeys)
        {
            var rows = Rows(input, table);
            if (rows.Count == 0)
                continue;

            var plan = ImportPlans.All.FirstOrDefault(candidate => candidate.Legacy == table);
            var sourceColumn = plan is null ? column : SourceColumn(plan, column);
            var parentIds = Rows(input, parent)
                .Select(row => Get(row, "Id")?.GetRawText())
                .Where(x => x is not null)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var value = Get(row, sourceColumn);
                if (value is null || value.Value.ValueKind == JsonValueKind.Null)
                    continue;
                if (!parentIds.Contains(value.Value.GetRawText()))
                    errors.Add($"{table}.{column}={value.Value.GetRawText()} senza {parent}.Id");
            }
        }

        var connections = Rows(input, "ProxyConnections");
        foreach (var row in connections)
        {
            var source = Get(row, "SourceServerId")?.GetRawText();
            var target = Get(row, "TargetServerId")?.GetRawText();
            if (source is not null && source == target)
                errors.Add($"ProxyConnections: source e target uguali ({source})");
        }

        foreach (var table in new[] { "Servers", "ServerApplications", "ReachabilityTests" })
        {
            foreach (var row in Rows(input, table))
            {
                var port = Get(row, table == "Servers" ? "SshPort" : table == "ServerApplications" ? "ExposedPort" : "Port");
                if (port is not null && port.Value.TryGetInt32(out var p) && (p is < 1 or > 65_535))
                    errors.Add($"{table}: porta fuori intervallo {p}");
            }
        }

        var diagrams = Rows(input, "Diagrams");
        if (diagrams.Count(row => Get(row, "IsDefault")?.ValueKind == JsonValueKind.True) > 1)
            errors.Add("Diagrams: più di un IsDefault");
    }

    private static void ValidateRow(
        TablePlan plan,
        JsonElement row,
        string key,
        List<string> errors)
    {
        if (plan.KeyColumns.Length != 1 || plan.KeyColumns[0] != "Id")
            return;

        var id = Get(row, "Id");
        if (id is null || id.Value.ValueKind == JsonValueKind.Null)
        {
            errors.Add($"{plan.Legacy} {key}: Id mancante");
            return;
        }

        if (id.Value.TryGetInt32(out var idValue) && idValue <= 0)
            errors.Add($"{plan.Legacy} {key}: Id non positivo");
    }

    private static string Key(JsonElement row, TablePlan plan) =>
        string.Join("|", plan.KeyColumns.Select(column =>
            Get(row, SourceColumn(plan, column))?.GetRawText() ?? ""));

    private static string SourceColumn(TablePlan plan, string targetColumn) =>
        plan.Columns.GetValueOrDefault(targetColumn, targetColumn);

    private static BuiltRow BuildValues(
        TablePlan plan,
        JsonElement row,
        ImportOptions options,
        LegacyCipher oldCipher,
        LegacyCipher newCipher)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (target, source) in plan.Columns)
        {
            var element = Get(row, source);
            if (element is null)
                continue;
            values[target] = plan.Transforms.TryGetValue(target, out var transform)
                ? transform(element.Value)
                : Convert(element.Value, target);
        }

        if (plan.Audit)
        {
            var createdAt = Get(row, "CreatedAt") is { } created
                ? Convert(created, "CreatedAt")
                : DateTime.UtcNow;
            values["CreatedAt"] = createdAt;
            values["UpdatedAt"] = Get(row, "UpdatedAt") is { } updated
                ? Convert(updated, "UpdatedAt")
                : createdAt;
            values["Version"] = 0;
        }

        var needsReauth = false;
        if (plan.Target == "ServerCredentials")
        {
            var secret = Get(row, "EncryptedSecret")?.GetString() ?? "";
            var passphrase = Get(row, "EncryptedPassphrase")?.GetString();
            var canMigrate = oldCipher.HasKey && newCipher.HasKey && !string.IsNullOrEmpty(secret);
            if (canMigrate)
            {
                try
                {
                    values["EncryptedSecret"] = newCipher.Encrypt(oldCipher.Decrypt(secret));
                    values["EncryptedPassphrase"] = string.IsNullOrEmpty(passphrase)
                        ? null
                        : newCipher.Encrypt(oldCipher.Decrypt(passphrase));
                    values["KeyVersion"] = options.NewKeyVersion;
                    values["NeedsReauth"] = false;
                }
                catch
                {
                    values["EncryptedSecret"] = "";
                    values["EncryptedPassphrase"] = null;
                    values["KeyVersion"] = options.NewKeyVersion;
                    values["NeedsReauth"] = true;
                    needsReauth = true;
                }
            }
            else
            {
                values["EncryptedSecret"] = "";
                values["EncryptedPassphrase"] = null;
                values["KeyVersion"] = options.NewKeyVersion;
                values["NeedsReauth"] = true;
                needsReauth = true;
            }
        }

        var redacted = false;
        if (plan.Redact && options.RedactAudit)
        {
            values["Script"] = "";
            values["Output"] = "";
            redacted = true;
        }

        return new BuiltRow(values, needsReauth, redacted);
    }

    private static object? Convert(JsonElement value, string targetColumn) =>
        value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var integer) => integer,
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when targetColumn.EndsWith("At", StringComparison.Ordinal) &&
                                      DateTime.TryParse(
                                          value.GetString(),
                                          CultureInfo.InvariantCulture,
                                          DateTimeStyles.RoundtripKind,
                                          out var date) => date,
            JsonValueKind.String => value.GetString(),
            _ => value.GetRawText()
        };

    private static async Task<ExistingRow> InspectAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        TablePlan plan,
        JsonElement row,
        string key,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(key))
            return new ExistingRow(false, null);

        var where = plan.KeyColumns.Select((column, index) => $"`{column}` = @k{index}");
        var select = plan.NaturalKey.Length == 0
            ? "1"
            : string.Join(", ", plan.NaturalKey.Select(column => $"`{column}`"));
        await using var command = new MySqlCommand(
            $"SELECT {select} FROM `{plan.Target}` WHERE {string.Join(" AND ", where)} LIMIT 1",
            connection,
            transaction);
        var values = key.Split('|');
        for (var i = 0; i < plan.KeyColumns.Length && i < values.Length; i++)
            command.Parameters.AddWithValue($"@k{i}", values[i]);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new ExistingRow(false, null);
        if (plan.NaturalKey.Length == 0)
            return new ExistingRow(true, null);

        var mismatches = new List<string>();
        for (var i = 0; i < plan.NaturalKey.Length; i++)
        {
            var databaseValue = reader.IsDBNull(i) ? null : reader.GetValue(i);
            var element = Get(row, SourceColumn(plan, plan.NaturalKey[i]));
            var sourceValue = element is null ? null : Convert(element.Value, plan.NaturalKey[i]);
            if (!Same(databaseValue, sourceValue))
                mismatches.Add(
                    $"{plan.NaturalKey[i]}: atteso {Show(sourceValue)}, trovato {Show(databaseValue)}");
        }

        return new ExistingRow(true, mismatches.Count == 0 ? null : string.Join("; ", mismatches));
    }

    private static bool Same(object? left, object? right)
    {
        if (left is null || left is DBNull)
            return right is null;
        if (right is null)
            return false;
        if (left is string leftText && right is string rightText)
            return string.Equals(leftText, rightText, StringComparison.OrdinalIgnoreCase);
        if (left is DateTime leftDate && right is DateTime rightDate)
            return leftDate == rightDate;
        if (IsNumber(left) && IsNumber(right))
            return System.Convert.ToDecimal(left, CultureInfo.InvariantCulture) ==
                   System.Convert.ToDecimal(right, CultureInfo.InvariantCulture);
        return left.Equals(right);
    }

    private static bool IsNumber(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static string Show(object? value) => value switch
    {
        null => "NULL",
        string text => $"'{text}'",
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "NULL"
    };

    private static async Task CheckCollisionsAsync(
        string connectionString,
        Dictionary<string, List<JsonElement>> input,
        List<string> errors,
        CancellationToken ct)
    {
        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(ct);

            foreach (var plan in ImportPlans.All)
            {
                foreach (var row in Rows(input, plan.Legacy))
                {
                    var key = Key(row, plan);
                    var existing = await InspectAsync(connection, null, plan, row, key, ct);
                    if (existing.Mismatch is not null)
                        errors.Add(
                            $"{plan.Legacy} {key}: Id già usato da una riga diversa ({existing.Mismatch}).");
                }
            }
        }
        catch (Exception exception)
        {
            errors.Add($"Verifica collisioni fallita: {exception.Message}");
        }
    }

    private static async Task InsertAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        TablePlan plan,
        Dictionary<string, object?> values,
        CancellationToken ct)
    {
        var columns = values.Keys.ToArray();
        var sql = $"INSERT INTO `{plan.Target}` ({string.Join(", ", columns.Select(c => $"`{c}`"))}) " +
                  $"VALUES ({string.Join(", ", columns.Select((_, i) => $"@p{i}"))})";
        await using var command = new MySqlCommand(sql, connection, transaction);
        for (var i = 0; i < columns.Length; i++)
            command.Parameters.AddWithValue($"@p{i}", values[columns[i]] ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task ApplyDeferredAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Dictionary<string, List<JsonElement>> input,
        CancellationToken ct)
    {
        foreach (var plan in ImportPlans.All.Where(x => x.Deferred.Count > 0))
        {
            var rows = Rows(input, plan.Legacy);
            foreach (var row in rows)
            {
                var id = Get(row, "Id")?.GetRawText();
                if (id is null)
                    continue;

                foreach (var (targetColumn, legacyColumn) in plan.Deferred)
                {
                    var value = Get(row, legacyColumn);
                    if (value is null || value.Value.ValueKind == JsonValueKind.Null)
                        continue;

                    await using var command = new MySqlCommand(
                        $"UPDATE `{plan.Target}` SET `{targetColumn}` = @value WHERE `Id` = @id",
                        connection,
                        transaction);
                    command.Parameters.AddWithValue("@value", Convert(value.Value, targetColumn));
                    command.Parameters.AddWithValue("@id", id);
                    await command.ExecuteNonQueryAsync(ct);
                }
            }
        }
    }

    private static JsonElement? Get(JsonElement row, string name)
    {
        foreach (var property in row.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }
}

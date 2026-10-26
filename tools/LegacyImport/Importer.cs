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
    bool RedactAudit);

public sealed record TableReport(string Table, int Read, int Inserted, int Skipped, int Failed);

public sealed record ImportReport(
    string Input,
    bool DryRun,
    DateTime GeneratedAt,
    List<TableReport> Tables,
    List<string> Errors,
    List<string> Warnings,
    int NeedsReauth,
    int RedactedAudit);

public sealed class Importer
{
    private static readonly (string Table, string Column, string ParentTable)[] ForeignKeys =
    [
        ("ProviderEnvironments", "ProviderId", "Providers"),
        ("ProviderEnvironments", "EnvironmentDefinitionId", "EnvironmentDefinitions"),
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
        ("ProxyRoutes", "ProxyConnectionId", "ProxyConnections"),
        ("ProxyRoutes", "TargetServerApplicationId", "ServerApplications"),
        ("ProxyRoutes", "TargetProxyConnectionId", "ProxyConnections"),
        ("ReachabilityTests", "ServerId", "Servers"),
        ("DiagramScopeItems", "DiagramId", "Diagrams"),
        ("DiagramNodes", "DiagramId", "Diagrams"),
        ("DiagramEdges", "DiagramId", "Diagrams")
    ];

    private static readonly (string Table, string[] Columns)[] Unique =
    [
        ("Providers", ["Name"]),
        ("EnvironmentDefinitions", ["Name"]),
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

    public async Task<ImportReport> RunAsync(ImportOptions options, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(options.InputPath, ct));
        var input = ReadInput(document.RootElement);

        var errors = new List<string>();
        var warnings = new List<string>();
        var needsReauth = 0;
        var redacted = 0;

        Validate(
            input, errors, warnings,
            !string.IsNullOrWhiteSpace(options.OldKey),
            !string.IsNullOrWhiteSpace(options.NewKey));

        var reports = new List<TableReport>();
        if (!options.Apply)
        {
            foreach (var plan in ImportPlans.All)
            {
                var rows = Rows(input, plan.Legacy);
                reports.Add(new TableReport(plan.Legacy, rows.Count, 0, 0, 0));
            }

            needsReauth = string.IsNullOrWhiteSpace(options.OldKey) ||
                          string.IsNullOrWhiteSpace(options.NewKey)
                ? Rows(input, "ServerCredentials").Count
                : 0;

            if (options.RedactAudit)
                redacted = Rows(input, "CommandExecutions").Count;

            return NewReport(options, reports, errors, warnings, needsReauth, redacted);
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            errors.Add("--connection è obbligatorio con --apply.");
            return NewReport(options, reports, errors, warnings, needsReauth, redacted);
        }

        if (errors.Count > 0)
            return NewReport(options, reports, errors, warnings, needsReauth, redacted);

        await using var connection = new MySqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct);

        var oldCipher = new LegacyCipher(options.OldKey);
        var newCipher = new LegacyCipher(options.NewKey);

        foreach (var plan in ImportPlans.All)
        {
            var rows = Rows(input, plan.Legacy);
            if (rows.Count == 0)
                continue;

            var inserted = 0;
            var skipped = 0;
            var failed = 0;

            await using var transaction = await connection.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in rows)
                {
                    var key = Key(row, plan);
                    if (await ExistsAsync(connection, transaction, plan, key, ct))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var values = BuildValues(
                            plan, row, options, oldCipher, newCipher, ref needsReauth, ref redacted);
                        await InsertAsync(connection, transaction, plan, values, ct);
                        inserted++;
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        errors.Add($"{plan.Legacy}: riga {key} non importata: {exception.Message}");
                    }
                }

                await transaction.CommitAsync(ct);
            }
            catch (Exception exception)
            {
                await transaction.RollbackAsync(ct);
                errors.Add($"{plan.Legacy}: transazione annullata: {exception.Message}");
            }

            reports.Add(new TableReport(plan.Legacy, rows.Count, inserted, skipped, failed));
        }

        await ApplyDeferredAsync(connection, input, ct);

        return NewReport(options, reports, errors, warnings, needsReauth, redacted);
    }

    private static ImportReport NewReport(
        ImportOptions options,
        List<TableReport> reports,
        List<string> errors,
        List<string> warnings,
        int needsReauth,
        int redacted) => new(
        options.InputPath,
        !options.Apply,
        DateTime.UtcNow,
        reports,
        errors,
        warnings,
        needsReauth,
        redacted);

    private static Dictionary<string, List<JsonElement>> ReadInput(JsonElement root)
    {
        var result = new Dictionary<string, List<JsonElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
                continue;
            result[property.Name] = property.Value.EnumerateArray().ToList();
        }

        return result;
    }

    private static List<JsonElement> Rows(
        Dictionary<string, List<JsonElement>> input, string table) =>
        input.TryGetValue(table, out var rows) ? rows : [];

    private static void Validate(
        Dictionary<string, List<JsonElement>> input,
        List<string> errors,
        List<string> warnings,
        bool hasOldKey,
        bool hasNewKey)
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

            foreach (var (table, columns) in Unique.Where(x => x.Table == plan.Legacy))
            {
                var values = rows.Select(row => string.Join("|", columns.Select(column =>
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
            var parents = Rows(input, parent);
            if (rows.Count == 0 || parents.Count == 0)
                continue;

            var parentIds = parents.Select(row => Get(row, "Id")?.GetRawText())
                .Where(x => x is not null)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var value = Get(row, column);
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

        var credentials = Rows(input, "ServerCredentials");
        if (credentials.Count > 0 && (!hasOldKey || !hasNewKey))
            warnings.Add($"{credentials.Count} credenziali: chiave vecchia o nuova assente, verranno importate come NeedsReauth");
    }

    private static void ValidateRow(
        TablePlan plan,
        JsonElement row,
        string key,
        List<string> errors)
    {
        if (plan.Legacy != "CommandExecutions" &&
            Get(row, "Id") is { } id &&
            id.TryGetInt32(out var idValue) &&
            idValue <= 0)
            errors.Add($"{plan.Legacy} {key}: Id non positivo");
    }

    private static string Key(JsonElement row, TablePlan plan) =>
        string.Join("|", plan.KeyColumns.Select(column =>
            Get(row, plan.Columns.GetValueOrDefault(column, column))?.GetRawText() ?? ""));

    private static Dictionary<string, object?> BuildValues(
        TablePlan plan,
        JsonElement row,
        ImportOptions options,
        LegacyCipher oldCipher,
        LegacyCipher newCipher,
        ref int needsReauth,
        ref int redacted)
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
                    values["KeyVersion"] = 1;
                    values["NeedsReauth"] = false;
                }
                catch
                {
                    values["EncryptedSecret"] = "";
                    values["EncryptedPassphrase"] = null;
                    values["KeyVersion"] = 1;
                    values["NeedsReauth"] = true;
                    needsReauth++;
                }
            }
            else
            {
                values["EncryptedSecret"] = "";
                values["EncryptedPassphrase"] = null;
                values["KeyVersion"] = 1;
                values["NeedsReauth"] = true;
                needsReauth++;
            }
        }

        if (plan.Redact && options.RedactAudit)
        {
            values["Script"] = "";
            values["Output"] = "";
            redacted++;
        }

        return values;
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
                                      DateTime.TryParse(value.GetString(), out var date) => date,
            JsonValueKind.String => value.GetString(),
            _ => value.GetRawText()
        };

    private static async Task<bool> ExistsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        TablePlan plan,
        string key,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        var columns = plan.KeyColumns
            .Select(column => plan.Columns.TryGetValue(column, out var source) ? column : column)
            .ToArray();
        var parts = columns.Select((column, index) => $"`{column}` = @k{index}");
        await using var command = new MySqlCommand(
            $"SELECT 1 FROM `{plan.Target}` WHERE {string.Join(" AND ", parts)} LIMIT 1",
            connection,
            transaction);
        var values = key.Split('|');
        for (var i = 0; i < columns.Length && i < values.Length; i++)
            command.Parameters.AddWithValue($"@k{i}", values[i]);
        return await command.ExecuteScalarAsync(ct) is not null;
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
                        connection);
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

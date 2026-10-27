using System.Data;
using System.Text.Json;
using MySql.Data.MySqlClient;

namespace InfraOrgIt.LegacyImport;

/// Adapter di sola lettura dal MySQL legacy: produce le stesse righe
/// JSON-shaped che l'importer accetta da file. Le tabelle assenti restano
/// fuori dall'input, così il report le segnala come mancanti.
public static class MySqlLegacySource
{
    public static async Task<Dictionary<string, List<JsonElement>>> ReadAsync(
        string connectionString,
        CancellationToken ct)
    {
        var result = new Dictionary<string, List<JsonElement>>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);

        foreach (var plan in ImportPlans.All)
        {
            List<JsonElement> rows;
            try
            {
                rows = await ReadTableAsync(connection, plan.Legacy, ct);
            }
            catch (MySqlException exception) when (exception.Number == 1146)
            {
                continue;
            }

            result[plan.Legacy] = rows;
        }

        return result;
    }

    private static async Task<List<JsonElement>> ReadTableAsync(
        MySqlConnection connection,
        string table,
        CancellationToken ct)
    {
        var rows = new List<JsonElement>();
        await using var command = new MySqlCommand($"SELECT * FROM `{table}`", connection)
        {
            CommandTimeout = 300
        };
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = Normalize(reader.IsDBNull(i) ? null : reader.GetValue(i));

            rows.Add(JsonSerializer.SerializeToElement(row));
        }

        return rows;
    }

    private static object? Normalize(object? value) => value switch
    {
        null => null,
        bool or byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal or string or DateTime or DateOnly or TimeSpan => value,
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => value.ToString()
    };
}

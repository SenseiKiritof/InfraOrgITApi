using System.Collections;
using System.Security.Cryptography;
using InfraOrgIt.LegacyImport;
using MySql.Data.MySqlClient;
using SSecureUtils.Utils;

// One-shot rewrap of ServerCredentials after the switch to SSecureUtils: the library
// derives the key with SHA-256 and prefixes the value with "v2:", while the previous
// format used the raw key bytes and no prefix. Rows already carrying "v2:" are skipped,
// so a second run is a no-op.

const string KeyPrefix = "ServerAccess__CredentialKeys__";
const string ConnectionVariable = "REWRAP_CONNECTION";

var unknown = args.Where(x => x is not ("--apply" or "--self-check")).ToArray();
if (unknown.Length > 0)
{
    Console.Error.WriteLine($"Argomento sconosciuto: {string.Join(' ', unknown)}");
    return Usage();
}

if (args.Contains("--self-check"))
    return SelfCheck();

var apply = args.Contains("--apply");
var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"{ConnectionVariable} mancante.");
    return Usage();
}

var keys = ReadKeys();
var currentVersion = ReadCurrentVersion(keys);
if (currentVersion is null)
    return 2;

var currentKey = keys[currentVersion.Value.ToString()];

var rows = new List<Row>();
var failed = 0;
await using (var connection = new MySqlConnection(connectionString))
{
    await connection.OpenAsync();
    await using var select = new MySqlCommand(
        "SELECT Id, EncryptedSecret, EncryptedPassphrase, KeyVersion FROM ServerCredentials", connection);
    await using var reader = await select.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new Row(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3)));
    }

    var updates = new List<Row>();
    var skipped = 0;

    foreach (var row in rows)
    {
        if (string.IsNullOrEmpty(row.Secret) || IsNewFormat(row.Secret))
        {
            skipped++;
            continue;
        }

        if (!keys.TryGetValue(row.KeyVersion.ToString(), out var oldKey))
        {
            Console.Error.WriteLine($"  id {row.Id}: ServerAccess__CredentialKeys__{row.KeyVersion} mancante");
            failed++;
            continue;
        }

        try
        {
            updates.Add(row with
            {
                Secret = Rewrap(row.Secret, oldKey, currentKey),
                Passphrase = string.IsNullOrEmpty(row.Passphrase)
                    ? row.Passphrase
                    : Rewrap(row.Passphrase, oldKey, currentKey),
                KeyVersion = currentVersion.Value
            });
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            Console.Error.WriteLine($"  id {row.Id}: decifratura fallita ({exception.GetType().Name}), riga invariata");
            failed++;
        }
    }

    Console.WriteLine($"{(apply ? "APPLY" : "DRY-RUN")} · {rows.Count} righe, da ricifrare {updates.Count}, " +
                      $"già nuove {skipped}, fallite {failed}");

    if (apply && updates.Count > 0)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var row in updates)
        {
            await using var update = new MySqlCommand(
                "UPDATE ServerCredentials SET EncryptedSecret = @secret, EncryptedPassphrase = @passphrase, " +
                "KeyVersion = @version WHERE Id = @id", connection, (MySqlTransaction)transaction);
            update.Parameters.AddWithValue("@secret", row.Secret);
            update.Parameters.AddWithValue("@passphrase", (object?)row.Passphrase ?? DBNull.Value);
            update.Parameters.AddWithValue("@version", row.KeyVersion);
            update.Parameters.AddWithValue("@id", row.Id);
            await update.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        Console.WriteLine($"  scritte {updates.Count} righe nella versione {currentVersion}");
    }
    else if (!apply && updates.Count > 0)
    {
        Console.WriteLine("  nessuna scrittura: rilanciare con --apply");
    }
}

return failed == 0 ? 0 : 1;

static int Usage()
{
    Console.Error.WriteLine(
        "Uso: InfraOrgIt.RewrapCredentials [--apply] [--self-check]\n" +
        "  senza --apply esegue un dry-run, --self-check verifica la conversione senza database.\n" +
        $"  Env: {ConnectionVariable}, {KeyPrefix}<n> per ogni versione in uso, ServerAccess__CurrentKeyVersion.");
    return 2;
}

static Dictionary<string, string> ReadKeys()
{
    var keys = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
    {
        var name = entry.Key.ToString()!;
        if (name.StartsWith(KeyPrefix, StringComparison.Ordinal) && entry.Value is { } value)
            keys[name[KeyPrefix.Length..]] = value.ToString()!;
    }

    return keys;
}

static int? ReadCurrentVersion(Dictionary<string, string> keys)
{
    var raw = Environment.GetEnvironmentVariable("ServerAccess__CurrentKeyVersion") ?? "1";
    if (!int.TryParse(raw, out var version))
    {
        Console.Error.WriteLine($"ServerAccess__CurrentKeyVersion non numerica: {raw}");
        return null;
    }

    if (!keys.ContainsKey(version.ToString()))
    {
        Console.Error.WriteLine($"ServerAccess__CredentialKeys__{version} mancante.");
        return null;
    }

    return version;
}

static bool IsNewFormat(string value) => value.StartsWith("v2:", StringComparison.Ordinal);

static string Rewrap(string ciphertext, string oldKey, string newKey) =>
    SecureUtils.Encrypt(new LegacyCipher(oldKey).Decrypt(ciphertext), newKey);

static int SelfCheck()
{
    var key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray());
    const string plaintext = "super-secret";

    var legacy = new LegacyCipher(key).Encrypt(plaintext);
    if (IsNewFormat(legacy))
        throw new InvalidOperationException("Il formato legacy non deve avere il prefisso v2:");

    var rewrapped = Rewrap(legacy, key, key);
    if (!IsNewFormat(rewrapped))
        throw new InvalidOperationException("Il valore ricifrato deve avere il prefisso v2:");
    if (SecureUtils.Decrypt(rewrapped, key) != plaintext)
        throw new InvalidOperationException("Roundtrip di ricifratura fallito.");

    Console.WriteLine("self-check ok: legacy → v2: → plaintext");
    return 0;
}

internal sealed record Row(int Id, string Secret, string? Passphrase, int KeyVersion);

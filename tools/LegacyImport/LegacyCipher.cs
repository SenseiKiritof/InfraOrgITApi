using System.Security.Cryptography;
using System.Text;

namespace InfraOrgIt.LegacyImport;

public sealed class LegacyCipher(string? base64Key)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public bool HasKey => !string.IsNullOrWhiteSpace(base64Key);

    public static string? ValidateKey(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            return null;

        try
        {
            return Convert.FromBase64String(base64Key).Length == 32
                ? null
                : "La chiave AES-GCM deve essere di 32 byte.";
        }
        catch (FormatException)
        {
            return "La chiave AES-GCM non è base64 valido.";
        }
    }

    public string Decrypt(string ciphertext)
    {
        var key = Key();
        var bytes = Convert.FromBase64String(ciphertext);
        var plain = new byte[bytes.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            bytes.AsSpan(0, NonceSize),
            bytes.AsSpan(NonceSize + TagSize),
            bytes.AsSpan(NonceSize, TagSize),
            plain);
        return Encoding.UTF8.GetString(plain);
    }

    public string Encrypt(string plaintext)
    {
        var key = Key();
        var data = Encoding.UTF8.GetBytes(plaintext);
        var result = new byte[NonceSize + TagSize + data.Length];
        var nonce = result.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, result.AsSpan(NonceSize + TagSize), result.AsSpan(NonceSize, TagSize));
        return Convert.ToBase64String(result);
    }

    private byte[] Key()
    {
        if (!HasKey)
            throw new InvalidOperationException("Chiave AES-GCM mancante.");
        var key = Convert.FromBase64String(base64Key!);
        return key.Length == 32
            ? key
            : throw new InvalidOperationException("La chiave AES-GCM deve essere di 32 byte.");
    }
}

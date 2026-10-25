using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.BuildingBlocks.Security;

public sealed class CredentialCipher(IOptions<ServerAccessOptions> options)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public (string Ciphertext, int KeyVersion) Encrypt(string plaintext)
    {
        var version = options.Value.CurrentKeyVersion;
        var key = KeyFor(version);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var result = new byte[NonceSize + TagSize + data.Length];
        var nonce = result.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, result.AsSpan(NonceSize + TagSize), result.AsSpan(NonceSize, TagSize));
        return (Convert.ToBase64String(result), version);
    }

    public string Decrypt(string ciphertext, int keyVersion)
    {
        var bytes = Convert.FromBase64String(ciphertext);
        if (bytes.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext troppo corto.");

        var plain = new byte[bytes.Length - NonceSize - TagSize];
        using var aes = new AesGcm(KeyFor(keyVersion), TagSize);
        aes.Decrypt(
            bytes.AsSpan(0, NonceSize),
            bytes.AsSpan(NonceSize + TagSize),
            bytes.AsSpan(NonceSize, TagSize),
            plain);
        return Encoding.UTF8.GetString(plain);
    }

    private byte[] KeyFor(int version)
    {
        if (!options.Value.CredentialKeys.TryGetValue(version.ToString(), out var encoded))
            throw new InvalidOperationException($"ServerAccess:CredentialKeys:{version} mancante.");

        var key = Convert.FromBase64String(encoded);
        return key.Length == 32
            ? key
            : throw new InvalidOperationException(
                $"ServerAccess:CredentialKeys:{version} deve essere di 32 byte.");
    }
}

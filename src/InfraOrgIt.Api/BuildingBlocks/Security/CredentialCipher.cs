using Microsoft.Extensions.Options;
using SSecureUtils.Utils;

namespace InfraOrgIt.Api.BuildingBlocks.Security;

public sealed class CredentialCipher(IOptions<ServerAccessOptions> options)
{
    public (string Ciphertext, int KeyVersion) Encrypt(string plaintext)
    {
        var version = options.Value.CurrentKeyVersion;
        return (SecureUtils.Encrypt(plaintext, KeyFor(version)), version);
    }

    public string Decrypt(string ciphertext, int keyVersion) =>
        SecureUtils.Decrypt(ciphertext, KeyFor(keyVersion));

    /// <summary>
    /// SSecureUtils passa la chiave in SHA-256 prima di usarla con AES-GCM, quindi il valore
    /// in configurazione non è più la chiave AES grezza: resta una stringa base64 di 32 byte,
    /// ma i valori cifrati con il formato precedente non sono più leggibili (serve il rewrap).
    /// </summary>
    private string KeyFor(int version)
    {
        if (!options.Value.CredentialKeys.TryGetValue(version.ToString(), out var encoded))
            throw new InvalidOperationException($"ServerAccess:CredentialKeys:{version} mancante.");

        return Convert.FromBase64String(encoded).Length == 32
            ? encoded
            : throw new InvalidOperationException(
                $"ServerAccess:CredentialKeys:{version} deve essere di 32 byte.");
    }
}

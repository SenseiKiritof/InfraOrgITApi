using System.Security.Cryptography;
using System.Text;

namespace InfraOrgIt.Api.Features.Devices;

public sealed record DeviceCipherResult(
    string Aad,
    string WrappedKey,
    string Nonce,
    string Ciphertext,
    string Tag);

/// <summary>
/// Hybrid encryption for secrets downloaded by a desktop: a random AES-256-GCM
/// key protects the payload, the device RSA public key wraps that key with
/// OAEP-SHA256. The AAD binds the ciphertext to the credential, device and
/// credential key version.
/// </summary>
public static class DeviceCipher
{
    public static DeviceCipherResult Encrypt(
        string publicKeyPem,
        string payload,
        string aad)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        if (rsa.KeySize < 2048)
            throw new CryptographicException("Chiave dispositivo troppo corta.");

        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(payload);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        var associated = Encoding.UTF8.GetBytes(aad);

        using (var aes = new AesGcm(key, tag.Length))
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associated);

        var wrapped = rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plaintext);

        return new DeviceCipherResult(
            aad,
            Convert.ToBase64String(wrapped),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(ciphertext),
            Convert.ToBase64String(tag));
    }
}

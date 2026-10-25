using System.Security.Cryptography;
using InfraOrgIt.Api.BuildingBlocks.Security;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.UnitTests;

public sealed class CredentialCipherTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray();

    [Fact]
    public void Encrypts_and_decrypts_with_key_version()
    {
        var cipher = CreateCipher(1);

        var (ciphertext, version) = cipher.Encrypt("super-secret");

        Assert.Equal(1, version);
        Assert.NotEqual("super-secret", ciphertext);
        Assert.Equal("super-secret", cipher.Decrypt(ciphertext, version));
    }

    [Fact]
    public void Tampered_ciphertext_fails()
    {
        var cipher = CreateCipher(1);
        var (ciphertext, version) = cipher.Encrypt("super-secret");
        var bytes = Convert.FromBase64String(ciphertext);
        bytes[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(
            () => cipher.Decrypt(Convert.ToBase64String(bytes), version));
    }

    [Fact]
    public void Unknown_key_version_fails()
    {
        var cipher = CreateCipher(1);
        var (ciphertext, _) = cipher.Encrypt("super-secret");

        Assert.Throws<InvalidOperationException>(() => cipher.Decrypt(ciphertext, 2));
    }

    private static CredentialCipher CreateCipher(int version) => new(
        Options.Create(new ServerAccessOptions
        {
            CurrentKeyVersion = version,
            CredentialKeys = new Dictionary<string, string>
            {
                ["1"] = Convert.ToBase64String(Key)
            }
        }));
}

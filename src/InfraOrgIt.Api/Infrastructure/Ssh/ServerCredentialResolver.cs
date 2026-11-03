using System.Security.Cryptography;
using System.Text;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Renci.SshNet;

namespace InfraOrgIt.Api.Infrastructure.Ssh;

/// <summary>Decifra una credenziale e ne costruisce il metodo di autenticazione.</summary>
public sealed class ServerCredentialResolver(CredentialCipher cipher)
{
    public (string Username, string Secret, string? Passphrase) Resolve(ServerCredential credential)
    {
        string secret;
        try
        {
            secret = cipher.Decrypt(credential.EncryptedSecret, credential.KeyVersion);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "credential_rejected",
                $"La credenziale {credential.Name} non è decifrabile: reinserire il segreto.");
        }

        string? passphrase = null;
        if (!string.IsNullOrEmpty(credential.EncryptedPassphrase))
        {
            try
            {
                passphrase = cipher.Decrypt(credential.EncryptedPassphrase, credential.KeyVersion);
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                throw new ApiException(
                    StatusCodes.Status409Conflict,
                    "credential_rejected",
                    $"La passphrase della credenziale {credential.Name} non è decifrabile.");
            }
        }

        return (credential.Username, secret, passphrase);
    }

    public AuthenticationMethod BuildAuthentication(ServerCredential credential)
    {
        var (username, secret, passphrase) = Resolve(credential);
        if (credential.AuthType == "Password")
            return new PasswordAuthenticationMethod(username, secret);

        try
        {
            return new PrivateKeyAuthenticationMethod(
                username,
                new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(secret)), passphrase));
        }
        catch (Exception exception)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "credential_rejected",
                $"La chiave privata della credenziale {credential.Name} non è valida: {exception.Message}");
        }
    }
}

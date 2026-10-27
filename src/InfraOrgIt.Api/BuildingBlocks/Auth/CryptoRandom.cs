using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public static class CryptoRandom
{
    public static string Token(int bytes = 32) =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    public static string Sha256Base64Url(string value) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(value)));
}

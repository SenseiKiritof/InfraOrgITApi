using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public static class CryptoRandom
{
    public static string Token(int bytes = 32) =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    public static string Sha256Base64Url(string value) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(value)));
}

public sealed class InMemorySessionStore(IMemoryCache cache) : ISessionStore
{
    public string Create(SessionData data)
    {
        var id = CryptoRandom.Token();
        cache.Set(Key(id), data, data.RefreshExpiresAt ?? data.AccessExpiresAt);
        return id;
    }

    public SessionData? Get(string id) =>
        cache.TryGetValue(Key(id), out SessionData? data) ? data : null;

    public void Update(string id, SessionData data) =>
        cache.Set(Key(id), data, data.RefreshExpiresAt ?? data.AccessExpiresAt);

    public void Remove(string id) => cache.Remove(Key(id));

    private static string Key(string id) => "session:" + id;
}

using Microsoft.Extensions.Caching.Memory;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed record LoginState(string State, string CodeVerifier);

public sealed class LoginStateStore : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 5_000 });

    public string Create(string state, string codeVerifier)
    {
        var id = CryptoRandom.Token();
        _cache.Set(Key(id), new LoginState(state, codeVerifier), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Lifetime,
            Size = 1
        });
        return id;
    }

    public LoginState? Consume(string id)
    {
        var key = Key(id);
        if (!_cache.TryGetValue(key, out LoginState? state) || state is null)
            return null;

        _cache.Remove(key);
        return state;
    }

    public void Dispose() => _cache.Dispose();

    private static string Key(string id) => "login:" + id;
}

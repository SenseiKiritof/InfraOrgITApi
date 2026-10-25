using Microsoft.Extensions.Caching.Memory;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed record LoginState(string State, string CodeVerifier);

public sealed class LoginStateStore(IMemoryCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public string Create(string state, string codeVerifier)
    {
        var id = CryptoRandom.Token();
        cache.Set(Key(id), new LoginState(state, codeVerifier), Lifetime);
        return id;
    }

    public LoginState? Consume(string id)
    {
        var key = Key(id);
        if (!cache.TryGetValue(key, out LoginState? state) || state is null)
            return null;

        cache.Remove(key);
        return state;
    }

    private static string Key(string id) => "login:" + id;
}

using System.Security.Cryptography;
using System.Text.Json;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

/// <summary>
/// In-process session store for the SQLite standalone mode. The desktop is a
/// single writer, so a semaphore is enough to keep the per-user session cap
/// deterministic without MySQL named locks.
/// </summary>
public sealed class LocalSessionStore(
    InfraOrgItDbContext db,
    IDataProtectionProvider protection,
    IOptions<SessionStoreOptions> options) : ISessionStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly IDataProtector _protector = protection.CreateProtector("InfraOrgIt.Sessions");

    public string Create(SessionData data)
    {
        Gate.Wait();
        try
        {
            var now = DateTime.UtcNow;
            db.Sessions.Where(x => x.ExpiresAt <= now).ExecuteDelete();

            var existing = db.Sessions
                .Where(x => x.Username == data.Username)
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.Id)
                .ToList();
            var excess = existing.Count - options.Value.MaxSessionsPerUser + 1;
            if (excess > 0)
                db.Sessions.Where(x => existing.Take(excess).Contains(x.Id)).ExecuteDelete();

            var token = CryptoRandom.Token();
            db.Sessions.Add(new SessionRecord
            {
                Id = Hash(token),
                Username = data.Username,
                Payload = Protect(data),
                ExpiresAt = Expiry(data),
                CreatedAt = now,
            });
            db.SaveChanges();
            return token;
        }
        finally
        {
            Gate.Release();
        }
    }

    public SessionData? Get(string id)
    {
        var record = db.Sessions.SingleOrDefault(x => x.Id == Hash(id));
        if (record is null) return null;

        if (record.ExpiresAt <= DateTime.UtcNow)
        {
            db.Sessions.Remove(record);
            db.SaveChanges();
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SessionData>(
                _protector.Unprotect(record.Payload), Json);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            db.Sessions.Remove(record);
            db.SaveChanges();
            return null;
        }
    }

    public void Update(string id, SessionData data)
    {
        var record = db.Sessions.SingleOrDefault(x => x.Id == Hash(id));
        if (record is null) return;
        record.Payload = Protect(data);
        record.ExpiresAt = Expiry(data);
        db.SaveChanges();
    }

    public void Remove(string id) =>
        db.Sessions.Where(x => x.Id == Hash(id)).ExecuteDelete();

    public void RemoveByUsername(string username) =>
        db.Sessions.Where(x => x.Username == username).ExecuteDelete();

    private string Protect(SessionData data) =>
        _protector.Protect(JsonSerializer.Serialize(data, Json));

    private static DateTime Expiry(SessionData data) =>
        (data.RefreshExpiresAt ?? data.AccessExpiresAt).UtcDateTime;

    private static string Hash(string id) => CryptoRandom.Sha256Base64Url(id);
}

using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed class MySqlSessionStore(
    InfraOrgItDbContext db,
    IDataProtectionProvider protection,
    IOptions<SessionStoreOptions> options) : ISessionStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _protector = protection.CreateProtector("InfraOrgIt.Sessions");

    public string Create(SessionData data)
    {
        var lockName = $"ds-infr-sessions:{data.Username}";
        var connection = db.Database.GetDbConnection();
        var alreadyOpen = connection.State == ConnectionState.Open;
        if (!alreadyOpen)
            db.Database.OpenConnection();

        try
        {
            var acquired = db.Database
                .SqlQueryRaw<int>("SELECT GET_LOCK({0}, 5) AS Value", lockName)
                .Single();
            if (acquired != 1)
                throw new InvalidOperationException("Timeout nell'acquisizione del lock sessioni.");

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
                CreatedAt = now
            });
            db.SaveChanges();
            return token;
        }
        finally
        {
            db.Database.ExecuteSqlRaw("DO RELEASE_LOCK({0})", lockName);
            if (!alreadyOpen)
                db.Database.CloseConnection();
        }
    }

    public SessionData? Get(string id)
    {
        var record = db.Sessions.SingleOrDefault(x => x.Id == Hash(id));
        if (record is null)
            return null;

        if (record.ExpiresAt <= DateTime.UtcNow)
        {
            db.Sessions.Remove(record);
            db.SaveChanges();
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SessionData>(_protector.Unprotect(record.Payload), Json);
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
        if (record is null)
            return;

        record.Payload = Protect(data);
        record.ExpiresAt = Expiry(data);
        db.SaveChanges();
    }

    public void Remove(string id) =>
        db.Sessions.Where(x => x.Id == Hash(id)).ExecuteDelete();

    private string Protect(SessionData data) =>
        _protector.Protect(JsonSerializer.Serialize(data, Json));

    private static DateTime Expiry(SessionData data) =>
        (data.RefreshExpiresAt ?? data.AccessExpiresAt).UtcDateTime;

    private static string Hash(string id) => CryptoRandom.Sha256Base64Url(id);
}

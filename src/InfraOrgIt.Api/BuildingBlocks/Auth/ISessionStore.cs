namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public interface ISessionStore
{
    string Create(SessionData data);
    SessionData? Get(string id);
    void Update(string id, SessionData data);
    void Remove(string id);

    /// <summary>
    /// Drops every active session of a user. Admin changes to roles or group
    /// memberships must call this so a revoked role is not served from the
    /// cached session payload (PT-07).
    /// </summary>
    void RemoveByUsername(string username);
}

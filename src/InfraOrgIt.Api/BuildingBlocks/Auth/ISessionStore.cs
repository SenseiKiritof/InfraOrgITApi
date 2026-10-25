namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public interface ISessionStore
{
    string Create(SessionData data);
    SessionData? Get(string id);
    void Update(string id, SessionData data);
    void Remove(string id);
}

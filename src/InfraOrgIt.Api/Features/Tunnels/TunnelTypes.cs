namespace InfraOrgIt.Api.Features.Tunnels;

public enum TunnelMode
{
    Local,
    Dynamic,
    Remote
}

public enum TunnelKind
{
    Tcp,
    Http,
    Https
}

/// <summary>
/// Where the SSH connection is opened: on the desktop with the user's agent
/// (Client) or on the API node using the server credential (Server).
/// </summary>
public enum TunnelTransport
{
    Client,
    Server
}

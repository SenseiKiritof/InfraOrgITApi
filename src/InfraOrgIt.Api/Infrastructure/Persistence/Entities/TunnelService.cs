namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

// Server-level tunnel preset (XPipe-style): independent from deployments, so
// the desktop can tunnel any listening service defined on the server.
public sealed class TunnelService : EntityBase
{
    public int ServerId { get; set; }
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "Local";
    public string TargetHost { get; set; } = "127.0.0.1";
    public int? TargetPort { get; set; }
    public int? LocalPort { get; set; }
    public int? RemotePort { get; set; }
    public string Kind { get; set; } = "Tcp";
    public string? OpenPath { get; set; }
    public bool AutoStart { get; set; }
    public bool Enabled { get; set; }

    public Server? Server { get; set; }
}

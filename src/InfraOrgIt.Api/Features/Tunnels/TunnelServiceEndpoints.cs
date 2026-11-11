using System.Linq.Expressions;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Tunnels;

/// <summary>
/// Server-level tunnel presets (XPipe-style) that complement the deployment
/// targets. Reads are open to authenticated users; writes need ServerAdmin
/// on the server's environment.
/// </summary>
public static class TunnelServiceEndpoints
{
    private static readonly TimeSpan DiscoverTimeout = TimeSpan.FromSeconds(5);

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/servers/{serverId:int}/tunnel-services")
            .RequireAuthorization();

        group.MapGet("", ListAsync).Produces<List<TunnelServiceDto>>();
        group.MapPost("", CreateAsync).Produces<TunnelServiceDto>();
        group.MapPut("/{id:int}", UpdateAsync)
            .Produces(StatusCodes.Status204NoContent);
        group.MapDelete("/{id:int}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent);
        group.MapPost("/discover", DiscoverAsync)
            .Produces<TunnelDiscoveryDto>()
            .RequireRateLimiting("reachability");
    }

    private static async Task<IResult> ListAsync(
        int serverId,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        await EnsureServerAsync(db, serverId, ct);

        var items = await db.TunnelServices.AsNoTracking()
            .Where(x => x.ServerId == serverId)
            .OrderBy(x => x.Name)
            .Select(ToDto)
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        int serverId,
        TunnelServiceInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking()
                         .SingleOrDefaultAsync(x => x.Id == serverId, ct)
                     ?? throw Guard.NotFound("Server", serverId);

        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);

        var details = Validate(input);
        await EnsureNameAvailableAsync(db, serverId, details.Name, null, ct);

        var entity = new TunnelService { ServerId = serverId };
        Apply(entity, details);
        db.TunnelServices.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await LoadAsync(db, entity.Id, ct));
    }

    private static async Task<IResult> UpdateAsync(
        int serverId,
        int id,
        TunnelServiceInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.TunnelServices
                         .SingleOrDefaultAsync(x => x.Id == id && x.ServerId == serverId, ct)
                     ?? throw Guard.NotFound("Servizio tunnel", id);

        var server = await db.Servers.AsNoTracking()
                         .SingleOrDefaultAsync(x => x.Id == serverId, ct)
                     ?? throw Guard.NotFound("Server", serverId);

        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);

        var details = Validate(input);
        await EnsureNameAvailableAsync(db, serverId, details.Name, id, ct);

        Apply(entity, details);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        int serverId,
        int id,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var entity = await db.TunnelServices
                         .SingleOrDefaultAsync(x => x.Id == id && x.ServerId == serverId, ct)
                     ?? throw Guard.NotFound("Servizio tunnel", id);

        var server = await db.Servers.AsNoTracking()
                         .SingleOrDefaultAsync(x => x.Id == serverId, ct)
                     ?? throw Guard.NotFound("Server", serverId);

        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);

        db.TunnelServices.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Lists listening TCP ports using the server's own SSH connection: the
    /// desktop proposes them when a service is created. Never contacts an
    /// arbitrary host.
    /// </summary>
    private static async Task<IResult> DiscoverAsync(
        int serverId,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        ISshConnector ssh,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking()
                         .Include(x => x.Credential)
                         .SingleOrDefaultAsync(x => x.Id == serverId, ct)
                     ?? throw Guard.NotFound("Server", serverId);

        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);

        SshCommandResult result;
        try
        {
            await using var session = await ssh.ConnectAsync(server, ct);
            result = await session.RunAsync("ss -tlnH", DiscoverTimeout, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Guard.Unprocessable(
                $"Scoperta delle porte fallita: {CleanOutput(exception.Message)}");
        }

        if (result.TimedOut)
            throw Guard.Unprocessable(
                "Scoperta delle porte fallita: ss non ha risposto entro il timeout.");
        if (result.ExitCode is not 0)
            throw Guard.Unprocessable(
                $"Scoperta delle porte fallita: {CleanOutput(result.Output)}");

        var items = ParseListeningPorts(result.Output);
        if (items.Count == 0 && !string.IsNullOrWhiteSpace(result.Output))
            throw Guard.Unprocessable(
                $"Scoperta delle porte fallita: output di ss non riconosciuto ({CleanOutput(result.Output)}).");

        return Results.Ok(new TunnelDiscoveryDto(items));
    }

    internal static IReadOnlyList<TunnelDiscoveryItemDto> ParseListeningPorts(
        string output)
    {
        var byPort = new Dictionary<int, TunnelDiscoveryItemDto>();
        foreach (var line in output.Split('\n'))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4)
                continue;

            // ss -tlnH: State Recv-Q Send-Q Local-Address:Port Peer-Address:Port
            var local = fields[3];
            var separator = local.LastIndexOf(':');
            if (separator <= 0 ||
                !int.TryParse(local[(separator + 1)..], out var port) ||
                port is < 1 or > 65_535)
                continue;

            byPort.TryAdd(port, new TunnelDiscoveryItemDto(
                port, local[..separator]));
        }

        return byPort.Values
            .OrderBy(x => x.Port)
            .Take(200)
            .ToList();
    }

    private static string CleanOutput(string value)
    {
        var text = value.Trim();
        if (text.Length == 0)
            return "nessun dettaglio.";
        return text.Length <= 300 ? text : text[..300];
    }

    private static async Task EnsureServerAsync(
        InfraOrgItDbContext db,
        int serverId,
        CancellationToken ct)
    {
        if (!await db.Servers.AnyAsync(x => x.Id == serverId, ct))
            throw Guard.NotFound("Server", serverId);
    }

    private static async Task EnsureNameAvailableAsync(
        InfraOrgItDbContext db,
        int serverId,
        string name,
        int? selfId,
        CancellationToken ct)
    {
        if (await db.TunnelServices.AnyAsync(
                x => x.ServerId == serverId && x.Name == name && x.Id != selfId, ct))
            throw Guard.Conflict($"Esiste già un servizio tunnel chiamato {name} su questo server.");
    }

    private static async Task<TunnelServiceDto> LoadAsync(
        InfraOrgItDbContext db,
        int id,
        CancellationToken ct) =>
        await db.TunnelServices.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDto)
            .SingleAsync(ct);

    private static readonly Expression<Func<TunnelService, TunnelServiceDto>> ToDto =
        x => new TunnelServiceDto(
        x.Id,
        x.ServerId,
        x.Name,
        x.Mode,
        x.TargetHost,
        x.TargetPort,
        x.LocalPort,
        x.RemotePort,
        x.Kind,
        x.OpenPath,
        x.AutoStart,
        x.Enabled);

    private static void Apply(TunnelService entity, ValidatedTunnelService details)
    {
        entity.Name = details.Name;
        entity.Mode = details.Mode;
        entity.TargetHost = details.TargetHost;
        entity.TargetPort = details.TargetPort;
        entity.LocalPort = details.LocalPort;
        entity.RemotePort = details.RemotePort;
        entity.Kind = details.Kind;
        entity.OpenPath = details.OpenPath;
        entity.AutoStart = details.AutoStart;
        entity.Enabled = details.Enabled;
    }

    private static ValidatedTunnelService Validate(TunnelServiceInput input)
    {
        var name = Guard.Text(input.Name, "Name", 100);
        var mode = Guard.Enum<TunnelMode>(input.Mode, "Mode");
        var kind = string.IsNullOrWhiteSpace(input.Kind)
            ? nameof(TunnelKind.Tcp)
            : Guard.Enum<TunnelKind>(input.Kind, "Kind");

        string targetHost;
        int? targetPort;
        int? localPort;
        int? remotePort;

        switch (mode)
        {
            case nameof(TunnelMode.Local):
                targetHost = Guard.Text(input.TargetHost, "TargetHost", 255);
                targetPort = RequirePort(input.TargetPort, "TargetPort");
                localPort = OptionalLocalPort(input.LocalPort, "LocalPort");
                if (input.RemotePort is not null)
                    throw Guard.Unprocessable(
                        "Il campo RemotePort non è ammesso per il modo Local.");
                remotePort = null;
                break;

            case nameof(TunnelMode.Dynamic):
                if (!string.IsNullOrWhiteSpace(input.TargetHost))
                    throw Guard.Unprocessable(
                        "Il campo TargetHost non è ammesso per il modo Dynamic.");
                if (input.TargetPort is not null)
                    throw Guard.Unprocessable(
                        "Il campo TargetPort non è ammesso per il modo Dynamic.");
                if (input.RemotePort is not null)
                    throw Guard.Unprocessable(
                        "Il campo RemotePort non è ammesso per il modo Dynamic.");
                targetHost = "127.0.0.1";
                targetPort = null;
                localPort = OptionalLocalPort(input.LocalPort, "LocalPort");
                remotePort = null;
                break;

            case nameof(TunnelMode.Remote):
                targetHost = Guard.Text(input.TargetHost, "TargetHost", 255);
                targetPort = RequirePort(input.TargetPort, "TargetPort");
                remotePort = RequirePort(input.RemotePort, "RemotePort");
                if (input.LocalPort is not null)
                    throw Guard.Unprocessable(
                        "Il campo LocalPort non è ammesso per il modo Remote.");
                localPort = null;
                break;

            default:
                throw Guard.Unprocessable("Mode ammesso: Local, Dynamic, Remote.");
        }

        if (mode != nameof(TunnelMode.Local) && kind != nameof(TunnelKind.Tcp))
            throw Guard.Unprocessable(
                "Kind Http o Https è ammesso solo per il modo Local.");

        var openPath = Guard.OptionalText(input.OpenPath, "OpenPath", 200);
        if (openPath is not null)
        {
            if (!openPath.StartsWith('/'))
                throw Guard.Unprocessable(
                    "Il campo OpenPath deve iniziare con '/'.");
            if (kind == nameof(TunnelKind.Tcp))
                throw Guard.Unprocessable(
                    "Il campo OpenPath è ammesso solo con Kind Http o Https.");
        }

        return new ValidatedTunnelService(
            name,
            mode,
            targetHost,
            targetPort,
            localPort,
            remotePort,
            kind,
            openPath,
            input.AutoStart,
            input.Enabled);
    }

    private static int RequirePort(int? value, string field) =>
        Guard.Port(
            value ?? throw Guard.Unprocessable($"Il campo {field} è obbligatorio."),
            field);

    private static int? OptionalLocalPort(int? value, string field)
    {
        if (value is null)
            return null;
        if (value is < 1024 or > 65_535)
            throw Guard.Unprocessable(
                $"Il campo {field} deve essere tra 1024 e 65535.");
        return value;
    }

    private sealed record ValidatedTunnelService(
        string Name,
        string Mode,
        string TargetHost,
        int? TargetPort,
        int? LocalPort,
        int? RemotePort,
        string Kind,
        string? OpenPath,
        bool AutoStart,
        bool Enabled);
}

public sealed record TunnelServiceInput(
    string? Name,
    string? Mode,
    string? TargetHost = null,
    int? TargetPort = null,
    int? LocalPort = null,
    int? RemotePort = null,
    string? Kind = null,
    string? OpenPath = null,
    bool AutoStart = false,
    bool Enabled = true);

public sealed record TunnelServiceDto(
    int Id,
    int ServerId,
    string Name,
    string Mode,
    string TargetHost,
    int? TargetPort,
    int? LocalPort,
    int? RemotePort,
    string Kind,
    string? OpenPath,
    bool AutoStart,
    bool Enabled);

public sealed record TunnelDiscoveryDto(
    IReadOnlyList<TunnelDiscoveryItemDto> Items);

public sealed record TunnelDiscoveryItemDto(
    int Port,
    string Address);

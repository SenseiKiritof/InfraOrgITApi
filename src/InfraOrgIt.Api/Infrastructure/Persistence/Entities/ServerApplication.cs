namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ServerApplication : EntityBase
{
    public int ServerId { get; set; }
    public int ApplicationVersionId { get; set; }
    public int ExposedPort { get; set; }

    // FK verso ProxyRoutes aggiunta in Fase 4, quando l'entità esiste.
    public int? ProxyRouteId { get; set; }

    public Server? Server { get; set; }
    public ApplicationVersion? ApplicationVersion { get; set; }
}

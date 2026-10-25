using System.Text.Json;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;

namespace InfraOrgIt.IntegrationTests.Legacy;

public sealed record LegacyFixture(
    List<LegacyEnvironment> Environments,
    List<LegacyProvider> Providers,
    List<LegacyProviderEnvironment> ProviderEnvironments,
    List<LegacyApplication> Applications,
    List<LegacyApplicationVersion> ApplicationVersions,
    List<LegacyApplicationVersionApi> ApplicationVersionApis,
    List<LegacyApplicationApiQueryParam> ApplicationApiQueryParams,
    List<LegacyProject> Projects,
    List<LegacyApplicationProject> ApplicationProjects)
{
    private static readonly string[] RequestTypes = ["GET", "POST", "PUT", "PATCH", "DELETE"];
    private static readonly string[] ParamTypes =
        ["Int", "String", "Bool", "DateTime", "Double", "Decimal"];

    public static LegacyFixture Load(string path) =>
        JsonSerializer.Deserialize<LegacyFixture>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("Fixture legacy vuota.");

    public void Validate()
    {
        RequireUniqueIds(Environments.Select(x => x.Id), "Environments");
        RequireUniqueIds(Providers.Select(x => x.Id), "Providers");
        RequireUniqueIds(ProviderEnvironments.Select(x => x.Id), "ProviderEnvironments");
        RequireUniqueIds(Applications.Select(x => x.Id), "Applications");
        RequireUniqueIds(ApplicationVersions.Select(x => x.Id), "ApplicationVersions");
        RequireUniqueIds(ApplicationVersionApis.Select(x => x.Id), "ApplicationVersionApis");
        RequireUniqueIds(ApplicationApiQueryParams.Select(x => x.Id), "ApplicationApiQueryParams");
        RequireUniqueIds(Projects.Select(x => x.Id), "Projects");

        RequireUnique(Environments.Select(x => x.Name), "Environments.Name");
        RequireUnique(Providers.Select(x => x.Name), "Providers.Name");
        RequireUnique(Applications.Select(x => x.Name), "Applications.Name");
        RequireUnique(Projects.Select(x => x.Name), "Projects.Name");
        RequireUnique(
            ApplicationVersions.Select(x => (x.ApplicationId, x.VersionNumber)),
            "ApplicationVersions (ApplicationId, VersionNumber)");
        RequireUnique(
            ApplicationProjects.Select(x => (x.ApplicationId, x.ProjectId)),
            "ApplicationProjects (ApplicationId, ProjectId)");

        foreach (var item in Environments)
            RequireText(item.Name, 100, "Environments.Name");
        foreach (var item in Providers)
            RequireText(item.Name, 200, "Providers.Name");
        foreach (var item in Applications)
        {
            RequireText(item.Name, 200, "Applications.Name");
            RequireText(item.BasePath, 50, "Applications.BasePath");
            RequirePort(item.ListeningOnPort, "Applications.ListeningOnPort");
        }

        foreach (var item in ProviderEnvironments)
        {
            Ensure(Providers.Any(x => x.Id == item.ProviderId), "ProviderEnvironment senza provider");
            Ensure(Environments.Any(x => x.Id == item.EnvironmentId), "ProviderEnvironment senza ambiente");
        }

        foreach (var item in ApplicationVersions)
            Ensure(Applications.Any(x => x.Id == item.ApplicationId), "Versione senza applicazione");

        foreach (var item in ApplicationVersionApis)
        {
            Ensure(ApplicationVersions.Any(x => x.Id == item.ApplicationVersionId),
                "API senza versione");
            Ensure(item.RequestType >= 0 && item.RequestType < RequestTypes.Length,
                "RequestType fuori intervallo");
            RequireText(item.Url, 200, "ApplicationVersionApis.Url");
        }

        foreach (var item in ApplicationApiQueryParams)
        {
            Ensure(ApplicationVersionApis.Any(x => x.Id == item.ApplicationApiId),
                "Query param senza API");
            Ensure(item.ParamType >= 0 && item.ParamType < ParamTypes.Length,
                "ParamType fuori intervallo");
            RequireText(item.Name, 200, "ApplicationApiQueryParams.Name");
        }

        foreach (var item in ApplicationProjects)
        {
            Ensure(Applications.Any(x => x.Id == item.ApplicationId),
                "Link progetto senza applicazione");
            Ensure(Projects.Any(x => x.Id == item.ProjectId), "Link progetto senza progetto");
        }
    }

    public MappedFixture Map() => new(
        Environments.Select(x => new EnvironmentDefinition
        {
            Id = x.Id,
            Name = x.Name,
            Description = x.Description
        }).ToList(),
        Providers.Select(x => new Provider { Id = x.Id, Name = x.Name }).ToList(),
        ProviderEnvironments.Select(x => new ProviderEnvironment
        {
            Id = x.Id,
            ProviderId = x.ProviderId,
            EnvironmentDefinitionId = x.EnvironmentId
        }).ToList(),
        Applications.Select(x => new Application
        {
            Id = x.Id,
            Name = x.Name,
            Description = x.Description,
            BasePath = x.BasePath,
            ListeningOnPort = x.ListeningOnPort
        }).ToList(),
        ApplicationVersions.Select(x => new ApplicationVersion
        {
            Id = x.Id,
            ApplicationId = x.ApplicationId,
            VersionNumber = x.VersionNumber
        }).ToList(),
        ApplicationVersionApis.Select(x => new ApplicationVersionApi
        {
            Id = x.Id,
            ApplicationVersionId = x.ApplicationVersionId,
            Url = x.Url,
            RequestTypeEnum = RequestTypes[x.RequestType],
            Description = x.Description,
            Body = x.Body
        }).ToList(),
        ApplicationApiQueryParams.Select(x => new ApplicationApiQueryParam
        {
            Id = x.Id,
            ApplicationApiId = x.ApplicationApiId,
            Name = x.Name,
            ParamType = ParamTypes[x.ParamType]
        }).ToList(),
        Projects.Select(x => new Project
        {
            Id = x.Id,
            Name = x.Name,
            Description = x.Description
        }).ToList(),
        ApplicationProjects.Select(x => new ApplicationProject
        {
            ApplicationsId = x.ApplicationId,
            ProjectsId = x.ProjectId
        }).ToList());

    private static void RequireUniqueIds(IEnumerable<int> ids, string table)
    {
        var duplicates = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Ensure(duplicates.Length == 0, $"{table}: ID duplicati {string.Join(", ", duplicates)}");
        Ensure(ids.All(x => x > 0), $"{table}: ID non positivo");
    }

    private static void RequireUnique<T>(IEnumerable<T> values, string field)
    {
        var duplicates = values.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Ensure(duplicates.Length == 0, $"{field}: duplicati {string.Join(", ", duplicates)}");
    }

    private static void RequireText(string? value, int maxLength, string field)
    {
        Ensure(!string.IsNullOrWhiteSpace(value), $"{field}: valore obbligatorio");
        Ensure(value!.Length <= maxLength, $"{field}: supera {maxLength} caratteri");
    }

    private static void RequirePort(int value, string field) =>
        Ensure(value is >= 1 and <= 65_535, $"{field}: porta non valida");

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

public sealed record LegacyEnvironment(int Id, string Name, string Description);

public sealed record LegacyProvider(int Id, string Name);

public sealed record LegacyProviderEnvironment(int Id, int ProviderId, int EnvironmentId);

public sealed record LegacyApplication(
    int Id,
    string Name,
    string Description,
    string BasePath,
    int ListeningOnPort);

public sealed record LegacyApplicationVersion(int Id, int ApplicationId, string VersionNumber);

public sealed record LegacyApplicationVersionApi(
    int Id,
    int ApplicationVersionId,
    string Url,
    int RequestType,
    string Description,
    string? Body);

public sealed record LegacyApplicationApiQueryParam(
    int Id,
    int ApplicationApiId,
    string Name,
    int ParamType);

public sealed record LegacyProject(int Id, string Name, string? Description);

public sealed record LegacyApplicationProject(int ApplicationId, int ProjectId);

public sealed record MappedFixture(
    List<EnvironmentDefinition> Environments,
    List<Provider> Providers,
    List<ProviderEnvironment> ProviderEnvironments,
    List<Application> Applications,
    List<ApplicationVersion> ApplicationVersions,
    List<ApplicationVersionApi> ApplicationVersionApis,
    List<ApplicationApiQueryParam> ApplicationApiQueryParams,
    List<Project> Projects,
    List<ApplicationProject> ApplicationProjects);

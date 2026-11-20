namespace InfraOrgIt.Api.Features.Remote;

/// <summary>
/// Recognises the database engine from the configurable application type
/// name. The type stays editable in the DB; only the name is mapped here.
/// </summary>
public static class DatabaseEngines
{
    public static readonly string[] Names =
        ["MySql", "PostgreSql", "MongoDb", "Redis", "SqlServer", "Oracle"];

    public static string? Resolve(string? type)
    {
        var key = new string((type ?? "")
            .Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return key switch
        {
            "mysql" or "mariadb" => "MySql",
            "postgres" or "postgresql" => "PostgreSql",
            "mongodb" or "mongo" => "MongoDb",
            "redis" => "Redis",
            "sqlserver" or "mssql" or "microsoftsqlserver" => "SqlServer",
            "oracle" => "Oracle",
            _ => null
        };
    }
}

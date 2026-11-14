using MySql.Data.MySqlClient;
using Testcontainers.MySql;

namespace InfraOrgIt.IntegrationTests;

/// <summary>
/// One MySQL container shared by the integration suite. Each test class gets
/// its own database so xUnit can run classes in parallel without cross-class
/// state. The container is removed by Testcontainers' Ryuk sidecar when the
/// test process exits.
/// </summary>
public static class TestMySql
{
    private static readonly Lazy<Task<MySqlContainer>> Server = new(async () =>
    {
        var container = new MySqlBuilder("mysql:8.4")
            .WithDatabase("infraorgit")
            .WithUsername("test")
            .WithPassword("test")
            .WithEnvironment("MYSQL_ROOT_PASSWORD", "test")
            .Build();
        await container.StartAsync();
        return container;
    });

    public static async Task<string> CreateDatabaseAsync(string name)
    {
        var container = await Server.Value;
        var database = $"it_{name}_{Guid.NewGuid():N}";

        var builder = new MySqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = "",
            UserID = "root",
            Password = "test"
        };
        await using (var connection = new MySqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci";
            await command.ExecuteNonQueryAsync();
        }

        builder.Database = database;
        return builder.ConnectionString;
    }
}

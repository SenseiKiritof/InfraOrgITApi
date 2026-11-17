using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;

namespace InfraOrgIt.IntegrationTests;

/// <summary>
/// One MySQL container shared by the integration suite. The schema is migrated
/// once into a template database; every test class gets a clone of it, so the
/// classes can run in parallel without paying 29 migrations each. The container
/// is removed by Testcontainers' Ryuk sidecar when the test process exits.
/// </summary>
public static class TestMySql
{
    private const string TemplateDatabase = "it_template";

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

    private static readonly Lazy<Task> Template = new(async () =>
    {
        var container = await Server.Value;

        await using (var connection = new MySqlConnection(ConnectionString(container, "")))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS `{TemplateDatabase}`");
            await ExecuteAsync(connection, CreateDatabaseSql(TemplateDatabase));
        }

        var options = new DbContextOptionsBuilder<InfraOrgItDbContext>()
            .UseMySQL(ConnectionString(container, TemplateDatabase))
            .Options;
        await using var db = new InfraOrgItDbContext(options);
        await db.Database.MigrateAsync();
    });

    /// <summary>Creates a migrated clone of the template database.</summary>
    public static async Task<string> CreateDatabaseAsync(string name)
    {
        var container = await Server.Value;
        await Template.Value;

        var database = await CreateDatabaseAsync(container, name);
        await CloneTemplateAsync(container, database);
        return ConnectionString(container, database);
    }

    /// <summary>Creates an empty database; used by the migration test.</summary>
    public static async Task<string> CreateEmptyDatabaseAsync(string name)
    {
        var container = await Server.Value;
        var database = await CreateDatabaseAsync(container, name);
        return ConnectionString(container, database);
    }

    private static async Task<string> CreateDatabaseAsync(
        MySqlContainer container, string name)
    {
        var database = $"it_{name}_{Guid.NewGuid():N}";
        await using var connection = new MySqlConnection(ConnectionString(container, ""));
        await connection.OpenAsync();
        await ExecuteAsync(connection, CreateDatabaseSql(database));
        return database;
    }

    private static async Task CloneTemplateAsync(
        MySqlContainer container, string database)
    {
        await using var admin = new MySqlConnection(ConnectionString(container, ""));
        await admin.OpenAsync();

        var tables = new List<string>();
        await using (var command = admin.CreateCommand())
        {
            command.CommandText = """
                SELECT TABLE_NAME
                FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = @schema AND TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_NAME
                """;
            command.Parameters.AddWithValue("@schema", TemplateDatabase);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        // Foreign keys reference tables that may not exist yet, and inserts can
        // arrive before their parents: both are valid with checks disabled.
        await using var target = new MySqlConnection(
            ConnectionString(container, database));
        await target.OpenAsync();
        await ExecuteAsync(target, "SET FOREIGN_KEY_CHECKS = 0");

        foreach (var table in tables)
            await ExecuteAsync(target, await ShowCreateTableAsync(admin, table));
        foreach (var table in tables)
            await ExecuteAsync(
                target,
                $"INSERT INTO `{table}` SELECT * FROM `{TemplateDatabase}`.`{table}`");

        await ExecuteAsync(target, "SET FOREIGN_KEY_CHECKS = 1");
    }

    private static async Task<string> ShowCreateTableAsync(
        MySqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SHOW CREATE TABLE `{TemplateDatabase}`.`{table}`";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return reader.GetString(1);
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateDatabaseSql(string database) =>
        $"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci";

    private static string ConnectionString(MySqlContainer container, string database)
    {
        var builder = new MySqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = database,
            UserID = "root",
            Password = "test"
        };
        return builder.ConnectionString;
    }
}

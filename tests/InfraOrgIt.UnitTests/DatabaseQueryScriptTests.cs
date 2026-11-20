using System.Text;
using System.Text.RegularExpressions;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.Features.Remote;

namespace InfraOrgIt.UnitTests;

public sealed partial class DatabaseQueryScriptTests
{
    [Fact]
    public void Resolves_the_engine_from_configurable_type_names()
    {
        Assert.Equal("MySql", DatabaseEngines.Resolve("MySql"));
        Assert.Equal("MySql", DatabaseEngines.Resolve("MariaDB"));
        Assert.Equal("PostgreSql", DatabaseEngines.Resolve("postgresql"));
        Assert.Equal("MongoDb", DatabaseEngines.Resolve("Mongo DB"));
        Assert.Equal("Redis", DatabaseEngines.Resolve("Redis"));
        Assert.Equal("SqlServer", DatabaseEngines.Resolve("Microsoft SQL Server"));
        Assert.Equal("Oracle", DatabaseEngines.Resolve("Oracle"));
        Assert.Null(DatabaseEngines.Resolve("HAProxy"));
    }

    [Fact]
    public void MySql_runs_inside_a_read_only_transaction()
    {
        var script = DatabaseQueryScript.Build("MySql", "app", "SELECT * FROM users");
        Assert.Contains("mysql --batch --raw", script);
        Assert.Contains("--database=app", script);
        var payload = Payload(script);
        Assert.StartsWith("START TRANSACTION READ ONLY;", payload);
        Assert.Contains("SELECT * FROM users", payload);
        Assert.EndsWith("ROLLBACK;\n", payload);
    }

    [Fact]
    public void PostgreSql_and_sql_server_and_oracle_use_their_clients()
    {
        var postgres = Payload(DatabaseQueryScript.Build("PostgreSql", "app", "SELECT 1"));
        Assert.StartsWith("BEGIN READ ONLY;", postgres);
        Assert.Contains("ROLLBACK;", postgres);

        var sqlServer = Payload(DatabaseQueryScript.Build("SqlServer", null, "SELECT 1"));
        Assert.StartsWith("SET NOCOUNT ON;\nBEGIN TRANSACTION;", sqlServer);

        var oracle = Payload(DatabaseQueryScript.Build("Oracle", null, "SELECT 1 FROM dual"));
        Assert.StartsWith("SET TRANSACTION READ ONLY;", oracle);
        Assert.EndsWith("EXIT;\n", oracle);
    }

    [Theory]
    [InlineData("MySql", "DELETE FROM users")]
    [InlineData("MySql", "SELECT 1; COMMIT; DELETE FROM users")]
    [InlineData("MySql", "UPDATE users SET name = 'x'")]
    [InlineData("MySql", "SELECT * FROM users INTO OUTFILE '/tmp/x'")]
    [InlineData("PostgreSql", "COPY users TO '/tmp/x'")]
    [InlineData("PostgreSql", "SELECT * INTO backup FROM users")]
    [InlineData("SqlServer", "EXEC xp_cmdshell 'dir'")]
    [InlineData("SqlServer", "SELECT * INTO backup FROM users")]
    [InlineData("Oracle", "BEGIN DELETE FROM users; END;")]
    public void Rejects_writes_and_transaction_escape(string engine, string query)
    {
        Assert.Throws<ApiException>(
            () => DatabaseQueryScript.Build(engine, null, query));
    }

    [Fact]
    public void Keyword_inside_a_string_literal_is_allowed()
    {
        var script = DatabaseQueryScript.Build(
            "MySql", null, "SELECT 'delete from users' AS note");
        Assert.Contains("delete from users", Payload(script));
    }

    [Fact]
    public void Builds_a_mongo_find_and_blocks_write_stages()
    {
        var script = DatabaseQueryScript.Build(
            "MongoDb",
            "app",
            """{"collection":"users","filter":{"active":true},"limit":500}""");
        Assert.Contains("mongosh --quiet --norc app", script);
        var payload = Payload(script);
        Assert.Contains("db.getCollection(\"users\")", payload);
        Assert.Contains(".limit(200)", payload);

        Assert.Throws<ApiException>(() => DatabaseQueryScript.Build(
            "MongoDb",
            "app",
            """{"collection":"users","pipeline":[{"$out":"copy"}]}"""));
        Assert.Throws<ApiException>(() => DatabaseQueryScript.Build(
            "MongoDb",
            "app",
            """{"filter":{}}"""));
    }

    [Fact]
    public void Redis_runs_one_read_command_and_blocks_writes()
    {
        var script = DatabaseQueryScript.Build("Redis", "2", "GET session:1");
        Assert.Contains("redis-cli -n 2", script);
        Assert.Equal("GET session:1\n", Payload(script));

        Assert.Throws<ApiException>(() =>
            DatabaseQueryScript.Build("Redis", null, "SET session:1 x"));
        Assert.Throws<ApiException>(() =>
            DatabaseQueryScript.Build("Redis", null, "GET a\nSET b c"));
        Assert.Throws<ApiException>(() =>
            DatabaseQueryScript.Build("Redis", "99", "GET a"));
    }

    [Fact]
    public void Rejects_invalid_database_names()
    {
        Assert.Throws<ApiException>(() =>
            DatabaseQueryScript.Build("MySql", "app; rm -rf /", "SELECT 1"));
        Assert.Throws<ApiException>(() =>
            DatabaseQueryScript.Build("MongoDb", "", "{\"collection\":\"x\"}"));
    }

    private static string Payload(string script)
    {
        var match = PayloadPattern().Match(script);
        Assert.True(match.Success, script);
        return Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups[1].Value));
    }

    [GeneratedRegex("printf %s '([^']+)'")]
    private static partial Regex PayloadPattern();
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using InfraOrgIt.Api.BuildingBlocks.Validation;

namespace InfraOrgIt.Api.Features.Remote;

/// <summary>
/// Builds a server-side client invocation for a read-only query. The SQL
/// engines run inside a read-only transaction, so the server rejects writes;
/// the block list only stops transaction escape and file/OS side effects.
/// The query text travels in base64 on stdin, never in the command line.
/// </summary>
public static partial class DatabaseQueryScript
{
    public static string Build(string engine, string? database, string query)
    {
        Validate(engine, query);
        var payload = engine switch
        {
            "MySql" => $"START TRANSACTION READ ONLY;\n{Trim(query)};\nROLLBACK;\n",
            "PostgreSql" => $"BEGIN READ ONLY;\n{Trim(query)};\nROLLBACK;\n",
            "SqlServer" => $"SET NOCOUNT ON;\nBEGIN TRANSACTION;\n{Trim(query)};\nROLLBACK;\n",
            "Oracle" => $"SET TRANSACTION READ ONLY;\n{Trim(query)};\nROLLBACK;\nEXIT;\n",
            "MongoDb" => MongoScript(query),
            "Redis" => $"{query.Trim()}\n",
            _ => throw Guard.Unprocessable("Motore database non supportato.")
        };
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var client = engine switch
        {
            "MySql" => $"mysql --batch --raw --default-character-set=utf8mb4{DatabaseArgument(database, "--database=")}",
            "PostgreSql" => $"psql -X -A -F \"$(printf '\\t')\" -P footer=off -v ON_ERROR_STOP=1{DatabaseArgument(database, "-d ")}",
            "SqlServer" => $"sqlcmd -b -h -1 -W -s \"$(printf '\\t')\"{DatabaseArgument(database, "-d ")}",
            "Oracle" => $"sqlplus -S -L {(string.IsNullOrWhiteSpace(database) ? "/" : $"/@{ServiceName(database)}")}",
            "MongoDb" => $"mongosh --quiet --norc {MongoDatabase(database)}",
            "Redis" => $"redis-cli{RedisDatabase(database)}",
            _ => throw Guard.Unprocessable("Motore database non supportato.")
        };
        if (engine == "MongoDb")
            return $"f=$(mktemp) && printf %s '{encoded}' | base64 -d > \"$f\" && "
                   + $"{client} --file \"$f\"; r=$?; rm -f \"$f\"; exit $r";
        return $"printf %s '{encoded}' | base64 -d | {client}";
    }

    public static void Validate(string engine, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw Guard.Unprocessable("La query è obbligatoria.");
        if (query.Length > 20_000)
            throw Guard.Unprocessable("La query supera 20.000 caratteri.");
        if (engine == "MongoDb")
        {
            ValidateMongo(query);
            return;
        }
        if (engine == "Redis")
        {
            ValidateRedis(query);
            return;
        }
        var code = StripLiteralsAndComments(query);
        if (WritePattern().IsMatch(code))
            throw Guard.Unprocessable(
                "La query contiene una scrittura o un comando non read-only.");
        if (engine == "MySql")
        {
            if (MySqlFilePattern().IsMatch(code))
                throw Guard.Unprocessable(
                    "INTO OUTFILE/DUMPFILE non è consentito.");
        }
        else if (IntoPattern().IsMatch(code))
        {
            throw Guard.Unprocessable(
                "SELECT ... INTO non è consentito in sola lettura.");
        }
    }

    private static string Trim(string query) => query.Trim().TrimEnd(';');

    private static string DatabaseArgument(string? database, string prefix) =>
        string.IsNullOrWhiteSpace(database) ? "" : $" {prefix}{SqlName(database)}";

    private static string SqlName(string value)
    {
        if (!SimpleName().IsMatch(value))
            throw Guard.Unprocessable("Nome database non valido.");
        return value;
    }

    private static string MongoDatabase(string? database)
    {
        if (string.IsNullOrWhiteSpace(database))
            throw Guard.Unprocessable("Indica il database MongoDB.");
        if (!SimpleName().IsMatch(database))
            throw Guard.Unprocessable("Nome database non valido.");
        return database;
    }

    private static string ServiceName(string value)
    {
        if (!ServicePattern().IsMatch(value))
            throw Guard.Unprocessable("Nome servizio Oracle non valido.");
        return value;
    }

    private static string RedisDatabase(string? database)
    {
        if (string.IsNullOrWhiteSpace(database)) return "";
        if (!int.TryParse(database, out var index) || index < 0 || index > 15)
            throw Guard.Unprocessable("Indice Redis non valido (0-15).");
        return $" -n {index}";
    }

    private static string MongoScript(string query)
    {
        JsonObject document;
        try
        {
            document = JsonNode.Parse(query)?.AsObject()
                       ?? throw new JsonException("oggetto atteso");
        }
        catch (JsonException)
        {
            throw Guard.Unprocessable(
                "La query MongoDB deve essere un oggetto JSON con collection e filter/pipeline.");
        }
        if (!SimpleName().IsMatch(document["collection"]?.GetValue<string>() ?? ""))
            throw Guard.Unprocessable("Indica la collection MongoDB.");
        var collection = JsonSerializer.Serialize(
            document["collection"]!.GetValue<string>());
        var limit = Math.Clamp(document["limit"]?.GetValue<int>() ?? 50, 1, 200);
        var filter = document["filter"]?.ToJsonString() ?? "{}";
        if (document["count"]?.GetValue<bool>() == true)
            return $"print(db.getCollection({collection}).countDocuments({filter}));";
        var pipeline = document["pipeline"]?.ToJsonString();
        if (pipeline is not null)
        {
            if (ForbiddenMongo().IsMatch(pipeline))
                throw Guard.Unprocessable(
                    "Il pipeline contiene uno stage di scrittura.");
            return $"const r=db.getCollection({collection}).aggregate({pipeline})"
                   + $".limit({limit}).toArray();print(EJSON.stringify(r));";
        }
        if (ForbiddenMongo().IsMatch(filter))
            throw Guard.Unprocessable(
                "Il filtro contiene un operatore non consentito.");
        var projection = document["projection"]?.ToJsonString();
        var sort = document["sort"]?.ToJsonString();
        var projectionArgument = projection is null ? "" : $",{projection}";
        var sortArgument = sort is null ? "" : $".sort({sort})";
        return $"const r=db.getCollection({collection})"
               + $".find({filter}{projectionArgument}){sortArgument}"
               + $".limit({limit}).toArray();print(EJSON.stringify(r));";
    }

    private static void ValidateMongo(string query)
    {
        if (!query.TrimStart().StartsWith('{'))
            throw Guard.Unprocessable(
                "La query MongoDB deve essere un oggetto JSON.");
        try
        {
            JsonNode.Parse(query);
        }
        catch (JsonException)
        {
            throw Guard.Unprocessable("La query MongoDB non è JSON valido.");
        }
    }

    private static void ValidateRedis(string query)
    {
        if (query.Contains('\n') || query.Contains('\r'))
            throw Guard.Unprocessable("Esegui un solo comando Redis per volta.");
        var first = query.Trim().Split(' ', 2)[0].ToLowerInvariant();
        if (RedisWrites.Contains(first))
            throw Guard.Unprocessable(
                $"Il comando Redis {first.ToUpperInvariant()} non è read-only.");
    }

    /// <summary>Remove string literals and comments before keyword checks.</summary>
    private static string StripLiteralsAndComments(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index += 1)
        {
            var current = value[index];
            if (current is '\'' or '"' or '`')
            {
                var quote = current;
                builder.Append(' ');
                index += 1;
                while (index < value.Length)
                {
                    if (value[index] == quote)
                    {
                        if (index + 1 < value.Length && value[index + 1] == quote)
                        {
                            index += 2;
                            continue;
                        }
                        break;
                    }
                    index += value[index] == '\\' ? 2 : 1;
                }
                builder.Append(' ');
                continue;
            }
            if (current == '-' && index + 1 < value.Length && value[index + 1] == '-')
            {
                while (index < value.Length && value[index] != '\n') index += 1;
                builder.Append('\n');
                continue;
            }
            if (current == '/' && index + 1 < value.Length && value[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < value.Length &&
                       !(value[index] == '*' && value[index + 1] == '/'))
                    index += 1;
                index += 1;
                builder.Append(' ');
                continue;
            }
            builder.Append(current);
        }
        return builder.ToString();
    }

    private static readonly HashSet<string> RedisWrites = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "set", "setnx", "setex", "psetex", "getset", "getdel", "getex",
        "append", "incr", "incrby", "incrbyfloat", "decr", "decrby", "mset",
        "msetnx", "del", "unlink", "rename", "renamenx", "move", "copy",
        "restore", "dump", "expire", "pexpire", "expireat", "pexpireat",
        "persist", "flushall", "flushdb", "swapdb", "select", "auth", "hello",
        "client", "config", "shutdown", "slaveof", "replicaof", "failover",
        "cluster", "module", "script", "eval", "evalsha", "eval_ro", "fcall",
        "fcall_ro", "function", "multi", "exec", "discard", "watch", "unwatch",
        "publish", "spublish", "subscribe", "psubscribe", "xadd", "xdel",
        "xtrim", "xgroup", "xack", "xclaim", "xautoclaim", "lpush", "rpush",
        "lpop", "rpop", "lset", "linsert", "lrem", "ltrim", "rpoplpush",
        "lmove", "blpop", "brpop", "brpoplpush", "blmove", "sadd", "srem",
        "spop", "smove", "sinterstore", "sunionstore", "sdiffstore", "zadd",
        "zrem", "zremrangebyrank", "zremrangebyscore", "zremrangebylex",
        "zincrby", "zunionstore", "zinterstore", "zdiffstore", "zrangestore",
        "hset", "hsetnx", "hmset", "hdel", "hincrby", "hincrbyfloat",
        "setbit", "bitop", "bitfield", "geoadd", "pfadd", "pfmerge",
        "sort", "migrate", "debug", "latency", "monitor", "sync", "psync"
    };

    [GeneratedRegex(
        @"\b(insert|update|delete|replace|merge|upsert|create|alter|drop|truncate|rename|grant|revoke|call|execute|exec|do|copy|load|bulk|shutdown|kill|flush|analyze|optimize|repair|begin|start|commit|rollback|savepoint|release|set|use|lock|unlock|prepare|deallocate|declare|handler|install|uninstall|vacuum|attach|detach)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex WritePattern();

    [GeneratedRegex(@"\binto\s+(outfile|dumpfile)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MySqlFilePattern();

    [GeneratedRegex(@"\binto\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntoPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_$-]{1,64}$")]
    private static partial Regex SimpleName();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,128}$")]
    private static partial Regex ServicePattern();

    [GeneratedRegex(
        @"""\$(out|merge|function|accumulator|where|mapReduce)\""|""mapReduce""",
        RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenMongo();
}

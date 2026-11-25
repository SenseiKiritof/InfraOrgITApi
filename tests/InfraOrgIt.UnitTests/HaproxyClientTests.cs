using System.Net;
using System.Text;
using InfraOrgIt.Api.Features.Haproxy;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;

namespace InfraOrgIt.UnitTests;

public sealed class HaproxyClientTests
{
    [Fact]
    public async Task Refreshes_routes_and_server_snapshots()
    {
        var handler = new FakeHandler(new Dictionary<string, string>
        {
            ["/v3/info"] = """{"api":{"version":"v3.0"}}""",
            ["/v3/services/haproxy/configuration/version"] = "1",
            ["/v3/services/haproxy/configuration/frontends"] =
                """[{"name":"fe_https","default_backend":"be_default"}]""",
            ["/v3/services/haproxy/configuration/frontends/fe_https/binds"] =
                """[{"port":443}]""",
            ["/v3/services/haproxy/configuration/frontends/fe_https/acls"] =
                """[{"acl_name":"h_shop","criterion":"var(txn.host)","value":"-m str shop.example"}]""",
            ["/v3/services/haproxy/configuration/frontends/fe_https/backend_switching_rules"] =
                """[{"name":"be_shop","cond":"if","cond_test":"h_shop { path_beg /app/ }"}]""",
            ["/v3/services/haproxy/stats/native"] =
                """{"stats":[{"type":"server","name":"web01","backend_name":"be_shop","stats":{"addr":"10.0.0.1:8080","status":"UP","check_status":"200","weight":1}}]}"""
        });
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://haproxy.local/")
        };
        var instance = new HaproxyInstance
        {
            BaseUrl = "http://haproxy.local",
            ApiVersion = "v3"
        };
        await using var client = new HaproxyClient(http, instance);

        var result = await client.RefreshAsync(CancellationToken.None);

        var route = result.Routes.Single(x => x.Host == "shop.example");
        Assert.Equal("fe_https", route.Frontend);
        Assert.Equal("shop.example", route.Host);
        Assert.Equal("/app/", route.Path);
        Assert.Equal("be_shop", route.Backend);
        Assert.Equal("443", route.Bind);
        Assert.Contains(result.Routes,
            x => x.IsDefault && x.Backend == "be_default");
        var server = Assert.Single(result.Servers);
        Assert.Equal("be_shop", server.Backend);
        Assert.Equal("web01", server.Server);
        Assert.Equal("UP", server.Status);
        Assert.Equal(1, result.ConfigurationVersion);
    }

    [Fact]
    public async Task Reads_stats_and_process_metrics()
    {
        var handler = new FakeHandler(new Dictionary<string, string>
        {
            ["/v3/info"] = """{"api":{"version":"v3.0"}}""",
            ["/v3/services/haproxy/stats/native"] = """
                {"stats":[
                  {"type":"frontend","name":"fe_https","stats":{"req_tot":10,"hrsp_5xx":2,"scur":1,"qcur":2,"rtime":12}},
                  {"type":"server","backend_name":"be_shop","name":"web01","stats":{"req_tot":8,"status":"UP","check_status":"200","addr":"10.0.0.1:8080","bin":100,"bout":200}}
                ]}
                """,
            ["/v3/services/haproxy/runtime/info"] = """
                {"info":{"version":"2.8.30","uptime":100,"curr_conns":2,"cum_conns":20,"tasks":5,"nbthread":4}}
                """
        });
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://haproxy.local/")
        };
        var instance = new HaproxyInstance { ApiVersion = "v3" };
        await using var client = new HaproxyClient(http, instance);

        var stats = await client.StatsAsync(CancellationToken.None);
        var process = await client.ProcessAsync(CancellationToken.None);

        Assert.Equal(2, stats.Count);
        var frontend = Assert.Single(stats, x => x.Kind == "frontend");
        Assert.Equal(10, frontend.ReqTot);
        Assert.Equal(2, frontend.H5xx);
        var server = Assert.Single(stats, x => x.Kind == "server");
        Assert.Equal("be_shop", server.Proxy);
        Assert.Equal("web01", server.Server);
        Assert.Equal(100, server.Bin);
        Assert.Equal("2.8.30", process.Version);
        Assert.Equal(20, process.CumConns);
        Assert.Equal(4, process.Threads);
    }

    [Fact]
    public async Task Refreshes_certificate_metadata()
    {
        var handler = new FakeHandler(new Dictionary<string, string>
        {
            ["/v3/info"] = """{"api":{"version":"v3.0"}}""",
            ["/v3/services/haproxy/storage/ssl_certificates"] =
                """[{"storage_name":"site.pem","file":"/etc/haproxy/ssl/site.pem"}]""",
            ["/v3/services/haproxy/storage/ssl_certificates/site.pem"] =
                """{"storage_name":"site.pem","file":"/etc/haproxy/ssl/site.pem","domains":"example.com","issuers":"Example CA","not_after":"2027-01-01T00:00:00Z","size":2048}"""
        });
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://haproxy.local/")
        };
        var instance = new HaproxyInstance { ApiVersion = "v3" };
        await using var client = new HaproxyClient(http, instance);

        var result = await client.RefreshAsync(CancellationToken.None);

        var certificate = Assert.Single(result.Certificates);
        Assert.Equal("site.pem", certificate.StorageName);
        Assert.Equal("example.com", certificate.Domains);
        Assert.Equal("Example CA", certificate.Issuer);
        Assert.Equal(2048, certificate.Size);
    }

    private sealed class FakeHandler(Dictionary<string, string> responses)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (!responses.TryGetValue(path, out var body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}

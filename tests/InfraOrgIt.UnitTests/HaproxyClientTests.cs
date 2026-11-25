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

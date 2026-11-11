using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.Features.Catalog;

namespace InfraOrgIt.UnitTests;

public sealed class ApiCallUrlTests
{
    [Fact]
    public void Uses_hostname_and_relative_path()
    {
        var url = ApiCallUrl.Build("http://app:8080", "/api/users", null);

        Assert.Equal("http://app:8080/api/users", url.AbsoluteUri);
    }

    [Fact]
    public void Replaces_the_stored_origin_with_the_hostname()
    {
        var url = ApiCallUrl.Build(
            "https://app.local", "http://swagger-host:9000/api/users?x=1", null);

        Assert.Equal("https://app.local/api/users?x=1", url.AbsoluteUri);
    }

    [Fact]
    public void Appends_the_query_parameters()
    {
        var url = ApiCallUrl.Build(
            "http://app",
            "/api/users?active=1",
            new Dictionary<string, string> { ["name"] = "a b", ["id"] = "2" });

        Assert.Equal("http://app/api/users?active=1&name=a%20b&id=2", url.AbsoluteUri);
    }

    [Fact]
    public void Adds_a_query_key_to_an_existing_url()
    {
        var url = ApiCallUrl.AddQuery(new Uri("http://app/api?x=1"), "api_key", "k");

        Assert.Equal("http://app/api?x=1&api_key=k", url.AbsoluteUri);
    }

    [Fact]
    public void Keeps_the_hostname_path()
    {
        var url = ApiCallUrl.Build("http://app/base", "/api/users", null);

        Assert.Equal("http://app/base/api/users", url.AbsoluteUri);
    }

    [Fact]
    public void Redacts_the_configured_query_keys()
    {
        var url = new Uri("http://app/api?x=1&token=super-secret&Token2=abc");

        var redacted = ApiCallUrl.Redact(url, ["token", "token2"]);

        Assert.Equal("http://app/api?x=1&token=***&Token2=***", redacted);
        Assert.DoesNotContain("super-secret", redacted);
    }

    [Fact]
    public void Keeps_the_query_when_no_key_is_configured()
    {
        var url = new Uri("http://app/api?x=1");

        Assert.Equal("http://app/api?x=1", ApiCallUrl.Redact(url, []));
    }

    [Fact]
    public void Redacts_the_saved_query_json()
    {
        var query = new Dictionary<string, string> { ["token"] = "secret", ["x"] = "1" };

        var redacted = ApiCallUrl.RedactQuery(query, ["token"]);

        Assert.NotNull(redacted);
        Assert.Equal("***", redacted["token"]);
        Assert.Equal("1", redacted["x"]);
    }

    [Fact]
    public void Fails_without_a_hostname_or_absolute_url()
    {
        Assert.Throws<ApiException>(() => ApiCallUrl.Build(null, "/api/users", null));
    }

    [Fact]
    public void Fails_on_a_non_http_hostname()
    {
        Assert.Throws<ApiException>(() => ApiCallUrl.Build("ftp://app", "/api/users", null));
    }
}

using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.Features.Haproxy;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;

namespace InfraOrgIt.UnitTests;

public sealed class HaproxyUrlRulesTests
{
    [Theory]
    [InlineData("ftp://proxy.example.com")]
    [InlineData("https://user:secret@proxy.example.com")]
    [InlineData("https://proxy.example.com/?x=1")]
    [InlineData("https://proxy.example.com/#fragment")]
    [InlineData("/relative")]
    public void Rejects_unsafe_or_malformed_urls(string value)
    {
        var exception = Assert.Throws<ApiException>(
            () => HaproxyUrlRules.Parse(value, "BaseUrl"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("BaseUrl", exception.Message);
    }

    [Fact]
    public void Accepts_plain_http_and_https_urls()
    {
        Assert.Equal("https://proxy.example.com:5555", HaproxyUrlRules
            .Parse("https://proxy.example.com:5555", "BaseUrl").ToString()
            .TrimEnd('/'));
        Assert.Equal("http://127.0.0.1:5555", HaproxyUrlRules
            .Parse("http://127.0.0.1:5555", "BaseUrl").ToString()
            .TrimEnd('/'));
    }

    [Fact]
    public void Matches_server_ip_hostname_and_public_ip()
    {
        var server = new Server
        {
            IpAddress = "10.0.0.5",
            Hostname = "proxy.example.com",
            PublicIpAddress = "203.0.113.7"
        };

        Assert.True(HaproxyUrlRules.HostMatchesServer(
            new Uri("https://10.0.0.5:5555"), server));
        Assert.True(HaproxyUrlRules.HostMatchesServer(
            new Uri("https://proxy.example.com"), server));
        Assert.True(HaproxyUrlRules.HostMatchesServer(
            new Uri("https://203.0.113.7"), server));
        Assert.False(HaproxyUrlRules.HostMatchesServer(
            new Uri("https://other.example.com"), server));
        Assert.False(HaproxyUrlRules.HostMatchesServer(
            new Uri("https://169.254.169.254"), server));
    }

    [Fact]
    public void Normalizes_brackets_and_trailing_dot()
    {
        var server = new Server { IpAddress = "::1", Hostname = "proxy.example.com." };

        Assert.True(HaproxyUrlRules.HostMatchesServer(
            new Uri("https://[::1]:5555"), server));
        Assert.True(HaproxyUrlRules.HostMatchesServer(
            new Uri("https://proxy.example.com"), server));
    }
}

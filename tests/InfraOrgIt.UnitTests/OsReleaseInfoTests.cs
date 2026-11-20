using System.Net;
using System.Text;
using InfraOrgIt.Api.Features.Inventory;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraOrgIt.UnitTests;

public sealed class OsReleaseInfoTests
{
    private const string Ubuntu = """
        NAME="Ubuntu"
        VERSION="24.04.3 LTS (Noble Numbat)"
        ID=ubuntu
        ID_LIKE=debian
        PRETTY_NAME="Ubuntu 24.04.3 LTS"
        VERSION_ID="24.04"
        VERSION_CODENAME=noble
        """;

    [Fact]
    public void Parses_os_release()
    {
        var info = OsReleaseInfo.Parse(Ubuntu);
        Assert.NotNull(info);
        Assert.Equal("ubuntu", info.Id);
        Assert.Equal("24.04", info.VersionId);
        Assert.Equal("noble", info.Codename);
        Assert.Equal("Ubuntu 24.04.3 LTS", info.PrettyName);
        Assert.Equal("24", info.Major);
    }

    [Fact]
    public void Rejects_text_without_id()
    {
        Assert.Null(OsReleaseInfo.Parse("NAME=Unknown"));
    }

    [Fact]
    public async Task Selects_the_next_lts_cycle_and_the_current_eol()
    {
        var catalog = Catalog("""
            [
              {"cycle":"26.10","eol":"2027-07-01","lts":false},
              {"cycle":"26.04","eol":"2031-05-29","lts":true},
              {"cycle":"25.10","eol":"2026-07-01","lts":false},
              {"cycle":"25.04","eol":"2026-01-15","lts":false},
              {"cycle":"24.10","eol":"2025-07-01","lts":false},
              {"cycle":"24.04","eol":"2029-05-31","lts":true}
            ]
            """);
        var result = await catalog.LookupAsync(
            new OsReleaseInfo("ubuntu", "Ubuntu", "24.04", "noble", "Ubuntu 24.04"),
            ltsOnly: true, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("26.04", result.TargetVersion);
        Assert.True(result.TargetLts);
        Assert.Equal(new DateOnly(2029, 5, 31), result.CurrentEol);
    }

    [Fact]
    public async Task Allows_the_next_non_lts_when_requested()
    {
        var catalog = Catalog("""
            [
              {"cycle":"26.04","eol":"2031-05-29","lts":true},
              {"cycle":"24.10","eol":"2025-07-01","lts":false},
              {"cycle":"24.04","eol":"2029-05-31","lts":true}
            ]
            """);
        var result = await catalog.LookupAsync(
            new OsReleaseInfo("ubuntu", "Ubuntu", "24.04", "noble", "Ubuntu 24.04"),
            ltsOnly: false, CancellationToken.None);
        Assert.Equal("24.10", result?.TargetVersion);
        Assert.False(result?.TargetLts);
    }

    [Fact]
    public async Task Reports_only_non_lts_when_lts_is_required()
    {
        var catalog = Catalog("""
            [
              {"cycle":"25.10","eol":"2026-07-01","lts":false},
              {"cycle":"24.04","eol":"2029-05-31","lts":true}
            ]
            """);
        var result = await catalog.LookupAsync(
            new OsReleaseInfo("ubuntu", "Ubuntu", "24.04", "noble", "Ubuntu 24.04"),
            ltsOnly: true, CancellationToken.None);
        Assert.Null(result?.TargetVersion);
        Assert.Contains("25.10", result?.Detail);
    }

    [Fact]
    public async Task Rhel_and_sles_majors_are_always_lts()
    {
        var rhel = Catalog("""
            [
              {"cycle":"10","eol":"2035-06-30","lts":false},
              {"cycle":"9","eol":"2032-06-30","lts":false}
            ]
            """, "rhel");
        var result = await rhel.LookupAsync(
            new OsReleaseInfo("rhel", "RHEL", "9.4", "", "RHEL 9.4"),
            ltsOnly: true, CancellationToken.None);
        Assert.Equal("10", result?.TargetVersion);
        Assert.True(result?.TargetLts);
    }

    private static EndOfLifeCatalog Catalog(string json, string product = "ubuntu")
    {
        var client = new HttpClient(new Handler(product, json))
        {
            BaseAddress = new Uri("https://endoflife.test/api/")
        };
        return new EndOfLifeCatalog(
            new Factory(client),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<EndOfLifeCatalog>.Instance);
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler(string product, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.Contains(product, StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}

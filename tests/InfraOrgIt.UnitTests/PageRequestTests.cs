using InfraOrgIt.Api.BuildingBlocks.Pagination;
using Microsoft.AspNetCore.Http;

namespace InfraOrgIt.UnitTests;

public sealed class PageRequestTests
{
    [Fact]
    public void Reads_q_and_sort_from_the_right_query_keys()
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString("?page=2&pageSize=10&q=abc&sort=-name");

        var page = PageRequest.From(http);

        Assert.Equal(2, page.Page);
        Assert.Equal(10, page.PageSize);
        Assert.Equal("abc", page.Q);
        Assert.Equal("-name", page.Sort);
    }
}

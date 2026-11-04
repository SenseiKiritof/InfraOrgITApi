using InfraOrgIt.Api.Infrastructure.Files;

namespace InfraOrgIt.UnitTests;

public sealed class RemotePathTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("var/www", "/var/www")]
    [InlineData("/var//www/", "/var/www")]
    [InlineData("/var/./www", "/var/www")]
    [InlineData("/var/log/../www", "/var/www")]
    [InlineData("/../etc", "/etc")]
    [InlineData("\\var\\www", "/var/www")]
    public void Normalizes_to_an_absolute_posix_path(string? input, string expected)
    {
        Assert.Equal(expected, RemotePath.Normalize(input));
    }

    [Fact]
    public void Combines_and_reads_the_last_segment()
    {
        Assert.Equal("/var/www/index.html", RemotePath.Combine("/var/www", "index.html"));
        Assert.Equal("/index.html", RemotePath.Combine("/", "index.html"));
        Assert.Equal("index.html", RemotePath.Name("/var/www/index.html"));
        Assert.Equal("/var", RemotePath.Combine("/var/www", ".."));
    }
}

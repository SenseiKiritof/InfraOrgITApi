using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.Features.Remote;

namespace InfraOrgIt.UnitTests;

public sealed class CommandRendererTests
{
    [Fact]
    public void Renders_placeholder_single_quoted_and_escaped()
    {
        var script = CommandRenderer.Render(
            "echo {{Name}}",
            new Dictionary<string, string> { ["Name"] = "it's" });

        Assert.Equal("echo 'it'\\''s'", script);
    }

    [Fact]
    public void Missing_placeholder_fails()
    {
        Assert.Throws<ApiException>(() =>
            CommandRenderer.Render("echo {{Name}}", new Dictionary<string, string>()));
    }

    [Fact]
    public void Rejects_shell_active_characters_in_used_values()
    {
        Assert.Throws<ApiException>(() =>
            CommandRenderer.CheckValues(
                "echo {{Name}}",
                new Dictionary<string, string> { ["Name"] = "$(id)" }));
    }

    [Fact]
    public void Ignores_unsafe_characters_in_unused_values()
    {
        CommandRenderer.CheckValues(
            "echo ok",
            new Dictionary<string, string> { ["Name"] = "$(id)" });
    }

    [Fact]
    public void Lists_distinct_placeholders()
    {
        Assert.Equal(
            ["Version", "ServerName"],
            CommandRenderer.Placeholders("echo {{Version}} {{ServerName}} {{Version}}"));
    }
}

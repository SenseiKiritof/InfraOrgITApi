using System.Text;
using InfraOrgIt.Api.Features.Auth;

namespace InfraOrgIt.UnitTests;

public sealed class OidcIdentityParserTests
{
    [Fact]
    public void Parses_username_name_and_surname()
    {
        const string payload = """
            {"preferred_username":"mario.rossi","given_name":"Mario","family_name":"Rossi"}
            """;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var identity = OidcIdentityParser.Parse($"header.{encoded}.signature");

        Assert.Equal("mario.rossi", identity.Username);
        Assert.Equal("Mario", identity.Name);
        Assert.Equal("Rossi", identity.Surname);
    }
}

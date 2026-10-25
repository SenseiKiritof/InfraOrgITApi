using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.Features.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace InfraOrgIt.UnitTests;

public sealed class OidcIdentityValidatorTests
{
    private const string Issuer = "https://issuer.test/realms/ds";
    private const string ClientId = "infraorgit";

    [Fact]
    public async Task Validates_signed_token_and_extracts_identity()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(key);

        var handler = new JwtSecurityTokenHandler();
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: ClientId,
            claims:
            [
                new Claim("preferred_username", "Mario.Rossi"),
                new Claim("given_name", "Mario"),
                new Claim("family_name", "Rossi")
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        var jwt = handler.WriteToken(token);

        var validator = new OidcIdentityValidator(
            new FakeConfigurationManager(configuration),
            Options.Create(new OidcOptions { Issuer = Issuer, ClientId = ClientId }),
            NullLogger<OidcIdentityValidator>.Instance);

        var identity = await validator.ValidateAsync(jwt, CancellationToken.None);

        Assert.Equal("mario.rossi", identity.Username);
        Assert.Equal("Mario", identity.Name);
        Assert.Equal("Rossi", identity.Surname);
    }

    private sealed class FakeConfigurationManager(OpenIdConnectConfiguration configuration)
        : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            Task.FromResult(configuration);

        public void RequestRefresh()
        {
        }
    }
}

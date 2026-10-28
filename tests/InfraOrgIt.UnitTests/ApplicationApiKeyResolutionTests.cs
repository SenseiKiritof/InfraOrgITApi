using InfraOrgIt.Api.Features.Catalog;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;

namespace InfraOrgIt.UnitTests;

public sealed class ApplicationApiKeyResolutionTests
{
    [Fact]
    public void Environment_key_overrides_the_global_one_only_for_the_same_name_and_location()
    {
        var keys = new[]
        {
            new ApplicationApiKey
            {
                Name = "X-Api-Key", Location = "Header", EnvironmentDefinitionId = null, EncryptedValue = "global"
            },
            new ApplicationApiKey
            {
                Name = "X-Api-Key", Location = "Header", EnvironmentDefinitionId = 7, EncryptedValue = "env"
            },
            new ApplicationApiKey
            {
                Name = "X-Api-Key", Location = "Query", EnvironmentDefinitionId = null, EncryptedValue = "query"
            },
            new ApplicationApiKey
            {
                Name = "X-Other", Location = "Header", EnvironmentDefinitionId = null, EncryptedValue = "other"
            }
        };

        var resolved = ApplicationApiKeysEndpoints.Resolve(keys);

        Assert.Equal(3, resolved.Count);
        Assert.Equal("env", resolved.Single(x => x.Location == "Header" && x.Name == "X-Api-Key").EncryptedValue);
        Assert.Equal("query", resolved.Single(x => x.Location == "Query").EncryptedValue);
        Assert.Equal("other", resolved.Single(x => x.Name == "X-Other").EncryptedValue);
    }
}

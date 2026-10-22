using System.Text;
using EPMRuleCreator.Graph;
using Microsoft.Identity.Client;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class MemoryTokenCacheTests
{
    private const string TestClientId = "00000000-0000-4000-8000-000000000001";

    [Fact]
    public async Task NewClientStartsWithoutPersistedAccounts()
    {
        var client = GraphAuthenticationService.CreateClient(TestClientId, "organizations");
        Assert.Empty(await client.GetAccountsAsync());
    }

    [Fact]
    public void SeparateClientsDoNotShareTheirInMemoryCache()
    {
        var first = GraphAuthenticationService.CreateClient(TestClientId, "organizations");
        var second = GraphAuthenticationService.CreateClient(TestClientId, "organizations");
        var data = Encoding.UTF8.GetBytes("""
            {"Account":{"unit-test-account":{"home_account_id":"test-home.test-tenant","environment":"login.microsoftonline.com","realm":"test-tenant","local_account_id":"test-home","username":"unit-test@example.invalid","authority_type":"MSSTS"}}}
            """);
        var firstCache = (ITokenCacheSerializer)first.UserTokenCache;
        var secondCache = (ITokenCacheSerializer)second.UserTokenCache;
        firstCache.DeserializeMsalV3(data);
        Assert.Contains("unit-test@example.invalid", Encoding.UTF8.GetString(firstCache.SerializeMsalV3()));
        Assert.DoesNotContain("unit-test@example.invalid", Encoding.UTF8.GetString(secondCache.SerializeMsalV3()));
    }

    [Fact]
    public void GraphClientHasNoFilePersistenceHelperOrCallbacks()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "EPMRuleCreator.sln"))) continue;
            var project = Path.Combine(directory.FullName, "src", "EPMRuleCreator.Graph");
            var source = File.ReadAllText(Path.Combine(project, "GraphAuthenticationService.cs"));
            Assert.DoesNotContain("MsalCacheHelper", source);
            Assert.DoesNotContain("StorageCreationPropertiesBuilder", source);
            Assert.DoesNotContain("RegisterCache(", source);
            Assert.DoesNotContain("SetBeforeAccess(", source);
            Assert.DoesNotContain("SetAfterAccess(", source);
            Assert.DoesNotContain("Microsoft.Identity.Client.Extensions.Msal", File.ReadAllText(Path.Combine(project, "EPMRuleCreator.Graph.csproj")));
            return;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

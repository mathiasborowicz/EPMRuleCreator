using EPMRuleCreator.Core.Services;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class TenantIdPreferenceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("organizations")]
    [InlineData("ORGANIZATIONS")]
    [InlineData(" organizations ")]
    public void DefaultAndLegacyGenericTenantAreDisplayedEmpty(string? saved)
        => Assert.Equal(string.Empty, TenantIdPreference.ForDisplay(saved));

    [Fact]
    public void ConcreteSavedTenantIsPreserved()
    {
        var tenant = Guid.NewGuid().ToString();
        Assert.Equal(tenant, TenantIdPreference.ForDisplay(" " + tenant + " "));
    }
}

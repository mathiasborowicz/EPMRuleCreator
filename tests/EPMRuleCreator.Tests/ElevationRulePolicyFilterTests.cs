using EPMRuleCreator.Graph;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class ElevationRulePolicyFilterTests
{
    [Theory]
    [InlineData("cff02aad-51b1-498d-83ad-81161a393f56_1", true)]
    [InlineData("CFF02AAD-51B1-498D-83AD-81161A393F56_1", true)]
    [InlineData("598096f6-4121-4088-859f-b11ebfbd09bc_1", false)]
    [InlineData(null, false)]
    public void IsElevationRulePolicyTemplate_OnlyAcceptsElevationRuleTemplate(string? templateId, bool expected)
    {
        Assert.Equal(expected, GraphAuthenticationService.IsElevationRulePolicyTemplate(templateId));
    }
}

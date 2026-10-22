using System.Text.Json.Nodes;
using IntuneEpmRuleCreator.Core.Models;
using IntuneEpmRuleCreator.Graph;
using Xunit;

namespace IntuneEpmRuleCreator.Tests;

public sealed class ElevationTypePayloadTests
{
    // Full item IDs verified against the supplied Graph settingDefinitions and
    // the Intune admin center's successful PUT. Option IDs do not include _ruletype.
    [Theory]
    [InlineData(ElevationType.UserConfirmed, "device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_self")]
    [InlineData(ElevationType.Automatic, "device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_automatic")]
    [InlineData(ElevationType.SupportApproved, "device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_supportarbitrated")]
    [InlineData(ElevationType.Deny, "device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_deny")]
    [InlineData(ElevationType.CurrentUser, "device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_userconfirmeduser")]
    public void ElevationType_UsesTheExactGraphOptionId(ElevationType type, string expectedOptionId)
    {
        var children = CreateRuleChildren();

        GraphAuthenticationService.SetElevationType(children, new ElevationRule { ElevationType = type });

        var setting = children[0]!;
        Assert.Equal("device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_ruletype",
            setting["settingDefinitionId"]!.GetValue<string>());
        Assert.Equal(expectedOptionId, setting["choiceSettingValue"]!["value"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UserConfirmed_UsesValidationIdsFromTheIntunePayload(bool justification, bool authentication)
    {
        var children = CreateRuleChildren();

        GraphAuthenticationService.SetElevationType(children, new ElevationRule
        {
            ElevationType = ElevationType.UserConfirmed,
            RequireBusinessJustification = justification,
            RequireWindowsAuthentication = authentication
        });

        var validation = children[0]!["choiceSettingValue"]!["children"]!.AsArray();
        if (!justification && !authentication)
        {
            // The successful Intune PUT uses children: [] when neither optional
            // validation is selected; Graph rejects an empty collection instance.
            Assert.Empty(validation);
            return;
        }

        var collection = Assert.Single(validation)!;
        Assert.Equal("device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_ruletype_validation",
            collection["settingDefinitionId"]!.GetValue<string>());
        var expected = new List<string>();
        if (justification)
            expected.Add("device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_ruletype_validation_0");
        if (authentication)
            expected.Add("device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_ruletype_validation_1");
        Assert.Equal(expected, collection["choiceSettingCollectionValue"]!.AsArray()
            .Select(value => value!["value"]!.GetValue<string>()).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentUser_AlwaysRequiresWindowsAuthentication_AndOnlyAddsSelectedJustification(bool justification)
    {
        var children = CreateRuleChildren();

        GraphAuthenticationService.SetElevationType(children, new ElevationRule
        {
            ElevationType = ElevationType.CurrentUser,
            RequireWindowsAuthentication = false,
            RequireBusinessJustification = justification
        });

        var validation = children[0]!["choiceSettingValue"]!["children"]!.AsArray();
        Assert.Equal(justification ? 2 : 1, validation.Count);
        Assert.Equal("device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_ruletype_userconfirmeduservalidation_0",
            validation[0]!["choiceSettingCollectionValue"]![0]!["value"]!.GetValue<string>());
        if (justification)
            Assert.Equal("device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_ruletype_userconfirmeduservalidation_2_0",
                validation[1]!["choiceSettingCollectionValue"]![0]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void UserConfirmed_ClearingValidation_RemovesPreviouslySelectedRequirements()
    {
        var children = CreateRuleChildren();
        GraphAuthenticationService.SetElevationType(children, new ElevationRule
        {
            ElevationType = ElevationType.UserConfirmed,
            RequireBusinessJustification = true,
            RequireWindowsAuthentication = true
        });

        GraphAuthenticationService.SetElevationType(children, new ElevationRule
        {
            ElevationType = ElevationType.UserConfirmed
        });

        Assert.Empty(children[0]!["choiceSettingValue"]!["children"]!.AsArray());
    }

    private static JsonArray CreateRuleChildren() => JsonNode.Parse("""
        [{
          "@odata.type": "#microsoft.graph.deviceManagementConfigurationChoiceSettingInstance",
          "settingDefinitionId": "device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_ruletype",
          "choiceSettingValue": {
            "@odata.type": "#microsoft.graph.deviceManagementConfigurationChoiceSettingValue",
            "value": "device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_automatic",
            "children": []
          }
        }]
        """)!.AsArray();
}

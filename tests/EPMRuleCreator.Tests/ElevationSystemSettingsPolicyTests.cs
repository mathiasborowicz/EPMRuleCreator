using EPMRuleCreator.Graph;
using System.Net;
using System.Text;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class ElevationSystemSettingsPolicyTests
{
    [Fact]
    public void AutomaticPolicy_MatchesTheSuppliedGraphPayload()
    {
        var body = NewElevationSystemSettingsPolicyPayload.Build(" ESS Policy ", "", true, true, false, false);

        Assert.Equal("ESS Policy", body["name"]!.GetValue<string>());
        Assert.Equal("598096f6-4121-4088-859f-b11ebfbd09bc_1", body["templateReference"]!["templateId"]!.GetValue<string>());
        Assert.Equal("windows10", body["platforms"]!.GetValue<string>());
        Assert.Equal("endpointPrivilegeManagement", body["technologies"]!.GetValue<string>());
        var setting = body["settings"]![0]!;
        var root = setting["settingInstance"]!;
        Assert.Equal("device_vendor_msft_policy_privilegemanagement_systemsettingrules_{systemsettingrulename}_networksettings",
            root["choiceSettingValue"]!["value"]!.GetValue<string>());
        Assert.Equal("edefb87f-e026-4e01-bbce-a7ea164f20fa",
            root["choiceSettingValue"]!["settingValueTemplateReference"]!["settingValueTemplateId"]!.GetValue<string>());
        Assert.Equal("a92a1181-863a-40b3-8c2c-04b512f10140",
            root["settingInstanceTemplateReference"]!["settingInstanceTemplateId"]!.GetValue<string>());
        var elevation = root["choiceSettingValue"]!["children"]![0]!;
        Assert.Equal("device_vendor_msft_policy_privilegemanagement_systemsettingrules_{systemsettingrulename}_automatic",
            elevation["choiceSettingValue"]!["value"]!.GetValue<string>());
        Assert.Empty(elevation["choiceSettingValue"]!["children"]!.AsArray());
    }

    [Fact]
    public void UserConfirmedPolicy_EmitsSelectedValidationOptions()
    {
        var body = NewElevationSystemSettingsPolicyPayload.Build("ESS Policy", null, true, false, true, false);
        var action = body["settings"]![0]!["settingInstance"]!["choiceSettingValue"]!["children"]![0]!;

        Assert.Equal("device_vendor_msft_policy_privilegemanagement_systemsettingrules_{systemsettingrulename}_self",
            action["choiceSettingValue"]!["value"]!.GetValue<string>());
        var validation = Assert.Single(action["choiceSettingValue"]!["children"]!.AsArray())!;
        Assert.Equal("#microsoft.graph.deviceManagementConfigurationChoiceSettingCollectionInstance", validation["@odata.type"]!.GetValue<string>());
        Assert.Equal("device_vendor_msft_policy_privilegemanagement_systemsettingrules_{systemsettingrulename}_elevationvalidation",
            validation["settingDefinitionId"]!.GetValue<string>());
        var option = Assert.Single(validation["choiceSettingCollectionValue"]!.AsArray())!;
        Assert.Equal("device_vendor_msft_policy_privilegemanagement_systemsettingrules_{systemsettingrulename}_elevationvalidation_0",
            option["value"]!.GetValue<string>());
    }

    [Fact]
    public void AutomaticPolicy_DoesNotEmitValidationChildren()
    {
        var body = NewElevationSystemSettingsPolicyPayload.Build("ESS Policy", null, true, true, false, false);
        var action = body["settings"]![0]!["settingInstance"]!["choiceSettingValue"]!["children"]![0]!;

        Assert.Empty(action["choiceSettingValue"]!["children"]!.AsArray());
    }

    [Fact]
    public void DisabledNetworkSettingsPolicy_ContainsNoSettings()
    {
        var body = NewElevationSystemSettingsPolicyPayload.Build("ESS Policy", null, false, false, false, false);

        Assert.Empty(body["settings"]!.AsArray());
    }

    [Fact]
    public void UserConfirmedPolicy_CanRequireBothValidationOptions()
    {
        var body = NewElevationSystemSettingsPolicyPayload.Build("ESS Policy", null, true, false, true, true);
        var validation = body["settings"]![0]!["settingInstance"]!["choiceSettingValue"]!["children"]![0]!["choiceSettingValue"]!["children"]![0]!;
        var values = validation["choiceSettingCollectionValue"]!.AsArray();

        Assert.Equal(2, values.Count);
        Assert.EndsWith("_elevationvalidation_0", values[0]!["value"]!.GetValue<string>());
        Assert.EndsWith("_elevationvalidation_1", values[1]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Writer_PostsToConfigurationPoliciesAndReturnsTheCreatedId()
    {
        using var client = new HttpClient(new CreatedPolicyHandler());
        var id = await new NewElevationSystemSettingsPolicyWriter(client).CreateAsync(
            "access-token", "ESS Policy", "", true, true, false, false);

        Assert.Equal("11111111-1111-1111-1111-111111111111", id);
    }

    private sealed class CreatedPolicyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://graph.microsoft.com/beta/deviceManagement/configurationPolicies", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"id\":\"11111111-1111-1111-1111-111111111111\"}", Encoding.UTF8, "application/json")
            });
        }
    }
}

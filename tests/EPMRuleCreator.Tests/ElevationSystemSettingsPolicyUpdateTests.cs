using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EPMRuleCreator.Graph;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class ElevationSystemSettingsPolicyUpdateTests
{
    private const string PolicyId = "11111111-1111-1111-1111-111111111111";
    private const string Root = "device_vendor_msft_policy_privilegemanagement_systemsettingrules_{systemsettingrulename}";

    [Theory]
    [InlineData("automatic", false, false, true)]
    [InlineData("self", false, false, false)]
    [InlineData("self", true, false, false)]
    [InlineData("self", false, true, false)]
    [InlineData("self", true, true, false)]
    public void ParseConfiguration_ReadsElevationActionAndValidationOptions(
        string action, bool businessJustification, bool windowsAuthentication, bool automatic)
    {
        var policy = EmptyPolicy();
        var actual = ElevationSystemSettingsPolicyWriter.ParseConfiguration(policy,
            SystemSettingsInstance(action, businessJustification, windowsAuthentication));

        Assert.True(actual.NetworkSettingsEnabled);
        Assert.Equal(automatic, actual.Automatic);
        Assert.Equal(!automatic && businessJustification, actual.RequireBusinessJustification);
        Assert.Equal(!automatic && windowsAuthentication, actual.RequireWindowsAuthentication);
    }

    [Theory]
    [InlineData(true, false, false, "automatic")]
    [InlineData(false, false, false, "self")]
    [InlineData(false, true, false, "self")]
    [InlineData(false, false, true, "self")]
    [InlineData(false, true, true, "self")]
    public void UpdateInstance_ChangesOnlyElevationActionAndValidationBranch(
        bool automatic, bool businessJustification, bool windowsAuthentication, string expectedAction)
    {
        var original = SystemSettingsInstance("self", true, true);
        original["settingInstanceTemplateReference"] = new JsonObject { ["settingInstanceTemplateId"] = "keep-instance-template" };
        var input = original.ToJsonString();

        var updated = ElevationSystemSettingsPolicyWriter.UpdateSystemSettingsInstance(
            original, automatic, businessJustification, windowsAuthentication);

        Assert.Equal(input, original.ToJsonString());
        Assert.Equal("keep-instance-template", updated["settingInstanceTemplateReference"]!["settingInstanceTemplateId"]!.GetValue<string>());
        var choice = updated["choiceSettingValue"]!;
        Assert.Equal(Root + "_networksettings", choice["value"]!.GetValue<string>());
        var action = choice["children"]![0]!;
        Assert.Equal(Root + "_" + expectedAction, action["choiceSettingValue"]!["value"]!.GetValue<string>());
        var children = action["choiceSettingValue"]!["children"]!.AsArray();
        var expectedCount = !automatic && (businessJustification || windowsAuthentication) ? 1 : 0;
        Assert.Equal(expectedCount, children.Count);
        if (expectedCount == 1)
        {
            var values = children[0]! ["choiceSettingCollectionValue"]!.AsArray();
            Assert.Equal((businessJustification ? 1 : 0) + (windowsAuthentication ? 1 : 0), values.Count);
            Assert.Equal(businessJustification, values.Any(value => value!["value"]!.GetValue<string>() == Root + "_elevationvalidation_0"));
            Assert.Equal(windowsAuthentication, values.Any(value => value!["value"]!.GetValue<string>() == Root + "_elevationvalidation_1"));
        }
    }

    [Fact]
    public void UpdateBody_PreservesAllKnownPolicyFieldsAndReplacesNameDescriptionAndSettings()
    {
        var original = new JsonObject
        {
            ["creationSource"] = "adminCenter",
            ["platforms"] = "windows10",
            ["technologies"] = "endpointPrivilegeManagement",
            ["roleScopeTagIds"] = new JsonArray("0", "tag-1"),
            ["templateReference"] = new JsonObject { ["templateId"] = ElevationSystemSettingsPolicyWriter.TemplateId },
            ["name"] = "Old name",
            ["description"] = "Old description"
        };
        var settings = new JsonArray(new JsonObject { ["settingInstance"] = new JsonObject { ["settingDefinitionId"] = "preserved" } });

        var body = ElevationSystemSettingsPolicyWriter.BuildUpdateBody(original, "New name", "New description", settings);

        Assert.Equal("adminCenter", body["creationSource"]!.GetValue<string>());
        Assert.Equal("windows10", body["platforms"]!.GetValue<string>());
        Assert.Equal("endpointPrivilegeManagement", body["technologies"]!.GetValue<string>());
        Assert.Equal("tag-1", body["roleScopeTagIds"]![1]!.GetValue<string>());
        Assert.Equal(ElevationSystemSettingsPolicyWriter.TemplateId, body["templateReference"]!["templateId"]!.GetValue<string>());
        Assert.Equal("New name", body["name"]!.GetValue<string>());
        Assert.Equal("New description", body["description"]!.GetValue<string>());
        Assert.Same(settings, body["settings"]);
    }

    [Fact]
    public async Task UpdateAsyncGetsPolicyAndSettingsPreservesOtherInstancesPutsAndVerifiesReadBack()
    {
        var handler = new UpdatingPolicyHandler();
        using var client = new HttpClient(handler);
        var inputPolicy = EmptyPolicy();

        var actual = await new ElevationSystemSettingsPolicyWriter(client).UpdateAsync(
            "token", inputPolicy, "Updated policy", "Updated description", true, false, true, true);

        Assert.Equal(new ElevationSystemSettingsPolicy(PolicyId, "Updated policy", "Updated description", true, false, true, true), actual);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Get, HttpMethod.Put, HttpMethod.Get, HttpMethod.Get }, handler.Methods);
        Assert.Equal(new[]
        {
            $"https://graph.microsoft.com/beta/deviceManagement/configurationPolicies('{PolicyId}')",
            $"https://graph.microsoft.com/beta/deviceManagement/configurationPolicies('{PolicyId}')/settings?$top=1000",
            $"https://graph.microsoft.com/beta/deviceManagement/configurationPolicies('{PolicyId}')",
            $"https://graph.microsoft.com/beta/deviceManagement/configurationPolicies('{PolicyId}')",
            $"https://graph.microsoft.com/beta/deviceManagement/configurationPolicies('{PolicyId}')/settings?$top=1000"
        }, handler.Uris);

        var put = handler.PutBody!;
        Assert.Equal("adminCenter", put["creationSource"]!.GetValue<string>());
        Assert.Equal("Updated policy", put["name"]!.GetValue<string>());
        Assert.Equal("Updated description", put["description"]!.GetValue<string>());
        var putSettings = put["settings"]!.AsArray();
        Assert.Equal(2, putSettings.Count);
        var otherSetting = putSettings.Single(item => item!["settingInstance"]!["settingDefinitionId"]!.GetValue<string>() == "other-setting");
        Assert.Equal("leave-me-alone", otherSetting!["settingInstance"]!["simpleSettingValue"]!["value"]!.GetValue<string>());
        var action = putSettings.Single(item => item!["settingInstance"]!["settingDefinitionId"]!.GetValue<string>() == Root + "_settingtype")!["settingInstance"]!["choiceSettingValue"]!["children"]![0]!;
        Assert.Equal(Root + "_self", action["choiceSettingValue"]!["value"]!.GetValue<string>());
        var validation = action["choiceSettingValue"]!["children"]![0]! ["choiceSettingCollectionValue"]!.AsArray();
        Assert.Equal(2, validation.Count);
    }

    [Fact]
    public async Task UpdateAsyncCanDisableAndLaterReEnableNetworkSettings()
    {
        var disableHandler = new UpdatingPolicyHandler();
        using var disableClient = new HttpClient(disableHandler);
        var disabled = await new ElevationSystemSettingsPolicyWriter(disableClient).UpdateAsync(
            "token", EmptyPolicy(), "Policy", "Description", false, false, false, false);

        Assert.False(disabled.NetworkSettingsEnabled);
        Assert.DoesNotContain(disableHandler.PutBody!["settings"]!.AsArray(), item =>
            item!["settingInstance"]!["settingDefinitionId"]!.GetValue<string>() == Root + "_settingtype");

        var enableHandler = new UpdatingPolicyHandler(includeNetworkSettings: false);
        using var enableClient = new HttpClient(enableHandler);
        var enabled = await new ElevationSystemSettingsPolicyWriter(enableClient).UpdateAsync(
            "token", EmptyPolicy(), "Policy", "Description", true, false, false, false);

        Assert.True(enabled.NetworkSettingsEnabled);
        var reEnabled = enableHandler.PutBody!["settings"]!.AsArray().Single(item =>
            item!["settingInstance"]!["settingDefinitionId"]!.GetValue<string>() == Root + "_settingtype");
        Assert.Equal(Root + "_self", reEnabled!["settingInstance"]!["choiceSettingValue"]!["children"]![0]!["choiceSettingValue"]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetPolicies_FiltersToSystemSettingsTemplate()
    {
        using var client = new HttpClient(new ListPoliciesHandler());
        var policies = await new ElevationSystemSettingsPolicyWriter(client).GetPoliciesAsync("token");

        var policy = Assert.Single(policies);
        Assert.Equal(PolicyId, policy.Id);
        Assert.Equal("ESS", policy.Name);
        Assert.False(policy.NetworkSettingsEnabled); // Configuration is loaded by GetElevationSystemSettingsPolicyConfigurationAsync.
    }

    private static ElevationSystemSettingsPolicy EmptyPolicy() =>
        new(PolicyId, "Policy", "Description", false, false, false, false);

    private static JsonObject SystemSettingsInstance(string action, bool business, bool windows)
    {
        var validations = new JsonArray();
        if (business || windows)
        {
            var values = new JsonArray();
            if (business) values.Add(new JsonObject { ["value"] = Root + "_elevationvalidation_0", ["children"] = new JsonArray() });
            if (windows) values.Add(new JsonObject { ["value"] = Root + "_elevationvalidation_1", ["children"] = new JsonArray() });
            validations.Add(new JsonObject
            {
                ["settingDefinitionId"] = Root + "_elevationvalidation",
                ["choiceSettingCollectionValue"] = values
            });
        }
        return new JsonObject
        {
            ["settingDefinitionId"] = Root + "_settingtype",
            ["choiceSettingValue"] = new JsonObject
            {
                ["value"] = Root + "_networksettings",
                ["children"] = new JsonArray(new JsonObject
                {
                    ["settingDefinitionId"] = Root + "_elevationaction",
                    ["choiceSettingValue"] = new JsonObject
                    {
                        ["value"] = Root + "_" + action,
                        ["children"] = validations
                    }
                }
            }
        };
    }

    private sealed class UpdatingPolicyHandler : HttpMessageHandler
    {
        private JsonObject? _savedSystemInstance;
        private JsonObject? _putBody;
        private string _savedName = "Policy";
        private string _savedDescription = "Description";
        internal List<HttpMethod> Methods { get; } = [];
        internal List<string> Uris { get; } = [];
        internal JsonObject? PutBody => _putBody;

        internal UpdatingPolicyHandler(bool includeNetworkSettings = true)
        {
            if (includeNetworkSettings)
                _savedSystemInstance = SystemSettingsInstance("automatic", false, false);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            Uris.Add(request.RequestUri!.AbsoluteUri);
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.EndsWith($"configurationPolicies('{PolicyId}')", StringComparison.Ordinal))
                return JsonResponse(new JsonObject
                {
                    ["creationSource"] = "adminCenter",
                    ["platforms"] = "windows10",
                    ["technologies"] = "endpointPrivilegeManagement",
                    ["roleScopeTagIds"] = new JsonArray("0"),
                    ["templateReference"] = new JsonObject { ["templateId"] = ElevationSystemSettingsPolicyWriter.TemplateId },
                    ["name"] = _savedName,
                    ["description"] = _savedDescription
                }.ToJsonString());
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.EndsWith("/settings", StringComparison.Ordinal))
                return SettingsResponse(_savedSystemInstance);
            if (request.Method == HttpMethod.Put)
            {
                _putBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                _savedName = _putBody["name"]!.GetValue<string>();
                _savedDescription = _putBody["description"]!.GetValue<string>();
                var system = _putBody["settings"]!.AsArray().SingleOrDefault(item =>
                    item!["settingInstance"]!["settingDefinitionId"]!.GetValue<string>() == Root + "_settingtype");
                _savedSystemInstance = system is null ? null : (JsonObject)system["settingInstance"]!.DeepClone();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        }

        private static HttpResponseMessage SettingsResponse(JsonObject? systemInstance)
        {
            var values = new JsonArray();
            if (systemInstance is not null)
                values.Add(new JsonObject { ["settingInstance"] = systemInstance.DeepClone() });
            values.Add(new JsonObject
            {
                ["settingInstance"] = new JsonObject
                {
                    ["settingDefinitionId"] = "other-setting",
                    ["simpleSettingValue"] = new JsonObject { ["value"] = "leave-me-alone" }
                })
            });
            return JsonResponse(new JsonObject { ["value"] = values }.ToJsonString());
        }
    }

    private sealed class ListPoliciesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Contains("templateId", request.RequestUri!.Query);
            Assert.Contains(Uri.EscapeDataString(ElevationSystemSettingsPolicyWriter.TemplateId), request.RequestUri.Query);
            return Task.FromResult(JsonResponse("""
                {"value":[
                  {"id":"11111111-1111-1111-1111-111111111111","name":"ESS","description":"system","templateReference":{"templateId":"598096f6-4121-4088-859f-b11ebfbd09bc_1"}},
                  {"id":"22222222-2222-2222-2222-222222222222","name":"Other","templateReference":{"templateId":"cff02aad-51b1-498d-83ad-81161a393f56_1"}}
                ]}
                """));
        }
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

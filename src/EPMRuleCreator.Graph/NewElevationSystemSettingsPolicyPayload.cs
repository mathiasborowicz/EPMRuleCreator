using System.Text.Json.Nodes;

namespace EPMRuleCreator.Graph;

internal static class NewElevationSystemSettingsPolicyPayload
{
    private const string RootDefinitionId = "device_vendor_msft_policy_privilegemanagement_systemsettingrules_{systemsettingrulename}";
    private const string GraphType = "#microsoft.graph.deviceManagementConfiguration";

    internal static JsonObject Build(string name, string? description, bool networkSettingsEnabled, bool automatic,
        bool requireBusinessJustification, bool requireWindowsAuthentication)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (automatic && (requireBusinessJustification || requireWindowsAuthentication))
            throw new ArgumentException("Validation options require User confirmed elevation.");

        var validations = new JsonArray();
        if (requireBusinessJustification || requireWindowsAuthentication)
        {
            var selectedOptions = new JsonArray();
            if (requireBusinessJustification) selectedOptions.Add(ValidationOption("0"));
            if (requireWindowsAuthentication) selectedOptions.Add(ValidationOption("1"));
            validations.Add(new JsonObject
            {
                ["@odata.type"] = GraphType + "ChoiceSettingCollectionInstance",
                ["settingDefinitionId"] = RootDefinitionId + "_elevationvalidation",
                ["choiceSettingCollectionValue"] = selectedOptions
            });
        }

        var elevationAction = new JsonObject
        {
            ["@odata.type"] = GraphType + "ChoiceSettingInstance",
            ["settingDefinitionId"] = RootDefinitionId + "_elevationaction",
            ["choiceSettingValue"] = new JsonObject
            {
                ["@odata.type"] = GraphType + "ChoiceSettingValue",
                ["value"] = RootDefinitionId + (automatic ? "_automatic" : "_self"),
                ["children"] = validations
            }
        };

        var setting = new JsonObject
        {
            ["@odata.type"] = GraphType + "Setting",
            ["settingInstance"] = new JsonObject
            {
                ["@odata.type"] = GraphType + "ChoiceSettingInstance",
                ["settingDefinitionId"] = RootDefinitionId + "_settingtype",
                ["choiceSettingValue"] = new JsonObject
                {
                    ["@odata.type"] = GraphType + "ChoiceSettingValue",
                    ["value"] = RootDefinitionId + "_networksettings",
                    ["children"] = new JsonArray(elevationAction),
                    ["settingValueTemplateReference"] = new JsonObject { ["settingValueTemplateId"] = "edefb87f-e026-4e01-bbce-a7ea164f20fa" }
                },
                ["settingInstanceTemplateReference"] = new JsonObject { ["settingInstanceTemplateId"] = "a92a1181-863a-40b3-8c2c-04b512f10140" }
            }
        };

        return new JsonObject
        {
            ["@odata.type"] = GraphType + "Policy",
            ["name"] = name.Trim(),
            ["description"] = description ?? string.Empty,
            ["platforms"] = "windows10",
            ["technologies"] = "endpointPrivilegeManagement",
            ["roleScopeTagIds"] = new JsonArray("0"),
            ["settings"] = networkSettingsEnabled ? new JsonArray(setting) : new JsonArray(),
            ["templateReference"] = new JsonObject { ["templateId"] = "598096f6-4121-4088-859f-b11ebfbd09bc_1" }
        };
    }

    private static JsonObject ValidationOption(string suffix) => new()
    {
        ["@odata.type"] = GraphType + "ChoiceSettingValue",
        ["value"] = RootDefinitionId + "_elevationvalidation_" + suffix,
        ["children"] = new JsonArray()
    };

}

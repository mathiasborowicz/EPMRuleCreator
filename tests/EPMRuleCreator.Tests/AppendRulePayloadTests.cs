using System.Text.Json.Nodes;
using EPMRuleCreator.Core.Models;
using EPMRuleCreator.Graph;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class AppendRulePayloadTests
{
    [Theory]
    [InlineData(SignatureSource.NotConfigured)]
    [InlineData(SignatureSource.CertificateFile)]
    [InlineData(SignatureSource.ReusableSettings)]
    public void AdditionalRule_DoesNotDuplicateTemplateReferences_OrModifyExistingRules(SignatureSource source)
    {
        var rules = CreateExistingRules(source);
        var original = rules.ToJsonString();
        var rule = new ElevationRule
        {
            Name = "Additional rule",
            FileName = "new.exe",
            FileHash = new string('A', 64),
            MinimumFileVersion = "2.0.0.0",
            SignatureSource = source,
            PublisherCertificate = new CertificateAttributes("CN=Publisher", "CN=Issuer", "thumbprint",
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(100), true)
            {
                CertificateBase64 = "AQID"
            },
            ReusableCertificateSettingId = "11111111-1111-1111-1111-111111111111"
        };

        var additional = GraphAuthenticationService.CreateRuleForAppend(rules, rule);

        // The supplied successful Intune PUT retains template references in the
        // original rule only. The reported duplicate is the file-version value ID.
        Assert.DoesNotContain("settingValueTemplateReference", additional.ToJsonString());
        Assert.DoesNotContain("settingInstanceTemplateReference", additional.ToJsonString());
        Assert.Equal(original, rules.ToJsonString());
        Assert.Contains("9d5c2849-fa98-437c-92f6-abee4e9b8a0c", rules.ToJsonString());
        Assert.Equal("2.0.0.0", FindSetting(additional, "fileversion")["simpleSettingValue"]!["value"]!.GetValue<string>());
        Assert.Equal("Additional rule", FindSetting(additional, "name")["simpleSettingValue"]!["value"]!.GetValue<string>());

        if (source == SignatureSource.CertificateFile)
            Assert.Contains("AQID", FindSetting(additional, "certificatefileupload")["simpleSettingValue"]!["value"]!.GetValue<string>());
        if (source == SignatureSource.ReusableSettings)
        {
            var reference = FindSetting(additional, "certificatepayloadwithreusablesetting")["simpleSettingValue"]!;
            Assert.Equal("#microsoft.graph.deviceManagementConfigurationReferenceSettingValue", reference["@odata.type"]!.GetValue<string>());
            Assert.Equal(rule.ReusableCertificateSettingId, reference["value"]!.GetValue<string>());
        }

        rules.Add(additional);
        var next = GraphAuthenticationService.CreateRuleForAppend(rules, rule with { Name = "Another rule" });
        Assert.DoesNotContain("settingValueTemplateReference", next.ToJsonString());
        Assert.DoesNotContain("settingInstanceTemplateReference", next.ToJsonString());
        Assert.Equal("Another rule", FindSetting(next, "name")["simpleSettingValue"]!["value"]!.GetValue<string>());
    }

    private static JsonObject FindSetting(JsonNode node, string suffix) => EnumerateObjects(node)
        .Single(item => item["settingDefinitionId"]?.GetValue<string>()?.EndsWith("_" + suffix, StringComparison.Ordinal) == true);

    private static IEnumerable<JsonObject> EnumerateObjects(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (var property in obj)
                if (property.Value is not null)
                    foreach (var descendant in EnumerateObjects(property.Value))
                        yield return descendant;
        }
        else if (node is JsonArray array)
            foreach (var item in array)
                if (item is not null)
                    foreach (var descendant in EnumerateObjects(item))
                        yield return descendant;
    }

    private static JsonArray CreateExistingRules(SignatureSource source)
    {
        var rules = JsonNode.Parse("""
            [{"children":[
              {"settingDefinitionId":"device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_name",
               "simpleSettingValue":{"value":"Existing rule"}},
              {"settingDefinitionId":"device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_filename",
               "simpleSettingValue":{"value":"old.exe"}},
              {"settingDefinitionId":"device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_filehash",
               "simpleSettingValue":{"value":"old hash"}},
              {"settingDefinitionId":"device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_fileversion",
               "settingInstanceTemplateReference":{"settingInstanceTemplateId":"4a36432a-ad41-4ea6-b69f-05655afc43d2"},
               "simpleSettingValue":{"value":"1.0.0.0","settingValueTemplateReference":{"settingValueTemplateId":"9d5c2849-fa98-437c-92f6-abee4e9b8a0c"}}},
              {"settingDefinitionId":"device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_ruletype",
               "choiceSettingValue":{"value":"device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_self","children":[]}},
              {"settingDefinitionId":"device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_childprocessbehavior",
               "choiceSettingValue":{"value":"device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_allowrunelevatedrulerequired","children":[]}}
            ]}]
            """)!.AsArray();

        // The theory exercises both certificate configurations. Their nested
        // references are copied again during PopulateRule and must be cleaned too.
        rules[0]!["children"]!.AsArray().Add(CreateCertificateSource(reusable: source == SignatureSource.ReusableSettings));
        return rules;
    }

    private static JsonObject CreateCertificateSource(bool reusable)
    {
        const string prefix = "device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_";
        return new JsonObject
        {
            ["settingDefinitionId"] = prefix + "signaturesource",
            ["settingInstanceTemplateReference"] = new JsonObject { ["settingInstanceTemplateId"] = "source-instance-template" },
            ["choiceSettingValue"] = new JsonObject
            {
                ["value"] = prefix + "signaturesource_" + (reusable ? "0" : "1"),
                ["settingValueTemplateReference"] = new JsonObject { ["settingValueTemplateId"] = "source-value-template" },
                ["children"] = new JsonArray(
                    new JsonObject
                    {
                        ["settingDefinitionId"] = prefix + (reusable ? "certificatepayloadwithreusablesetting" : "certificatefileupload"),
                        ["simpleSettingValue"] = new JsonObject
                        {
                            ["@odata.type"] = reusable
                                ? "#microsoft.graph.deviceManagementConfigurationReferenceSettingValue"
                                : "#microsoft.graph.deviceManagementConfigurationStringSettingValue",
                            ["value"] = "old-certificate",
                            ["settingValueTemplateReference"] = new JsonObject { ["settingValueTemplateId"] = "certificate-value-template" }
                        }
                    },
                    new JsonObject
                    {
                        ["settingDefinitionId"] = prefix + "certificatetype",
                        ["settingInstanceTemplateReference"] = new JsonObject { ["settingInstanceTemplateId"] = "certificate-type-instance-template" },
                        ["choiceSettingValue"] = new JsonObject { ["value"] = prefix + "publisher", ["children"] = new JsonArray() }
                    })
            }
        };
    }
}

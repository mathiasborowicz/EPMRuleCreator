using System.Text.Json.Nodes;
using IntuneEpmRuleCreator.Core.Models;
using IntuneEpmRuleCreator.Core.Services;

namespace IntuneEpmRuleCreator.Graph;

internal static class NewElevationPolicyPayload
{
    // Microsoft EPM schema identifiers from the supplied Intune admin-center PUT;
    // these identify the policy template, not an application or tenant.
    internal const string TemplateId = "cff02aad-51b1-498d-83ad-81161a393f56_1";
    internal const string RootDefinitionId = "device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}";
    private const string RootInstanceTemplateId = "ee3d2e5f-6b3d-4cb1-af9b-37b02d3dbae2";
    private const string GraphType = "#microsoft.graph.deviceManagementConfiguration";

    internal static JsonObject Build(string name, string? description, ElevationRule rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var validation = new RuleValidationService().Validate(rule, existingRuleCount: 0, hasPolicy: true);
        if (!validation.CanSubmit)
            throw new ArgumentException(string.Join(Environment.NewLine,
                validation.Issues.Where(issue => issue.IsBlocking).Select(issue => issue.Message)), nameof(rule));

        // Construct the initial rule directly; no existing policy or rule is needed.
        var children = new JsonArray();
        AddSimple(children, "name", rule.Name);
        AddSimple(children, "description", rule.Description);
        AddSimple(children, "filename", rule.FileName);
        AddSimple(children, "filehash", rule.FileHash);
        AddSimple(children, "filepath", rule.FilePath);
        AddSimple(children, "productname", rule.ProductName);
        AddSimple(children, "filedescription", rule.FileDescription is { Length: > 50 } text ? text[..50] : rule.FileDescription);
        AddSimple(children, "internalname", rule.InternalName);
        AddSimple(children, "fileversion", rule.MinimumFileVersion);

        children.Add(Choice("ruletype", RootDefinitionId + "_self"));
        GraphAuthenticationService.SetElevationType(children, rule);
        if (rule.ChildProcessBehavior != ChildProcessBehavior.NotConfigured)
            children.Add(Choice("childprocessbehavior", RootDefinitionId + "_" + GraphAuthenticationService.GetChildProcessOption(rule.ChildProcessBehavior)));
        GraphAuthenticationService.SetRestrictArguments(children, rule);
        children.Add(Choice("appliesto", RootDefinitionId + "_allusers"));

        if (rule.SignatureSource != SignatureSource.NotConfigured)
        {
            var source = Choice("signaturesource", RootDefinitionId + "_signaturesource_" +
                (rule.SignatureSource == SignatureSource.CertificateFile ? "1" : "0"));
            var certificateChildren = source["choiceSettingValue"]!["children"]!.AsArray();
            certificateChildren.Add(Choice("certificatetype", RootDefinitionId + "_" + GraphAuthenticationService.GetCertificateTypeOption(rule.CertificateType)));
            if (rule.SignatureSource == SignatureSource.CertificateFile)
            {
                var certificate = rule.PublisherCertificate!;
                if (string.IsNullOrWhiteSpace(certificate.CertificateBase64))
                    throw new ArgumentException("The selected certificate data is unavailable. Analyze the file or choose a certificate again.", nameof(rule));
                var pem = "-----BEGIN CERTIFICATE-----\r\n" +
                    Convert.ToBase64String(Convert.FromBase64String(certificate.CertificateBase64), Base64FormattingOptions.InsertLineBreaks) +
                    "\r\n-----END CERTIFICATE-----\r\n";
                AddSimple(certificateChildren, "certificatefileupload", pem);
            }
            else
                AddSimple(certificateChildren, "certificatepayloadwithreusablesetting", rule.ReusableCertificateSettingId,
                    GraphType + "ReferenceSettingValue");
            children.Add(source);
        }

        var firstRule = new JsonObject { ["children"] = children };
        GraphAuthenticationService.RemoveTemplateReferences(firstRule);
        return new JsonObject
        {
            ["@odata.type"] = GraphType + "Policy",
            ["name"] = name.Trim(),
            ["description"] = description ?? string.Empty,
            ["platforms"] = "windows10",
            ["technologies"] = "endpointPrivilegeManagement",
            ["creationSource"] = null,
            ["roleScopeTagIds"] = new JsonArray("0"),
            ["templateReference"] = new JsonObject { ["templateId"] = TemplateId },
            ["settings"] = new JsonArray(new JsonObject
            {
                ["@odata.type"] = GraphType + "Setting",
                ["settingInstance"] = new JsonObject
                {
                    ["@odata.type"] = GraphType + "GroupSettingCollectionInstance",
                    ["settingDefinitionId"] = RootDefinitionId,
                    ["settingInstanceTemplateReference"] = new JsonObject { ["settingInstanceTemplateId"] = RootInstanceTemplateId },
                    ["groupSettingCollectionValue"] = new JsonArray(firstRule)
                }
            })
        };
    }

    private static void AddSimple(JsonArray children, string suffix, string? value,
        string valueType = GraphType + "StringSettingValue")
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        children.Add(new JsonObject
        {
            ["@odata.type"] = GraphType + "SimpleSettingInstance",
            ["settingDefinitionId"] = RootDefinitionId + "_" + suffix,
            ["simpleSettingValue"] = new JsonObject { ["@odata.type"] = valueType, ["value"] = value }
        });
    }

    private static JsonObject Choice(string suffix, string itemId) => new()
    {
        ["@odata.type"] = GraphType + "ChoiceSettingInstance",
        ["settingDefinitionId"] = RootDefinitionId + "_" + suffix,
        ["choiceSettingValue"] = new JsonObject
        {
            ["@odata.type"] = GraphType + "ChoiceSettingValue",
            ["value"] = itemId,
            ["children"] = new JsonArray()
        }
    };
}

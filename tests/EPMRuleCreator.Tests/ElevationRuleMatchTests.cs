using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EPMRuleCreator.Core.Models;
using EPMRuleCreator.Graph;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class ElevationRuleMatchTests
{
    [Fact]
    public void FindRuleMatches_MatchesHashAndReportsNormalizedVersion()
    {
        var rules = Rules();
        var matches = GraphAuthenticationService.FindRuleMatches(rules,
            new ElevationRule { FileHash = "aa-bb", MinimumFileVersion = "1.2", Name = "new" });

        var match = Assert.Single(matches);
        Assert.Equal(0, match.Index);
        Assert.Equal("Existing", match.Name);
        Assert.Equal("old.exe", match.FileName);
        Assert.Equal("1.2", match.FileVersion);
        Assert.Equal("AA BB", match.FileHash);
        Assert.Equal("File hash matches", match.MatchReason);
        Assert.True(match.VersionMatches);
        Assert.Equal("Version matches (1.2.0.0)", match.VersionDisplayReason);
    }

    [Fact]
    public void FindRuleMatches_ReportsDifferentVersionWithoutHidingHashMatch()
    {
        var matches = GraphAuthenticationService.FindRuleMatches(Rules(),
            new ElevationRule { FileHash = "AABB", MinimumFileVersion = "2", Name = "new" });

        var match = Assert.Single(matches);
        Assert.False(match.VersionMatches);
        Assert.Equal("Version differs (stored 1.2.0.0, requested 2.0.0.0)", match.VersionDisplayReason);
    }

    [Fact]
    public void ValidateSelectedRuleMatch_RejectsRuleChangedAfterUserSelection()
    {
        var rules = Rules();
        var incoming = new ElevationRule { FileHash = "AABB", MinimumFileVersion = "1.2", Name = "Updated" };
        var selected = Assert.Single(GraphAuthenticationService.FindRuleMatches(rules, incoming));
        var nameSetting = rules[0]!["children"]!.AsArray().Single(item =>
            item!["settingDefinitionId"]!.GetValue<string>().EndsWith("_name", StringComparison.Ordinal))!;
        nameSetting["simpleSettingValue"]!["value"] = "Changed by another editor";

        Assert.Throws<InvalidOperationException>(() =>
            GraphAuthenticationService.ValidateSelectedRuleMatch(rules, incoming, selected));
    }

    [Fact]
    public void ValidateSelectedRuleMatch_AllowsUnchangedSelectedRule()
    {
        var rules = Rules();
        var incoming = new ElevationRule { FileHash = "AABB", MinimumFileVersion = "1.2", Name = "Updated" };
        var selected = Assert.Single(GraphAuthenticationService.FindRuleMatches(rules, incoming));

        GraphAuthenticationService.ValidateSelectedRuleMatch(rules, incoming, selected);
    }

    [Fact]
    public void ValidateSelectedRuleMatch_RejectsSelectedRuleMovedToAnotherIndex()
    {
        var rules = Rules();
        var incoming = new ElevationRule { FileHash = "AABB", MinimumFileVersion = "1.2", Name = "Updated" };
        var selected = Assert.Single(GraphAuthenticationService.FindRuleMatches(rules, incoming));
        var otherRule = RuleTemplate("Other");
        var otherHashSetting = otherRule["children"]!.AsArray().Single(item =>
            item!["settingDefinitionId"]!.GetValue<string>().EndsWith("_filehash", StringComparison.Ordinal))!;
        otherHashSetting["simpleSettingValue"]!["value"] = "CC DD";
        rules.Insert(0, otherRule);

        Assert.Throws<InvalidOperationException>(() =>
            GraphAuthenticationService.ValidateSelectedRuleMatch(rules, incoming, selected));
    }

    [Fact]
    public void FindRuleMatches_MatchesReusableCertificateByReferenceId()
    {
        var rules = Rules();
        var children = rules[0]!["children"]!.AsArray();
        children.Add(new JsonObject
        {
            ["settingDefinitionId"] = Prefix + "signaturesource",
            ["choiceSettingValue"] = new JsonObject
            {
                ["value"] = Prefix + "signaturesource_0",
                ["children"] = new JsonArray(new JsonObject
                {
                    ["settingDefinitionId"] = Prefix + "certificatepayloadwithreusablesetting",
                    ["simpleSettingValue"] = new JsonObject { ["value"] = "setting-id-1" }
                })
            }
        });

        var matches = GraphAuthenticationService.FindRuleMatches(rules,
            new ElevationRule { SignatureSource = SignatureSource.ReusableSettings, ReusableCertificateSettingId = "setting-id-1" });

        var match = Assert.Single(matches);
        Assert.Equal("reusable:setting-id-1", match.Signature);
        Assert.Equal("Signing certificate matches", match.MatchReason);
    }

    [Fact]
    public void FindRuleMatches_UsesX509ThumbprintForUploadedCertificatePem()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=EPM test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pem = certificate.ExportCertificatePem();
        var rules = Rules();
        rules[0]!["children"]!.AsArray().Add(new JsonObject
        {
            ["settingDefinitionId"] = Prefix + "signaturesource",
            ["choiceSettingValue"] = new JsonObject
            {
                ["value"] = Prefix + "signaturesource_1",
                ["children"] = new JsonArray(new JsonObject
                {
                    ["settingDefinitionId"] = Prefix + "certificatefileupload",
                    ["simpleSettingValue"] = new JsonObject { ["value"] = pem }
                })
            }
        });

        var match = Assert.Single(GraphAuthenticationService.FindRuleMatches(rules,
            new ElevationRule
            {
                SignatureSource = SignatureSource.CertificateFile,
                PublisherCertificate = new CertificateAttributes("CN=EPM test", "CN=EPM test", "ignored",
                    DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), true)
                {
                    CertificateBase64 = Convert.ToBase64String(certificate.Export(X509ContentType.Cert))
                }
            }));

        Assert.Equal(certificate.Thumbprint, match.Signature);
        Assert.Equal("Signing certificate matches", match.MatchReason);
    }

    [Fact]
    public void CreateRuleForUpdate_ReplacesOnlySelectedIndexAndUsesNewValues()
    {
        var rules = Rules();
        rules.Add(RuleTemplate("Second"));
        var untouched = rules[0]!.ToJsonString();

        var updated = GraphAuthenticationService.CreateRuleForUpdate(rules, 1,
            new ElevationRule { Name = "Updated", FileName = "new.exe", FileHash = "CCDD", MinimumFileVersion = "3" });

        Assert.Equal(untouched, rules[0]!.ToJsonString());
        Assert.Equal("Updated", Value(updated, "name"));
        Assert.Equal("new.exe", Value(updated, "filename"));
        Assert.Equal("CCDD", Value(updated, "filehash"));
        Assert.Equal("3.0.0.0", Value(updated, "fileversion"));
        Assert.Throws<ArgumentOutOfRangeException>(() => GraphAuthenticationService.CreateRuleForUpdate(rules, 3,
            new ElevationRule { Name = "Invalid" }));
    }

    [Fact]
    public void ValidateSelectedRuleMatch_RejectsWhenIndexNowPointsToAnotherRule()
    {
        var rule = new ElevationRule { FileHash = "AABB", MinimumFileVersion = "1.2" };
        var expected = Assert.Single(GraphAuthenticationService.FindRuleMatches(Rules(), rule));
        var currentRules = new JsonArray(RuleTemplate("Different rule"), RuleTemplate("Existing"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GraphAuthenticationService.ValidateSelectedRuleMatch(currentRules, rule, expected));

        Assert.Contains("selected rule changed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSelectedRuleMatch_RejectsWhenMatchedRuleWasEditedInPlace()
    {
        var rule = new ElevationRule { FileHash = "AABB", MinimumFileVersion = "1.2" };
        var expected = Assert.Single(GraphAuthenticationService.FindRuleMatches(Rules(), rule));
        var currentRules = Rules();
        currentRules[0]! ["children"]![0]!["simpleSettingValue"]!["value"] = "Renamed concurrently";

        Assert.Throws<InvalidOperationException>(() =>
            GraphAuthenticationService.ValidateSelectedRuleMatch(currentRules, rule, expected));
    }

    [Fact]
    public void ValidateSelectedRuleMatch_AllowsUnchangedSelectedMatch()
    {
        var rule = new ElevationRule { FileHash = "AABB", MinimumFileVersion = "1.2" };
        var rules = Rules();
        var expected = Assert.Single(GraphAuthenticationService.FindRuleMatches(rules, rule));

        GraphAuthenticationService.ValidateSelectedRuleMatch(rules, rule, expected);
    }

    private const string Prefix = "device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}_";

    private static JsonArray Rules() => new(RuleTemplate("Existing"));

    private static JsonObject RuleTemplate(string name) => new()
    {
        ["children"] = new JsonArray(
            Setting("name", name), Setting("filename", "old.exe"), Setting("filehash", "AA BB"), Setting("fileversion", "1.2"),
            new JsonObject { ["settingDefinitionId"] = Prefix + "ruletype", ["choiceSettingValue"] = new JsonObject { ["value"] = Prefix + "self", ["children"] = new JsonArray() } },
            new JsonObject { ["settingDefinitionId"] = Prefix + "childprocessbehavior", ["choiceSettingValue"] = new JsonObject { ["value"] = Prefix + "allowrunelevatedrulerequired", ["children"] = new JsonArray() } })
    };

    private static JsonObject Setting(string suffix, string value) => new()
    {
        ["settingDefinitionId"] = Prefix + suffix,
        ["simpleSettingValue"] = new JsonObject { ["value"] = value }
    };

    private static string? Value(JsonObject rule, string suffix) => rule["children"]!.AsArray().OfType<JsonObject>()
        .Single(item => item["settingDefinitionId"]!.GetValue<string>().EndsWith("_" + suffix, StringComparison.Ordinal))
        ["simpleSettingValue"]?["value"]?.GetValue<string>();
}

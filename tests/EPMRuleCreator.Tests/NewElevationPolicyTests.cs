using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EPMRuleCreator.Core.Models;
using EPMRuleCreator.Graph;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class NewElevationPolicyTests
{
    public static IEnumerable<object[]> RuleVariants() =>
        from elevation in Enum.GetValues<ElevationType>()
        from source in Enum.GetValues<SignatureSource>()
        select new object[] { elevation, source };

    [Theory]
    [MemberData(nameof(RuleVariants))]
    public void NewPolicy_ContainsItsFirstRule_WithoutAnyExistingPolicy(ElevationType elevation, SignatureSource source)
    {
        var rule = Rule() with { ElevationType = elevation, SignatureSource = source };
        var body = NewElevationPolicyPayload.Build("  New EPM policy  ", "Description", rule);

        Assert.Equal("New EPM policy", body["name"]!.GetValue<string>());
        Assert.Equal("windows10", body["platforms"]!.GetValue<string>());
        Assert.Equal("endpointPrivilegeManagement", body["technologies"]!.GetValue<string>());
        Assert.Equal("cff02aad-51b1-498d-83ad-81161a393f56_1", body["templateReference"]!["templateId"]!.GetValue<string>());
        Assert.Single(body["templateReference"]!.AsObject()); // No read-only display/family properties.
        Assert.False(body.ContainsKey("id"));
        Assert.False(body.ContainsKey("isAssigned"));
        Assert.False(body.ContainsKey("assignments"));
        Assert.Equal("0", body["roleScopeTagIds"]![0]!.GetValue<string>());
        var setting = Assert.Single(body["settings"]!.AsArray())!;
        var root = setting["settingInstance"]!;
        Assert.Equal("ee3d2e5f-6b3d-4cb1-af9b-37b02d3dbae2",
            root["settingInstanceTemplateReference"]!["settingInstanceTemplateId"]!.GetValue<string>());
        var firstRule = Assert.Single(root["groupSettingCollectionValue"]!.AsArray())!;
        Assert.DoesNotContain("settingValueTemplateReference", firstRule.ToJsonString());
        Assert.DoesNotContain("settingInstanceTemplateReference", firstRule.ToJsonString());
        var children = firstRule["children"]!.AsArray();
        Assert.Equal(rule.Name, Find(children, "name")!["simpleSettingValue"]!["value"]!.GetValue<string>());
        var expectedType = elevation switch
        {
            ElevationType.UserConfirmed => "self",
            ElevationType.CurrentUser => "userconfirmeduser",
            ElevationType.Automatic => "automatic",
            ElevationType.Deny => "deny",
            _ => "supportarbitrated"
        };
        Assert.Equal(NewElevationPolicyPayload.RootDefinitionId + "_" + expectedType,
            Find(children, "ruletype")!["choiceSettingValue"]!["value"]!.GetValue<string>());
        Assert.Equal(NewElevationPolicyPayload.RootDefinitionId + "_allowrunelevatedrulerequired",
            Find(children, "childprocessbehavior")!["choiceSettingValue"]!["value"]!.GetValue<string>());
        var signature = Find(children, "signaturesource");
        if (source == SignatureSource.NotConfigured)
            Assert.Null(signature);
        else
        {
            var certificateChildren = signature!["choiceSettingValue"]!["children"]!.AsArray();
            if (source == SignatureSource.CertificateFile)
                Assert.Equal("-----BEGIN CERTIFICATE-----\r\nAQID\r\n-----END CERTIFICATE-----\r\n",
                    Find(certificateChildren, "certificatefileupload")!["simpleSettingValue"]!["value"]!.GetValue<string>());
            else
            {
                var value = Find(certificateChildren, "certificatepayloadwithreusablesetting")!["simpleSettingValue"]!;
                Assert.Equal("#microsoft.graph.deviceManagementConfigurationReferenceSettingValue", value["@odata.type"]!.GetValue<string>());
                Assert.Equal(rule.ReusableCertificateSettingId, value["value"]!.GetValue<string>());
            }
        }
    }

    [Fact]
    public void OptionalEmptyFields_AndUnselectedValidation_AreOmitted()
    {
        var body = NewElevationPolicyPayload.Build("Policy", null, Rule());
        var children = body["settings"]![0]!["settingInstance"]!["groupSettingCollectionValue"]![0]!["children"]!.AsArray();
        foreach (var suffix in new[] { "description", "fileversion", "internalname", "filedescription", "productname", "filepath", "arguments" })
            Assert.Null(Find(children, suffix));
        Assert.Empty(Find(children, "ruletype")!["choiceSettingValue"]!["children"]!.AsArray());
    }

    [Fact]
    public void CertificateOnlyRule_DoesNotRequireFileNameOrHash_AndPreservesAuthorityType()
    {
        var body = NewElevationPolicyPayload.Build("Policy", null, Rule() with
        {
            SignatureSource = SignatureSource.ReusableSettings,
            CertificateType = CertificateType.CertificateAuthority,
            FileName = string.Empty,
            FileHash = null
        });
        var children = body["settings"]![0]!["settingInstance"]!["groupSettingCollectionValue"]![0]!["children"]!.AsArray();
        Assert.Null(Find(children, "filename"));
        Assert.Null(Find(children, "filehash"));
        var sourceChildren = Find(children, "signaturesource")!["choiceSettingValue"]!["children"]!.AsArray();
        Assert.Equal(NewElevationPolicyPayload.RootDefinitionId + "_issuingauthority",
            Find(sourceChildren, "certificatetype")!["choiceSettingValue"]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Creation_SendsOnePostWithTheRule_AndReturnsTheCreatedId()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created,
            """{"id":"11111111-1111-1111-1111-111111111111","name":"New policy"}""");
        using var client = new HttpClient(handler);

        var created = await new NewElevationPolicyWriter(client).CreateAsync("test-token", "New policy", null, Rule());

        Assert.Equal("11111111-1111-1111-1111-111111111111", created.Id);
        Assert.False(created.IsAssigned);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://graph.microsoft.com/beta/deviceManagement/configurationPolicies", handler.Url);
        var body = JsonNode.Parse(handler.Body!)!;
        Assert.Single(body["settings"]![0]!["settingInstance"]!["groupSettingCollectionValue"]!.AsArray());
    }

    [Theory]
    [InlineData(400, "{\"error\":{\"message\":\"Invalid setting\"}}", "Invalid setting")]
    [InlineData(201, "{}", "Check Intune before retrying")]
    [InlineData(201, "{\"id\":123}", "Check Intune before retrying")]
    [InlineData(201, "not json", "Check Intune before retrying")]
    public async Task CreationErrors_AreReportedWithoutAnAutomaticSecondPost(int status, string response, string expected)
    {
        var handler = new RecordingHandler((HttpStatusCode)status, response);
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            new NewElevationPolicyWriter(client).CreateAsync("test-token", "Policy", null, Rule()));
        Assert.Contains(expected, exception.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task InvalidDraft_DoesNotSendARequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, "{}");
        using var client = new HttpClient(handler);
        var writer = new NewElevationPolicyWriter(client);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => writer.CreateAsync("test-token", " ", null, Rule()));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => writer.CreateAsync("test-token", "Policy", null, Rule() with { FileHash = "bad" }));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task InterruptedCreation_IsNotAutomaticallyRetried()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, "{}", interrupt: true);
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NewElevationPolicyWriter(client).CreateAsync("test-token", "Policy", null, Rule()));
        Assert.Contains("Check Intune before retrying", exception.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void InitialRule_ContainsSelectedArgumentsAndFourPartVersion()
    {
        var body = NewElevationPolicyPayload.Build("Policy", null, Rule() with
        {
            MinimumFileVersion = "1.2.3.4",
            FileDescription = new string('D', 60),
            RestrictArguments = RestrictArgumentsMode.Allow,
            ArgumentList = ["/silent", "/norestart"],
            RequireBusinessJustification = true,
            RequireWindowsAuthentication = true
        });
        var children = body["settings"]![0]!["settingInstance"]!["groupSettingCollectionValue"]![0]!["children"]!.AsArray();
        Assert.Equal("1.2.3.4", Find(children, "fileversion")!["simpleSettingValue"]!["value"]!.GetValue<string>());
        Assert.Equal(new string('D', 50), Find(children, "filedescription")!["simpleSettingValue"]!["value"]!.GetValue<string>());
        var arguments = Find(children, "arguments")!["choiceSettingValue"]!["children"]![0]!["simpleSettingCollectionValue"]!.AsArray();
        Assert.Equal(new[] { "/silent", "/norestart" }, arguments.Select(value => value!["value"]!.GetValue<string>()).ToArray());
        Assert.Equal(2, Find(children, "ruletype")!["choiceSettingValue"]!["children"]![0]!["choiceSettingCollectionValue"]!.AsArray().Count);
    }

    private static JsonNode? Find(JsonArray children, string suffix) => children.FirstOrDefault(child =>
        child?["settingDefinitionId"]?.GetValue<string>()?.EndsWith("_" + suffix, StringComparison.Ordinal) == true);

    [Theory]
    [InlineData("11", "11.0.0.0")]
    [InlineData("11.1", "11.1.0.0")]
    [InlineData("11.1.2", "11.1.2.0")]
    public void NewPolicySendsFourPartVersion(string input, string expected)
    {
        var rule = Rule() with { MinimumFileVersion = input };
        var body = NewElevationPolicyPayload.Build("New policy", null, rule);
        var children = body["settings"]![0]!["settingInstance"]!["groupSettingCollectionValue"]![0]!["children"]!.AsArray();
        Assert.Equal(expected, Find(children, "fileversion")!["simpleSettingValue"]!["value"]!.GetValue<string>());
        Assert.Equal(input, rule.MinimumFileVersion);
    }

    private static ElevationRule Rule() => new()
    {
        Name = "First rule", FileName = "app.exe", FileHash = new string('A', 64),
        ReusableCertificateSettingId = "22222222-2222-2222-2222-222222222222",
        PublisherCertificate = new CertificateAttributes("CN=Publisher", "CN=Issuer", "thumbprint",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(100), true) { CertificateBase64 = "AQID" }
    };

    private sealed class RecordingHandler(HttpStatusCode status, string response, bool interrupt = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Url { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (interrupt) throw new HttpRequestException("Connection interrupted.");
            Method = request.Method;
            Url = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}

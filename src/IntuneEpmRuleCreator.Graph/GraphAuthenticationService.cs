using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntuneEpmRuleCreator.Core.Models;
using IntuneEpmRuleCreator.Core.Services;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace IntuneEpmRuleCreator.Graph;

public sealed record GraphUserProfile(string DisplayName, string UserPrincipalName, string TenantId);
public sealed record ElevationRulePolicy(
    string Id,
    string Name,
    string? Description,
    string? Platforms,
    string? Technologies,
    DateTimeOffset? LastModifiedDateTime,
    int SettingCount,
    bool IsAssigned);
public sealed record ElevationRuleCollectionInfo(int RuleCount, int MaximumRuleCount);
public sealed record EpmReusableCertificateSetting(string Id, string DisplayName, string? Description, DateTimeOffset? LastModifiedDateTime);

/// <summary>Interactive delegated authentication for the Microsoft Graph global cloud.</summary>
public sealed class GraphAuthenticationService : IAsyncDisposable
{
    private static readonly string[] Scopes = ["User.Read", "DeviceManagementConfiguration.ReadWrite.All"];
    private const string ElevationRulesSettingDefinitionId = "device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}";
    private const string ReusableCertificateSettingDefinitionId = "device_vendor_msft_policy_privilegemanagement_reusablesettings_certificatefile";
    private const int DefaultMaximumRuleCount = 100;
    private readonly HttpClient _httpClient = new();
    private IPublicClientApplication? _client;
    private MsalCacheHelper? _cacheHelper;
    private IAccount? _account;
    private IntPtr _parentWindow;

    public async Task<GraphUserProfile> SignInAsync(string clientId, string tenantId, IntPtr parentWindow, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(clientId, out _))
            throw new ArgumentException("Enter the application (client) ID from the Entra app registration.", nameof(clientId));
        var authorityTenant = string.IsNullOrWhiteSpace(tenantId) ? "organizations" : tenantId.Trim();
        if (authorityTenant != "organizations" && !Guid.TryParse(authorityTenant, out _))
            throw new ArgumentException("Tenant must be 'organizations' or a tenant ID (GUID).", nameof(tenantId));

        await DisposeClientAsync();
        _client = PublicClientApplicationBuilder.Create(clientId.Trim())
            .WithAuthority($"https://login.microsoftonline.com/{authorityTenant}")
            .WithRedirectUri("http://localhost")
            .Build();

        var cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntuneEpmRuleCreator");
        Directory.CreateDirectory(cacheDirectory);
        var storageProperties = new StorageCreationPropertiesBuilder("msal.cache", cacheDirectory).Build();
        _cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        _cacheHelper.VerifyPersistence();
        _cacheHelper.RegisterCache(_client.UserTokenCache);

        var authResult = await _client.AcquireTokenInteractive(Scopes)
            .WithPrompt(Prompt.SelectAccount)
            .WithUseEmbeddedWebView(false)
            .WithParentActivityOrWindow(parentWindow)
            .ExecuteAsync(cancellationToken);
        _account = authResult.Account;
        _parentWindow = parentWindow;
        return await GetUserProfileAsync(authResult.AccessToken, authResult.TenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<ElevationRulePolicy>> GetElevationRulePoliciesAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null || _account is null)
            throw new InvalidOperationException("Sign in to Microsoft Graph before loading EPM policies.");

        var accessToken = await GetAccessTokenAsync(cancellationToken);
        var filter = Uri.EscapeDataString("templateReference/templateFamily eq 'endpointSecurityEndpointPrivilegeManagement'");
        var select = "id,name,description,platforms,lastModifiedDateTime,technologies,settingCount,isAssigned";
        var nextLink = $"https://graph.microsoft.com/beta/deviceManagement/configurationPolicies?$top=25&$filter={filter}&$select={Uri.EscapeDataString(select)}";
        var policies = new List<ElevationRulePolicy>();

        while (!string.IsNullOrEmpty(nextLink))
        {
            var requestUri = new Uri(nextLink);
            if (requestUri.Scheme != Uri.UriSchemeHttps || requestUri.Host != "graph.microsoft.com")
                throw new InvalidOperationException("Graph returned an unexpected paging URL; the request was stopped.");

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            if (root.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in values.EnumerateArray())
                {
                    policies.Add(new ElevationRulePolicy(
                        GetString(item, "id") ?? string.Empty,
                        GetString(item, "name") ?? "(Unnamed policy)",
                        GetString(item, "description"),
                        GetString(item, "platforms"),
                        GetString(item, "technologies"),
                        GetDateTimeOffset(item, "lastModifiedDateTime"),
                        item.TryGetProperty("settingCount", out var settingCount) && settingCount.TryGetInt32(out var count) ? count : 0,
                        item.TryGetProperty("isAssigned", out var isAssigned) && isAssigned.ValueKind == JsonValueKind.True));
                }
            }

            nextLink = root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
        }

        return policies;
    }

    public async Task<IReadOnlyList<EpmReusableCertificateSetting>> GetEpmReusableCertificateSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null || _account is null)
            throw new InvalidOperationException("Sign in to Microsoft Graph before loading reusable settings.");

        var accessToken = await GetAccessTokenAsync(cancellationToken);
        var select = Uri.EscapeDataString("id,displayName,description,lastModifiedDateTime,settingDefinitionId");
        var nextLink = $"https://graph.microsoft.com/beta/deviceManagement/reusablePolicySettings?$select={select}&$top=100";
        var reusableSettings = new List<EpmReusableCertificateSetting>();

        while (!string.IsNullOrEmpty(nextLink))
        {
            var requestUri = new Uri(nextLink);
            if (requestUri.Scheme != Uri.UriSchemeHttps || requestUri.Host != "graph.microsoft.com")
                throw new InvalidOperationException("Graph returned an unexpected paging URL; the request was stopped.");

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            if (root.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in values.EnumerateArray())
                {
                    if (!string.Equals(GetString(item, "settingDefinitionId"), ReusableCertificateSettingDefinitionId, StringComparison.Ordinal))
                        continue;

                    reusableSettings.Add(new EpmReusableCertificateSetting(
                        GetString(item, "id") ?? string.Empty,
                        GetString(item, "displayName") ?? "(Unnamed reusable setting)",
                        GetString(item, "description"),
                        GetDateTimeOffset(item, "lastModifiedDateTime")));
                }
            }

            nextLink = root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
        }

        return reusableSettings;
    }

    public async Task<ElevationRuleCollectionInfo> GetElevationRuleCollectionInfoAsync(
        ElevationRulePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (_client is null || _account is null)
            throw new InvalidOperationException("Sign in to Microsoft Graph before loading policy settings.");

        var accessToken = await GetAccessTokenAsync(cancellationToken);
        var escapedPolicyId = Uri.EscapeDataString(policy.Id);
        var nextLink = $"https://graph.microsoft.com/beta/deviceManagement/configurationPolicies('{escapedPolicyId}')/settings?$expand=settingDefinitions&$top=1000";
        var foundElevationSetting = false;
        var ruleCount = 0;
        var maximumRuleCount = DefaultMaximumRuleCount;

        while (!string.IsNullOrEmpty(nextLink))
        {
            var requestUri = new Uri(nextLink);
            if (requestUri.Scheme != Uri.UriSchemeHttps || requestUri.Host != "graph.microsoft.com")
                throw new InvalidOperationException("Graph returned an unexpected paging URL; the request was stopped.");

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;

            if (root.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
            {
                foreach (var setting in values.EnumerateArray())
                {
                    if (!setting.TryGetProperty("settingInstance", out var instance) ||
                        !string.Equals(GetString(instance, "settingDefinitionId"), ElevationRulesSettingDefinitionId, StringComparison.Ordinal))
                        continue;

                    if (!instance.TryGetProperty("groupSettingCollectionValue", out var rules) || rules.ValueKind != JsonValueKind.Array)
                        throw new InvalidOperationException("The Elevation Rules setting was returned in an unexpected format; rule count is unavailable.");

                    foundElevationSetting = true;
                    ruleCount += rules.GetArrayLength();

                    if (setting.TryGetProperty("settingDefinitions", out var definitions) && definitions.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var definition in definitions.EnumerateArray())
                        {
                            if (string.Equals(GetString(definition, "id"), ElevationRulesSettingDefinitionId, StringComparison.Ordinal) &&
                                definition.TryGetProperty("maximumCount", out var maximum) && maximum.TryGetInt32(out var parsedMaximum))
                            {
                                maximumRuleCount = parsedMaximum;
                                break;
                            }
                        }
                    }
                }
            }

            nextLink = root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
        }

        if (!foundElevationSetting && policy.SettingCount > 0)
            throw new InvalidOperationException("The policy has settings, but the Elevation Rules collection was not found. Its rule count cannot be safely determined.");

        return new ElevationRuleCollectionInfo(ruleCount, maximumRuleCount);
    }

    public async Task<int> AppendElevationRuleAsync(
        ElevationRulePolicy policy,
        ElevationRule rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(rule);
        if (_client is null || _account is null)
            throw new InvalidOperationException("Sign in to Microsoft Graph before writing a rule.");
        if (string.IsNullOrWhiteSpace(policy.Id) || !Guid.TryParse(policy.Id, out _))
            throw new InvalidOperationException("The selected policy has no valid Graph ID.");

        var token = await GetAccessTokenAsync(cancellationToken);
        var policyDocument = await GetPolicyDocumentForWriteAsync(policy, token, cancellationToken);
        var settings = await GetPolicySettingsForWriteAsync(policy, token, cancellationToken);
        var setting = settings.SingleOrDefault(item =>
            string.Equals(item["settingInstance"]?["settingDefinitionId"]?.GetValue<string>(), ElevationRulesSettingDefinitionId, StringComparison.Ordinal));
        if (setting is null)
            throw new InvalidOperationException("This policy does not yet have an Elevation Rules setting to update. Add an initial rule in Intune first; this app only appends to an existing rules collection.");

        var settingInstance = setting["settingInstance"] as JsonObject
            ?? throw new InvalidOperationException("The policy's Elevation Rules setting has an unexpected format.");
        var rules = settingInstance["groupSettingCollectionValue"] as JsonArray
            ?? throw new InvalidOperationException("The policy's Elevation Rules collection is unavailable.");
        var maximum = GetMaximumRuleCount(setting, DefaultMaximumRuleCount);
        if (rules.Count >= maximum)
            throw new InvalidOperationException($"The policy already contains {rules.Count} rules, reaching its maximum of {maximum}.");
        if (rules.OfType<JsonObject>().Any(existing =>
                string.Equals(GetSimpleValue(existing, "name"), rule.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A rule named '{rule.Name}' already exists in this policy.");

        var template = FindRuleTemplate(rules, rule);
        var newRule = (JsonObject)template.DeepClone();
        PopulateRule(newRule, rule, rules);
        rules.Add(newRule);

        var settingId = setting["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(settingId))
            throw new InvalidOperationException("Graph did not return an ID for the Elevation Rules setting.");

        // The Intune admin center saves EPM rules by replacing the policy with PUT,
        // including settings in the policy body. The settings navigation PATCH route
        // is documented but is not routed by the EPM backend in the user's tenant.
        var policySettings = new JsonArray();
        foreach (var policySetting in settings)
        {
            var instance = policySetting["settingInstance"]?.DeepClone()
                ?? throw new InvalidOperationException("A policy setting has no settingInstance and cannot be preserved safely.");
            policySettings.Add(new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationSetting",
                ["settingInstance"] = instance
            });
        }

        var body = new JsonObject();
        foreach (var propertyName in new[]
        {
            "creationSource", "name", "description", "platforms", "technologies", "roleScopeTagIds", "templateReference"
        })
        {
            if (!policyDocument.TryGetPropertyValue(propertyName, out var propertyValue))
                throw new InvalidOperationException($"Graph did not return policy property '{propertyName}'; the policy cannot be replaced safely.");
            body[propertyName] = propertyValue?.DeepClone();
        }
        body["settings"] = policySettings;

        var policyPath = Uri.EscapeDataString(policy.Id);
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"https://graph.microsoft.com/beta/deviceManagement/configurationPolicies('{policyPath}')");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Graph rejected the rule update ({(int)response.StatusCode}): {SummarizeGraphError(error)}", null, response.StatusCode);
        }

        try
        {
            var verified = await GetElevationRuleCollectionInfoAsync(policy, cancellationToken);
            if (verified.RuleCount != rules.Count)
                throw new InvalidOperationException($"Graph accepted the update, but verification returned {verified.RuleCount} rules instead of {rules.Count}.");
            return verified.RuleCount;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException("Graph accepted the write request, but the updated rule could not be verified. Check Intune before retrying to avoid a duplicate rule.", exception);
        }
    }

    private async Task<List<JsonObject>> GetPolicySettingsForWriteAsync(
        ElevationRulePolicy policy,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var policyId = Uri.EscapeDataString(policy.Id);
        var nextLink = $"https://graph.microsoft.com/beta/deviceManagement/configurationPolicies('{policyId}')/settings?$expand=settingDefinitions&$top=1000";
        var settings = new List<JsonObject>();
        while (!string.IsNullOrEmpty(nextLink))
        {
            var requestUri = new Uri(nextLink);
            if (requestUri.Scheme != Uri.UriSchemeHttps || requestUri.Host != "graph.microsoft.com")
                throw new InvalidOperationException("Graph returned an unexpected paging URL; the request was stopped.");
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            if (root.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in values.EnumerateArray())
                    if (JsonNode.Parse(value.GetRawText()) is JsonObject setting)
                        settings.Add(setting);
            }
            nextLink = root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
        }

        return settings;
    }

    private async Task<JsonObject> GetPolicyDocumentForWriteAsync(
        ElevationRulePolicy policy,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var policyId = Uri.EscapeDataString(policy.Id);
        var requestUri = new Uri($"https://graph.microsoft.com/beta/deviceManagement/configurationPolicies('{policyId}')");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var policyDocument = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject;
        return policyDocument ?? throw new InvalidOperationException("Graph returned an invalid policy document.");
    }

    private static int GetMaximumRuleCount(JsonObject setting, int fallback)
    {
        if (setting["settingDefinitions"] is JsonArray definitions)
            foreach (var definition in definitions.OfType<JsonObject>())
                if (string.Equals(definition["id"]?.GetValue<string>(), ElevationRulesSettingDefinitionId, StringComparison.Ordinal) &&
                    int.TryParse(definition["maximumCount"]?.ToString(), out var maximum))
                    return maximum;
        return fallback;
    }

    private static JsonObject FindRuleTemplate(JsonArray rules, ElevationRule rule)
    {
        var expectedRuleType = GetRuleTypeOption(rule.ElevationType);
        foreach (var candidate in rules.OfType<JsonObject>())
        {
            var children = candidate["children"] as JsonArray;
            if (children is null) continue;
            if (!string.Equals(GetChoiceOption(children, "ruletype"), expectedRuleType, StringComparison.Ordinal))
                continue;

            if (!HasElevationTypeValidationSchema(children, rule.ElevationType))
                continue;

            // Signature source is independent of elevation type. Use this rule for the
            // elevation-specific schema, then replace/remove its signature source below.
            return candidate;
        }

        throw new InvalidOperationException($"This policy has no rule with elevation type '{expectedRuleType}' to use as a schema template. Add a matching rule in Intune first.");
    }

    private static bool HasElevationTypeValidationSchema(JsonArray ruleChildren, ElevationType elevationType)
    {
        var ruleType = FindChild(ruleChildren, "ruletype");
        var nested = ruleType?["choiceSettingValue"]?["children"] as JsonArray;
        if (nested is null)
            return elevationType is not (ElevationType.UserConfirmed or ElevationType.CurrentUser);

        return elevationType switch
        {
            ElevationType.UserConfirmed => FindChild(nested, "ruletype_userconfirmeduservalidation") is not null &&
                                           FindChild(nested, "ruletype_userconfirmeduservalidation_2") is not null,
            ElevationType.CurrentUser => FindChild(nested, "ruletype_validation") is not null,
            _ => true
        };
    }

    private static void PopulateRule(JsonObject newRule, ElevationRule rule, JsonArray allRules)
    {
        var children = newRule["children"] as JsonArray
            ?? throw new InvalidOperationException("The selected rule template has no child settings.");
        SetSimple(children, "name", rule.Name);
        SetOptionalSimple(children, "description", rule.Description);
        SetOptionalSimple(children, "filename", rule.FileName);
        SetOptionalSimple(children, "filehash", rule.FileHash);
        SetOptionalSimple(children, "filepath", rule.FilePath);
        SetOptionalSimple(children, "productname", rule.ProductName);
        // Intune's EPM schema caps File description at 50 characters. File metadata is
        // read-only in the UI, so retain the prefix that fits and report this in validation.
        SetOptionalSimple(children, "filedescription",
            rule.FileDescription is { Length: > 50 } fileDescription ? fileDescription[..50] : rule.FileDescription);
        SetOptionalSimple(children, "internalname", rule.InternalName);
        SetOptionalSimple(children, "fileversion", RuleValidationService.NormalizeMinimumFileVersionToFourParts(rule.MinimumFileVersion));

        SetChoice(children, "ruletype", GetRuleTypeOption(rule.ElevationType));
        if (rule.ElevationType == ElevationType.UserConfirmed)
        {
            SetValidationCollectionSelection(children, "ruletype_userconfirmeduservalidation",
                rule.RequireWindowsAuthentication ? ["0"] : []);
            SetValidationCollectionSelection(children, "ruletype_userconfirmeduservalidation_2",
                rule.RequireBusinessJustification ? ["0"] : []);
        }
        else if (rule.ElevationType == ElevationType.CurrentUser)
        {
            SetValidationCollectionSelection(children, "ruletype_validation",
                rule.RequireBusinessJustification ? ["0", "1"] : ["1"]);
        }
        if (rule.ChildProcessBehavior == ChildProcessBehavior.NotConfigured)
            RemoveChild(children, "childprocessbehavior");
        else
            SetChoice(children, "childprocessbehavior", GetChildProcessOption(rule.ChildProcessBehavior));

        SetRestrictArguments(children, rule);

        if (rule.SignatureSource == SignatureSource.CertificateFile && rule.PublisherCertificate is { } certificate)
        {
            if (string.IsNullOrWhiteSpace(certificate.CertificateBase64))
                throw new InvalidOperationException("The extracted publisher certificate data is unavailable. Analyze the file again.");
            var pem = "-----BEGIN CERTIFICATE-----\r\n" + Convert.ToBase64String(Convert.FromBase64String(certificate.CertificateBase64), Base64FormattingOptions.InsertLineBreaks) + "\r\n-----END CERTIFICATE-----\r\n";
            CopyCertificateSourceConfiguration(children, FindCertificateSourceTemplate(allRules, reusable: false));
            SetChoice(children, "signaturesource", "1");
            SetNestedChoice(children, "signaturesource", "certificatetype", GetCertificateTypeOption(rule.CertificateType));
            SetSimpleRequired(children, "certificatefileupload", pem);
        }
        if (rule.SignatureSource == SignatureSource.ReusableSettings && rule.ReusableCertificateSettingId is { } reusableId)
        {
            CopyCertificateSourceConfiguration(children, FindCertificateSourceTemplate(allRules, reusable: true));
            SetChoice(children, "signaturesource", "0");
            SetNestedChoice(children, "signaturesource", "certificatetype", GetCertificateTypeOption(rule.CertificateType));
            SetSimpleRequired(children, "certificatepayloadwithreusablesetting", reusableId);
        }
        if (rule.SignatureSource == SignatureSource.NotConfigured)
            RemoveChild(children, "signaturesource");
    }

    private static void SetRestrictArguments(JsonArray children, ElevationRule rule)
    {
        if (rule.RestrictArguments == RestrictArgumentsMode.NotConfigured)
        {
            RemoveChild(children, "arguments");
            return;
        }

        var values = new JsonArray();
        foreach (var argument in rule.ArgumentList)
            values.Add(new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationStringSettingValue",
                ["settingValueTemplateReference"] = null,
                ["value"] = argument
            });

        var argumentDefinitionId = ElevationRulesSettingDefinitionId + "_arguments";
        var copy = new JsonObject
        {
            ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationChoiceSettingInstance",
            ["settingDefinitionId"] = argumentDefinitionId,
            ["settingInstanceTemplateReference"] = null,
            ["auditRuleInformation"] = null,
            ["choiceSettingValue"] = new JsonObject
            {
                ["settingValueTemplateReference"] = null,
                ["value"] = argumentDefinitionId + "_allow",
                ["children"] = new JsonArray(new JsonObject
                {
                    ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationSimpleSettingCollectionInstance",
                    ["settingDefinitionId"] = argumentDefinitionId + "_restrictargumentlist",
                    ["settingInstanceTemplateReference"] = null,
                    ["auditRuleInformation"] = null,
                    ["simpleSettingCollectionValue"] = values
                })
            }
        };

        var existing = FindChild(children, "arguments");
        if (existing is null) children.Add(copy);
        else children[children.IndexOf(existing)] = copy;
    }

    private static void SetValidationCollectionSelection(JsonArray ruleChildren, string suffix, string[] selectedOptions)
    {
        var ruleType = FindChild(ruleChildren, "ruletype")
            ?? throw new InvalidOperationException("The selected rule template is missing its elevation type setting.");
        var nestedChildren = ruleType["choiceSettingValue"]?["children"] as JsonArray
            ?? throw new InvalidOperationException("The selected user-confirmed rule has no validation settings.");
        var collection = FindChild(nestedChildren, suffix)
            ?? throw new InvalidOperationException($"The selected user-confirmed rule is missing the '{suffix}' validation setting.");

        if (selectedOptions.Length == 0)
        {
            collection["choiceSettingCollectionValue"] = new JsonArray();
            return;
        }

        var definitionId = collection["settingDefinitionId"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"The '{suffix}' validation setting has no definition ID.");
        var selectedValues = new JsonArray();
        foreach (var option in selectedOptions)
        {
            selectedValues.Add(new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationChoiceSettingValue",
                ["settingValueTemplateReference"] = null,
                ["value"] = definitionId + "_" + option,
                ["children"] = new JsonArray()
            });
        }
        collection["choiceSettingCollectionValue"] = selectedValues;
    }

    private static JsonObject FindCertificateSourceTemplate(JsonArray rules, bool reusable)
    {
        foreach (var candidate in rules.OfType<JsonObject>())
        {
            if (candidate["children"] is not JsonArray children || FindChild(children, "signaturesource") is not { } source)
                continue;

            var option = GetChoiceOption(children, "signaturesource");
            if (reusable && option == "0" && FindDescendant(children, "certificatepayloadwithreusablesetting") is not null)
                return source;
            if (!reusable && option == "1" && FindDescendant(children, "certificatefileupload") is not null)
                return source;
        }

        throw new InvalidOperationException(reusable
            ? "This policy has no reusable-certificate rule to use as a certificate schema template. Add a reusable-certificate rule in Intune first."
            : "This policy has no publisher-certificate rule to use as a certificate schema template. Add a publisher-certificate rule in Intune first.");
    }

    private static void CopyCertificateSourceConfiguration(JsonArray targetChildren, JsonObject sourceTemplate)
    {
        var copy = (JsonObject)sourceTemplate.DeepClone();
        var existing = FindChild(targetChildren, "signaturesource");
        if (existing is null)
        {
            targetChildren.Add(copy);
            return;
        }

        var index = targetChildren.IndexOf(existing);
        targetChildren[index] = copy;
    }

    private static void SetSimple(JsonArray children, string suffix, string value)
    {
        var child = FindDescendant(children, suffix);
        if (child is null) return; // Some EPM rule types intentionally omit optional fields.
        var simpleValue = child["simpleSettingValue"] as JsonObject
            ?? throw new InvalidOperationException($"The template's '{suffix}' field has an unexpected type.");
        simpleValue["value"] = value;
    }

    private static void SetOptionalSimple(JsonArray children, string suffix, string? value)
    {
        var child = FindChild(children, suffix);
        if (child is null) return;
        if (string.IsNullOrWhiteSpace(value))
        {
            children.Remove(child);
            return;
        }

        SetSimple(children, suffix, value);
    }

    private static string? GetChoiceOption(JsonArray children, string suffix)
    {
        var value = FindChild(children, suffix)?["choiceSettingValue"]?["value"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value)) return null;
        var separator = value.LastIndexOf('_');
        return separator >= 0 ? value[(separator + 1)..] : value;
    }

    private static string GetRuleTypeOption(ElevationType elevationType) => elevationType switch
    {
        ElevationType.CurrentUser => "self",
        ElevationType.UserConfirmed => "userconfirmeduser",
        ElevationType.Automatic => "automatic",
        ElevationType.SupportApproved => "supportarbitrated",
        ElevationType.Deny => "deny",
        _ => throw new InvalidOperationException("Unsupported elevation type.")
    };

    private static string GetChildProcessOption(ChildProcessBehavior behavior) => behavior switch
    {
        ChildProcessBehavior.NotConfigured => throw new InvalidOperationException("Not configured has no child-process choice; its setting must be omitted."),
        ChildProcessBehavior.RequireRuleToElevate => "allowrunelevatedrulerequired",
        ChildProcessBehavior.AllowAllElevated => "allowrunelevated",
        ChildProcessBehavior.DenyAll => "deny",
        _ => throw new InvalidOperationException("Unsupported child-process behavior.")
    };

    private static string GetCertificateTypeOption(CertificateType certificateType) => certificateType switch
    {
        CertificateType.Publisher => "publisher",
        CertificateType.CertificateAuthority => "issuingauthority",
        _ => throw new InvalidOperationException("Unsupported certificate type.")
    };

    private static void RemoveChild(JsonArray children, string suffix)
    {
        if (FindChild(children, suffix) is { } child)
            children.Remove(child);
    }

    private static void SetSimpleRequired(JsonArray children, string suffix, string value)
    {
        if (FindDescendant(children, suffix) is null)
            throw new InvalidOperationException($"The selected rule template is missing required '{suffix}' data.");
        SetSimple(children, suffix, value);
    }

    private static string? GetSimpleValue(JsonObject rule, string suffix)
    {
        if (rule["children"] is not JsonArray children) return null;
        return FindDescendant(children, suffix)?["simpleSettingValue"]?["value"]?.GetValue<string>();
    }

    private static void SetChoice(JsonArray children, string suffix, string option)
    {
        var child = FindChild(children, suffix)
            ?? throw new InvalidOperationException($"The template is missing the required '{suffix}' choice.");
        var choiceValue = child["choiceSettingValue"] as JsonObject
            ?? throw new InvalidOperationException($"The template's '{suffix}' choice has an unexpected type.");
        var current = choiceValue["value"]?.GetValue<string>() ?? string.Empty;
        var separator = current.LastIndexOf('_');
        choiceValue["value"] = separator >= 0 ? current[..(separator + 1)] + option : option;
    }

    private static void SetNestedChoice(JsonArray children, string parentSuffix, string suffix, string option)
    {
        var parent = FindChild(children, parentSuffix)?["choiceSettingValue"]?["children"] as JsonArray
            ?? throw new InvalidOperationException($"The certificate template is missing nested '{parentSuffix}' choices.");
        SetChoice(parent, suffix, option);
    }

    private static JsonObject? FindChild(JsonArray children, string suffix) => children.OfType<JsonObject>()
        .FirstOrDefault(child => child["settingDefinitionId"]?.GetValue<string>()?.EndsWith("_" + suffix, StringComparison.Ordinal) == true);

    private static JsonObject? FindDescendant(JsonArray children, string suffix)
    {
        foreach (var child in children.OfType<JsonObject>())
        {
            if (child["settingDefinitionId"]?.GetValue<string>()?.EndsWith("_" + suffix, StringComparison.Ordinal) == true)
                return child;
            if (child["choiceSettingValue"]?["children"] is JsonArray nested && FindDescendant(nested, suffix) is { } match)
                return match;
        }
        return null;
    }

    private static string SummarizeGraphError(string responseBody)
    {
        try
        {
            using var error = JsonDocument.Parse(responseBody);
            if (error.RootElement.TryGetProperty("error", out var graphError) && graphError.TryGetProperty("message", out var message))
                return message.GetString() ?? "Unknown Graph error.";
        }
        catch (JsonException) { }
        return "No error details were returned.";
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _client!.AcquireTokenSilent(Scopes, _account).ExecuteAsync(cancellationToken);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            var result = await _client!.AcquireTokenInteractive(Scopes)
                .WithPrompt(Prompt.SelectAccount)
                .WithUseEmbeddedWebView(false)
                .WithParentActivityOrWindow(_parentWindow)
                .ExecuteAsync(cancellationToken);
            _account = result.Account;
            return result.AccessToken;
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName) =>
        DateTimeOffset.TryParse(GetString(element, propertyName), out var value) ? value : null;

    public async Task RemoveAccountAsync()
    {
        if (_client is not null && _account is not null)
            await _client.RemoveAsync(_account);
        _account = null;
    }

    private async Task<GraphUserProfile> GetUserProfileAsync(string accessToken, string tenantId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me?$select=displayName,userPrincipalName");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        return new GraphUserProfile(
            root.TryGetProperty("displayName", out var displayName) ? displayName.GetString() ?? "" : "",
            root.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() ?? "" : "",
            tenantId);
    }

    private Task DisposeClientAsync()
    {
        if (_cacheHelper is not null && _client is not null)
            _cacheHelper.UnregisterCache(_client.UserTokenCache);
        _cacheHelper = null;
        _client = null;
        _account = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeClientAsync();
        _httpClient.Dispose();
    }
}

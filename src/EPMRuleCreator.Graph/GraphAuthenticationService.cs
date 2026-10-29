using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography.X509Certificates;
using EPMRuleCreator.Core.Models;
using EPMRuleCreator.Core.Services;
using Microsoft.Identity.Client;

namespace EPMRuleCreator.Graph;

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
public sealed record ElevationRuleMatch(int Index, string Name, string FileName, string? FileVersion, string? FileHash,
    string? Signature, string MatchReason, bool VersionMatches, string VersionDisplayReason,
    string? OtherRuleWithSameName, bool IncomingNameAlreadyExists);
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
        _client = CreateClient(clientId.Trim(), authorityTenant);
        try
        {
            var authResult = await _client.AcquireTokenInteractive(Scopes)
                .WithPrompt(Prompt.SelectAccount)
                .WithUseEmbeddedWebView(false)
                .WithParentActivityOrWindow(parentWindow)
                .ExecuteAsync(cancellationToken);
            _account = authResult.Account;
            _parentWindow = parentWindow;
            return await GetUserProfileAsync(authResult.AccessToken, authResult.TenantId, cancellationToken);
        }
        catch
        {
            await DisposeClientAsync();
            throw;
        }
    }

    // MSAL's default cache belongs to this client instance and lives only in RAM.
    // Do not register file-persistence callbacks or a shared cache.
    internal static IPublicClientApplication CreateClient(string clientId, string tenantId) =>
        PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
            .WithRedirectUri("http://localhost")
            .Build();

    public async Task<IReadOnlyList<ElevationRulePolicy>> GetElevationRulePoliciesAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null || _account is null)
            throw new InvalidOperationException("Sign in to Microsoft Graph before loading EPM policies.");

        var accessToken = await GetAccessTokenAsync(cancellationToken);
        var filter = Uri.EscapeDataString("templateReference/templateFamily eq 'endpointSecurityEndpointPrivilegeManagement'");
        var select = "id,name,description,platforms,lastModifiedDateTime,technologies,settingCount,isAssigned,templateReference";
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
                    // The EPM family also contains System Settings policies, which are
                    // shown in a separate UI and do not have an Elevation Rules collection.
                    var templateId = item.TryGetProperty("templateReference", out var templateReference)
                        ? GetString(templateReference, "templateId")
                        : null;
                    if (!IsElevationRulePolicyTemplate(templateId))
                        continue;

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

    internal static bool IsElevationRulePolicyTemplate(string? templateId) =>
        string.Equals(templateId, NewElevationPolicyPayload.TemplateId, StringComparison.OrdinalIgnoreCase);

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

    public async Task<ElevationRulePolicy> CreateElevationRulePolicyAsync(
        string name,
        string? description,
        ElevationRule rule,
        CancellationToken cancellationToken = default)
    {
        if (_client is null || _account is null)
            throw new InvalidOperationException("Sign in to Microsoft Graph before creating a policy.");

        var token = await GetAccessTokenAsync(cancellationToken);
        return await new NewElevationPolicyWriter(_httpClient).CreateAsync(token, name, description, rule, cancellationToken);
    }

    public async Task<string> CreateElevationSystemSettingsPolicyAsync(
        string name,
        string? description,
        bool networkSettingsEnabled,
        bool automatic,
        bool requireBusinessJustification,
        bool requireWindowsAuthentication,
        CancellationToken cancellationToken = default)
    {
        if (_client is null || _account is null)
            throw new InvalidOperationException("Sign in to Microsoft Graph before creating a policy.");

        var token = await GetAccessTokenAsync(cancellationToken);
        return await new NewElevationSystemSettingsPolicyWriter(_httpClient).CreateAsync(token, name, description,
            networkSettingsEnabled, automatic, requireBusinessJustification, requireWindowsAuthentication, cancellationToken);
    }

    public async Task<IReadOnlyList<ElevationSystemSettingsPolicy>> GetElevationSystemSettingsPoliciesAsync(
        CancellationToken cancellationToken = default)
    {
        if (_client is null || _account is null)
            throw new InvalidOperationException("Sign in to Microsoft Graph before loading system settings policies.");

        var token = await GetAccessTokenAsync(cancellationToken);
        return await new ElevationSystemSettingsPolicyWriter(_httpClient).GetPoliciesAsync(token, cancellationToken);
    }

    public async Task<ElevationSystemSettingsPolicy> GetElevationSystemSettingsPolicyConfigurationAsync(
        ElevationSystemSettingsPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (_client is null || _account is null)
            throw new InvalidOperationException("Sign in to Microsoft Graph before loading system settings policy configuration.");

        var token = await GetAccessTokenAsync(cancellationToken);
        return await new ElevationSystemSettingsPolicyWriter(_httpClient)
            .GetConfigurationAsync(token, policy, cancellationToken);
    }

    public async Task<ElevationSystemSettingsPolicy> UpdateElevationSystemSettingsPolicyAsync(
        ElevationSystemSettingsPolicy policy,
        string name,
        string? description,
        bool networkSettingsEnabled,
        bool automatic,
        bool requireBusinessJustification,
        bool requireWindowsAuthentication,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (_client is null || _account is null)
            throw new InvalidOperationException("Sign in to Microsoft Graph before updating a system settings policy.");

        var token = await GetAccessTokenAsync(cancellationToken);
        return await new ElevationSystemSettingsPolicyWriter(_httpClient).UpdateAsync(token, policy, name, description,
            networkSettingsEnabled, automatic, requireBusinessJustification, requireWindowsAuthentication, cancellationToken);
    }

    public Task<int> AppendElevationRuleAsync(
        ElevationRulePolicy policy,
        ElevationRule rule,
        CancellationToken cancellationToken = default) => SaveElevationRuleAsync(policy, rule, false, null, cancellationToken);

    public async Task<IReadOnlyList<ElevationRuleMatch>> GetElevationRuleMatchesAsync(
        ElevationRulePolicy policy, ElevationRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(rule);
        if (_client is null || _account is null)
            throw new InvalidOperationException("Sign in to Microsoft Graph before matching rules.");
        ValidatePolicyId(policy);
        var token = await GetAccessTokenAsync(cancellationToken);
        var settings = await GetPolicySettingsForWriteAsync(policy, token, cancellationToken);
        var setting = FindRulesSetting(settings);
        var rules = setting?["settingInstance"]?["groupSettingCollectionValue"] as JsonArray;
        if (rules is null) return [];
        return FindRuleMatches(rules, rule);
    }

    public async Task<int> SaveElevationRuleAsync(
        ElevationRulePolicy policy, ElevationRule rule, bool updateExisting, ElevationRuleMatch? existingRuleMatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(rule);
        if (_client is null || _account is null)
            throw new InvalidOperationException("Sign in to Microsoft Graph before writing a rule.");
        ValidatePolicyId(policy);
        if (updateExisting && existingRuleMatch is null)
            throw new ArgumentException("The selected existing rule is required when updating a rule.", nameof(existingRuleMatch));

        var token = await GetAccessTokenAsync(cancellationToken);
        var policyDocument = await GetPolicyDocumentForWriteAsync(policy, token, cancellationToken);
        var settings = await GetPolicySettingsForWriteAsync(policy, token, cancellationToken);
        var setting = FindRulesSetting(settings);
        if (setting is null)
            throw new InvalidOperationException("This policy does not yet have an Elevation Rules setting to update. Add an initial rule in Intune first; this app only appends to an existing rules collection.");

        var settingInstance = setting["settingInstance"] as JsonObject
            ?? throw new InvalidOperationException("The policy's Elevation Rules setting has an unexpected format.");
        var rules = settingInstance["groupSettingCollectionValue"] as JsonArray
            ?? throw new InvalidOperationException("The policy's Elevation Rules collection is unavailable.");
        var maximum = GetMaximumRuleCount(setting, DefaultMaximumRuleCount);
        if (!updateExisting && rules.Count >= maximum)
            throw new InvalidOperationException($"The policy already contains {rules.Count} rules, reaching its maximum of {maximum}.");
        var existingRuleIndex = existingRuleMatch?.Index;
        if (existingRuleMatch is not null)
            ValidateSelectedRuleMatch(rules, rule, existingRuleMatch!);
        if (!updateExisting && existingRuleMatch is null && rules.OfType<JsonObject>().Any(existing =>
                string.Equals(GetSimpleValue(existing, "name"), rule.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A rule named '{rule.Name}' already exists in this policy.");

        var newRule = updateExisting
            ? CreateRuleForUpdate(rules, existingRuleIndex!.Value, rule)
            : CreateRuleForAppend(rules, rule);
        if (updateExisting) rules[existingRuleIndex!.Value] = newRule;
        else rules.Add(newRule);

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

    internal static IReadOnlyList<ElevationRuleMatch> FindRuleMatches(JsonArray rules, ElevationRule rule)
    {
        var matches = new List<ElevationRuleMatch>();
        var requestedHash = NormalizeHash(rule.FileHash);
        var requestedCertificate = GetRequestedCertificateThumbprint(rule);
        var incomingNameAlreadyExists = rules.OfType<JsonObject>().Any(existing =>
            string.Equals(GetSimpleValue(existing, "name"), rule.Name, StringComparison.OrdinalIgnoreCase));
        for (var index = 0; index < rules.Count; index++)
        {
            if (rules[index] is not JsonObject existing) continue;
            var hash = GetSimpleValue(existing, "filehash");
            var fileName = GetSimpleValue(existing, "filename") ?? string.Empty;
            var version = GetSimpleValue(existing, "fileversion");
            var signature = GetSignatureIdentity(existing);
            var hashMatch = requestedHash is not null && string.Equals(requestedHash, NormalizeHash(hash), StringComparison.OrdinalIgnoreCase);
            var signatureMatch = requestedCertificate is not null && string.Equals(requestedCertificate, signature, StringComparison.OrdinalIgnoreCase);
            if (!hashMatch && !signatureMatch) continue;
            var normalizedRequestedVersion = NormalizeVersion(rule.MinimumFileVersion);
            var normalizedExistingVersion = NormalizeVersion(version);
            var versionMatches = normalizedRequestedVersion is not null && normalizedExistingVersion is not null &&
                string.Equals(normalizedRequestedVersion, normalizedExistingVersion, StringComparison.Ordinal);
            var versionReason = normalizedExistingVersion is null
                ? "No stored file version"
                : normalizedRequestedVersion is null ? "Requested file version unavailable"
                : versionMatches ? $"Version matches ({normalizedExistingVersion})"
                : $"Version differs (stored {normalizedExistingVersion}, requested {normalizedRequestedVersion})";
            var reason = hashMatch && signatureMatch ? "File hash and signing certificate match"
                : hashMatch ? "File hash matches" : "Signing certificate matches";
            var otherRuleWithSameName = rules.OfType<JsonObject>()
                .Where((candidate, candidateIndex) => candidateIndex != index &&
                    string.Equals(GetSimpleValue(candidate, "name"), rule.Name, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => GetSimpleValue(candidate, "name"))
                .FirstOrDefault();
            matches.Add(new ElevationRuleMatch(index, GetSimpleValue(existing, "name") ?? string.Empty,
                fileName, version, hash, signature, reason, versionMatches, versionReason,
                otherRuleWithSameName, incomingNameAlreadyExists));
        }
        return matches;
    }

    internal static JsonObject CreateRuleForUpdate(JsonArray rules, int index, ElevationRule rule)
    {
        if ((uint)index >= (uint)rules.Count || rules[index] is not JsonObject existing)
            throw new ArgumentOutOfRangeException(nameof(index), "The selected rule index is not present in the policy.");
        var template = (JsonObject)existing.DeepClone();
        PopulateRule(template, rule, rules);
        RemoveTemplateReferences(template);
        return template;
    }

    internal static void ValidateSelectedRuleMatch(JsonArray currentRules, ElevationRule rule, ElevationRuleMatch expectedMatch)
    {
        ArgumentNullException.ThrowIfNull(currentRules);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(expectedMatch);
        var refreshedMatch = FindRuleMatches(currentRules, rule)
            .FirstOrDefault(match => match.Index == expectedMatch.Index);
        if (refreshedMatch is null || refreshedMatch != expectedMatch)
            throw new InvalidOperationException("The selected rule changed while the confirmation dialog was open. Refresh the policy and choose a matching rule again.");
    }

    private static JsonObject? FindRulesSetting(IEnumerable<JsonObject> settings) => settings.SingleOrDefault(item =>
        string.Equals(item["settingInstance"]?["settingDefinitionId"]?.GetValue<string>(), ElevationRulesSettingDefinitionId, StringComparison.Ordinal));

    private static void ValidatePolicyId(ElevationRulePolicy policy)
    {
        if (string.IsNullOrWhiteSpace(policy.Id) || !Guid.TryParse(policy.Id, out _))
            throw new InvalidOperationException("The selected policy has no valid Graph ID.");
    }

    private static string? NormalizeHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(character => !char.IsWhiteSpace(character) && character is not '-' and not ':')
            .Select(char.ToUpperInvariant).ToArray());
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? NormalizeVersion(string? value) => RuleValidationService.NormalizeMinimumFileVersionToFourParts(value);

    private static string? GetRequestedCertificateThumbprint(ElevationRule rule)
    {
        if (rule.SignatureSource == SignatureSource.ReusableSettings)
            return string.IsNullOrWhiteSpace(rule.ReusableCertificateSettingId) ? null : "reusable:" + rule.ReusableCertificateSettingId.Trim();
        if (rule.SignatureSource != SignatureSource.CertificateFile || string.IsNullOrWhiteSpace(rule.PublisherCertificate?.CertificateBase64))
            return null;
        try
        {
            using var certificate = X509Certificate2.CreateFromPem("-----BEGIN CERTIFICATE-----\n" + rule.PublisherCertificate.CertificateBase64 + "\n-----END CERTIFICATE-----");
            return NormalizeThumbprint(certificate.Thumbprint);
        }
        catch (CryptographicException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static string? GetSignatureIdentity(JsonObject rule)
    {
        if (rule["children"] is not JsonArray children) return null;
        var reusable = FindDescendant(children, "certificatepayloadwithreusablesetting")?["simpleSettingValue"]?["value"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(reusable)) return "reusable:" + reusable.Trim();
        var pem = FindDescendant(children, "certificatefileupload")?["simpleSettingValue"]?["value"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(pem)) return null;
        try { using var certificate = X509Certificate2.CreateFromPem(pem); return NormalizeThumbprint(certificate.Thumbprint); }
        catch (CryptographicException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static string? NormalizeThumbprint(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : new string(value.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());

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

    internal static JsonObject CreateRuleForAppend(JsonArray rules, ElevationRule rule)
    {
        var template = FindRuleTemplate(rules, rule);
        var newRule = (JsonObject)template.DeepClone();
        PopulateRule(newRule, rule, rules);
        // The captured Intune PUT keeps template references on the original rule
        // only. Reusing them in an appended rule produces DuplicateReferenceId.
        // Clean after populating, which may copy certificate settings or add refs.
        RemoveTemplateReferences(newRule);
        return newRule;
    }

    internal static void RemoveTemplateReferences(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("settingInstanceTemplateReference");
            obj.Remove("settingValueTemplateReference");
            foreach (var property in obj)
                if (property.Value is not null)
                    RemoveTemplateReferences(property.Value);
        }
        else if (node is JsonArray array)
            foreach (var item in array)
                if (item is not null)
                    RemoveTemplateReferences(item);
    }

    private static JsonObject FindRuleTemplate(JsonArray rules, ElevationRule rule)
    {
        foreach (var candidate in rules.OfType<JsonObject>())
        {
            var children = candidate["children"] as JsonArray;
            if (children is not null && FindChild(children, "ruletype") is not null)
                return candidate;
        }

        throw new InvalidOperationException("This policy's Elevation Rules collection has no existing rule to use as a payload template. Add an initial rule in Intune first.");
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

        SetElevationType(children, rule);
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

    internal static void SetRestrictArguments(JsonArray children, ElevationRule rule)
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

    internal static void SetElevationType(JsonArray ruleChildren, ElevationRule rule)
    {
        var ruleType = FindChild(ruleChildren, "ruletype")
            ?? throw new InvalidOperationException("The selected rule template is missing its elevation type setting.");
        var definitionId = ruleType["settingDefinitionId"]?.GetValue<string>()
            ?? throw new InvalidOperationException("The elevation type setting has no definition ID.");
        var option = GetRuleTypeOption(rule.ElevationType);
        var choice = ruleType["choiceSettingValue"] as JsonObject
            ?? throw new InvalidOperationException("The elevation type setting has an unexpected format.");
        // Graph's rule-type option itemIds are rooted at the rule group, not at
        // the _ruletype setting. Validation children still use definitionId below.
        choice["value"] = ElevationRulesSettingDefinitionId + "_" + option;
        choice["settingValueTemplateReference"] = rule.ElevationType == ElevationType.UserConfirmed
            ? new JsonObject
            {
                ["settingValueTemplateId"] = "cb2ea689-ebc3-42ea-a7a4-c704bb13e3ad",
                ["useTemplateDefault"] = false
            }
            : null;

        var nested = new JsonArray();
        switch (rule.ElevationType)
        {
            case ElevationType.UserConfirmed:
                var userConfirmedOptions = new List<string>();
                if (rule.RequireBusinessJustification) userConfirmedOptions.Add("0");
                if (rule.RequireWindowsAuthentication) userConfirmedOptions.Add("1");
                // Intune omits optional validation settings when nothing is selected.
                // Sending an instance with an empty value collection is rejected.
                if (userConfirmedOptions.Count > 0)
                    nested.Add(CreateValidationCollection(definitionId + "_validation", [.. userConfirmedOptions]));
                break;
            case ElevationType.CurrentUser:
                nested.Add(CreateValidationCollection(definitionId + "_userconfirmeduservalidation", ["0"]));
                if (rule.RequireBusinessJustification)
                    nested.Add(CreateValidationCollection(definitionId + "_userconfirmeduservalidation_2", ["0"]));
                break;
        }
        choice["children"] = nested;
    }

    private static JsonObject CreateValidationCollection(string definitionId, string[] selectedOptions)
    {
        var values = new JsonArray();
        foreach (var option in selectedOptions)
            values.Add(new JsonObject
            {
                ["settingValueTemplateReference"] = null,
                ["value"] = definitionId + "_" + option,
                ["children"] = new JsonArray()
            });

        return new JsonObject
        {
            ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationChoiceSettingCollectionInstance",
            ["settingDefinitionId"] = definitionId,
            ["settingInstanceTemplateReference"] = null,
            ["auditRuleInformation"] = null,
            ["choiceSettingCollectionValue"] = values
        };
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
        ElevationType.CurrentUser => "userconfirmeduser",
        ElevationType.UserConfirmed => "self",
        ElevationType.Automatic => "automatic",
        ElevationType.SupportApproved => "supportarbitrated",
        ElevationType.Deny => "deny",
        _ => throw new InvalidOperationException("Unsupported elevation type.")
    };

    internal static string GetChildProcessOption(ChildProcessBehavior behavior) => behavior switch
    {
        ChildProcessBehavior.NotConfigured => throw new InvalidOperationException("Not configured has no child-process choice; its setting must be omitted."),
        ChildProcessBehavior.RequireRuleToElevate => "allowrunelevatedrulerequired",
        ChildProcessBehavior.AllowAllElevated => "allowrunelevated",
        ChildProcessBehavior.DenyAll => "deny",
        _ => throw new InvalidOperationException("Unsupported child-process behavior.")
    };

    internal static string GetCertificateTypeOption(CertificateType certificateType) => certificateType switch
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
        await DisposeClientAsync();
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

    private async Task DisposeClientAsync()
    {
        var client = _client;
        _client = null;
        _account = null;
        _parentWindow = IntPtr.Zero;
        if (client is not null)
            foreach (var account in await client.GetAccountsAsync())
                await client.RemoveAsync(account);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeClientAsync();
        _httpClient.Dispose();
    }
}

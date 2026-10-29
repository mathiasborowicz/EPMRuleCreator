using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EPMRuleCreator.Graph;

/// <summary>Represents an Endpoint Privilege Management system settings policy and its editable configuration.</summary>
public sealed record ElevationSystemSettingsPolicy(
    string Id,
    string Name,
    string? Description,
    bool NetworkSettingsEnabled,
    bool Automatic,
    bool RequireBusinessJustification,
    bool RequireWindowsAuthentication);

internal sealed class ElevationSystemSettingsPolicyWriter(HttpClient httpClient)
{
    internal const string TemplateId = "598096f6-4121-4088-859f-b11ebfbd09bc_1";
    private const string RootDefinitionId = "device_vendor_msft_policy_privilegemanagement_systemsettingrules_{systemsettingrulename}";
    private const string SettingTypeDefinitionId = RootDefinitionId + "_settingtype";
    private const string ElevationActionDefinitionId = RootDefinitionId + "_elevationaction";
    private const string ElevationValidationDefinitionId = RootDefinitionId + "_elevationvalidation";
    private const string NetworkSettingsValue = RootDefinitionId + "_networksettings";
    private const string GraphRoot = "https://graph.microsoft.com/beta/deviceManagement/configurationPolicies";

    internal async Task<IReadOnlyList<ElevationSystemSettingsPolicy>> GetPoliciesAsync(
        string accessToken, CancellationToken cancellationToken = default)
    {
        var filter = Uri.EscapeDataString($"templateReference/templateId eq '{TemplateId}'");
        var select = Uri.EscapeDataString("id,name,description,templateReference");
        var nextLink = $"{GraphRoot}?$top=100&$filter={filter}&$select={select}";
        var policies = new List<ElevationSystemSettingsPolicy>();

        while (!string.IsNullOrEmpty(nextLink))
        {
            using var response = await SendAsync(HttpMethod.Get, nextLink, accessToken, null, cancellationToken);
            var root = await ReadJsonObjectAsync(response, cancellationToken);
            if (root["value"] is JsonArray values)
            {
                foreach (var item in values.OfType<JsonObject>())
                {
                    if (!string.Equals(GetString(item["templateReference"] as JsonObject, "templateId"), TemplateId, StringComparison.Ordinal))
                        continue;
                    var id = GetString(item, "id");
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    policies.Add(new ElevationSystemSettingsPolicy(id,
                        GetString(item, "name") ?? "(Unnamed policy)", GetString(item, "description"),
                        false, false, false, false));
                }
            }
            nextLink = GetString(root, "@odata.nextLink");
        }
        return policies;
    }

    internal async Task<ElevationSystemSettingsPolicy> GetConfigurationAsync(
        string accessToken, ElevationSystemSettingsPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ValidatePolicyId(policy.Id);
        var settings = await GetSettingsAsync(policy.Id, accessToken, cancellationToken);
        var matches = FindSystemSettingsInstances(settings);
        if (matches.Count == 0)
            return policy with { NetworkSettingsEnabled = false, Automatic = false, RequireBusinessJustification = false, RequireWindowsAuthentication = false };
        if (matches.Count > 1)
            throw new InvalidOperationException("The policy contains multiple Network settings instances; it cannot be parsed safely.");
        return ParseConfiguration(policy, GetSettingInstance(matches[0]));
    }

    internal async Task<ElevationSystemSettingsPolicy> UpdateAsync(
        string accessToken,
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
        ValidatePolicyId(policy.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (automatic && (requireBusinessJustification || requireWindowsAuthentication))
            throw new ArgumentException("Validation options require User confirmed elevation.");

        // Read the policy and the full settings collection before replacement so all existing policy settings survive.
        var policyDocument = await GetPolicyDocumentAsync(policy.Id, accessToken, cancellationToken);
        var settings = await GetSettingsAsync(policy.Id, accessToken, cancellationToken);
        var systemSettings = FindSystemSettingsInstances(settings);
        if (systemSettings.Count > 1)
            throw new InvalidOperationException("The policy contains multiple Network settings instances; it cannot be updated safely.");
        var systemSetting = systemSettings.SingleOrDefault();
        JsonObject? updatedInstance = null;
        if (networkSettingsEnabled)
        {
            updatedInstance = systemSetting is null
                ? CreateSystemSettingsInstance(automatic, requireBusinessJustification, requireWindowsAuthentication)
                : UpdateSystemSettingsInstance((JsonObject)GetSettingInstance(systemSetting).DeepClone(), automatic,
                    requireBusinessJustification, requireWindowsAuthentication);
        }

        var putSettings = new JsonArray();
        foreach (var setting in settings)
        {
            if (ReferenceEquals(setting, systemSetting))
            {
                if (!networkSettingsEnabled) continue;
                putSettings.Add(new JsonObject
                {
                    ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationSetting",
                    ["settingInstance"] = updatedInstance!.DeepClone()
                });
                continue;
            }

            var instance = setting["settingInstance"]?.DeepClone();
            if (instance is null)
                throw new InvalidOperationException("Graph returned a policy setting without a settingInstance; replacement was stopped to avoid losing configuration.");
            putSettings.Add(new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationSetting",
                ["settingInstance"] = instance
            });
        }
        if (networkSettingsEnabled && systemSetting is null)
            putSettings.Add(new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationSetting",
                ["settingInstance"] = updatedInstance!.DeepClone()
            });

        var body = BuildUpdateBody(policyDocument, name.Trim(), description ?? string.Empty, putSettings);
        var policyUri = PolicyUri(policy.Id);
        using (var response = await SendAsync(HttpMethod.Put, policyUri, accessToken, body, cancellationToken))
            await EnsureSuccessAsync(response, cancellationToken);

        var expected = new ElevationSystemSettingsPolicy(policy.Id, name.Trim(), description ?? string.Empty,
            networkSettingsEnabled, automatic, requireBusinessJustification, requireWindowsAuthentication);
        try
        {
            var readBackDocument = await GetPolicyDocumentAsync(policy.Id, accessToken, cancellationToken);
            var readBackPolicy = policy with
            {
                Name = GetString(readBackDocument, "name") ?? string.Empty,
                Description = GetString(readBackDocument, "description") ?? string.Empty
            };
            var verified = await GetConfigurationAsync(accessToken, readBackPolicy, cancellationToken);
            if (verified != expected)
                throw new InvalidOperationException("The policy values returned by Graph do not match the requested update.");
            return verified;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException("Graph accepted the policy update, but read-back verification failed. Check the policy in Intune before retrying.", exception);
        }
    }

    internal static ElevationSystemSettingsPolicy ParseConfiguration(ElevationSystemSettingsPolicy policy, JsonObject instance)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(instance);
        if (!string.Equals(GetString(instance, "settingDefinitionId"), SettingTypeDefinitionId, StringComparison.Ordinal))
            throw new InvalidOperationException("The setting is not the supported Elevation System Settings instance.");
        var rootChoice = instance["choiceSettingValue"] as JsonObject
            ?? throw new InvalidOperationException("The system settings choice value is unavailable.");
        var selectedRoot = GetString(rootChoice, "value");
        var networkEnabled = string.Equals(selectedRoot, NetworkSettingsValue, StringComparison.Ordinal);
        if (!networkEnabled)
            throw new InvalidOperationException("The policy does not select the supported Network settings option.");

        var action = FindChild(rootChoice["children"] as JsonArray, ElevationActionDefinitionId)
            ?? throw new InvalidOperationException("The system settings elevation action is missing.");
        var actionChoice = action["choiceSettingValue"] as JsonObject
            ?? throw new InvalidOperationException("The system settings elevation action has an unexpected format.");
        var actionValue = GetString(actionChoice, "value");
        var automatic = string.Equals(actionValue, RootDefinitionId + "_automatic", StringComparison.Ordinal);
        if (!automatic && !string.Equals(actionValue, RootDefinitionId + "_self", StringComparison.Ordinal))
            throw new InvalidOperationException("The policy uses an unsupported system settings elevation action.");

        var business = false;
        var windows = false;
        foreach (var child in actionChoice["children"] as JsonArray ?? [])
        {
            if (!string.Equals(GetString(child as JsonObject, "settingDefinitionId"), ElevationValidationDefinitionId, StringComparison.Ordinal))
                continue;
            if (child is not JsonObject collection || collection["choiceSettingCollectionValue"] is not JsonArray values)
                throw new InvalidOperationException("The system settings validation collection has an unexpected format.");
            foreach (var value in values.OfType<JsonObject>())
            {
                var option = GetString(value, "value");
                if (string.Equals(option, ElevationValidationDefinitionId + "_0", StringComparison.Ordinal)) business = true;
                else if (string.Equals(option, ElevationValidationDefinitionId + "_1", StringComparison.Ordinal)) windows = true;
            }
        }
        return policy with
        {
            NetworkSettingsEnabled = networkEnabled,
            Automatic = automatic,
            RequireBusinessJustification = !automatic && business,
            RequireWindowsAuthentication = !automatic && windows
        };
    }

    internal static JsonObject UpdateSystemSettingsInstance(JsonObject original, bool automatic,
        bool requireBusinessJustification, bool requireWindowsAuthentication)
    {
        var updated = (JsonObject)original.DeepClone();
        var rootChoice = updated["choiceSettingValue"] as JsonObject
            ?? throw new InvalidOperationException("The system settings choice value is unavailable.");
        if (!string.Equals(GetString(rootChoice, "value"), NetworkSettingsValue, StringComparison.Ordinal))
            throw new InvalidOperationException("The system settings instance is not configured for Network settings.");
        var action = FindChild(rootChoice["children"] as JsonArray, ElevationActionDefinitionId)
            ?? throw new InvalidOperationException("The system settings elevation action is missing.");
        var actionChoice = action["choiceSettingValue"] as JsonObject
            ?? throw new InvalidOperationException("The system settings elevation action has an unexpected format.");
        actionChoice["value"] = RootDefinitionId + (automatic ? "_automatic" : "_self");
        var children = actionChoice["children"] as JsonArray
            ?? throw new InvalidOperationException("The system settings elevation action children are unavailable.");
        for (var index = children.Count - 1; index >= 0; index--)
            if (children[index] is JsonObject child &&
                string.Equals(GetString(child, "settingDefinitionId"), ElevationValidationDefinitionId, StringComparison.Ordinal))
                children.RemoveAt(index);

        if (!automatic && (requireBusinessJustification || requireWindowsAuthentication))
        {
            var values = new JsonArray();
            if (requireBusinessJustification) values.Add(ValidationOption("0"));
            if (requireWindowsAuthentication) values.Add(ValidationOption("1"));
            children.Add(new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationChoiceSettingCollectionInstance",
                ["settingDefinitionId"] = ElevationValidationDefinitionId,
                ["choiceSettingCollectionValue"] = values
            });
        }
        return updated;
    }

    private static JsonObject CreateSystemSettingsInstance(bool automatic,
        bool requireBusinessJustification, bool requireWindowsAuthentication)
    {
        var body = NewElevationSystemSettingsPolicyPayload.Build("Temporary", string.Empty, true, automatic,
            requireBusinessJustification, requireWindowsAuthentication);
        return (JsonObject)(body["settings"]?[0]?["settingInstance"]?.DeepClone()
            ?? throw new InvalidOperationException("A Network settings instance could not be constructed."));
    }

    internal static JsonObject BuildUpdateBody(JsonObject policyDocument, string name, string description, JsonArray settings)
    {
        var body = new JsonObject();
        foreach (var property in new[] { "creationSource", "platforms", "technologies", "roleScopeTagIds", "templateReference" })
        {
            if (!policyDocument.TryGetPropertyValue(property, out var value) ||
                (value is null && !string.Equals(property, "creationSource", StringComparison.Ordinal)))
                throw new InvalidOperationException($"Graph did not return required policy property '{property}'; the policy cannot be replaced safely.");
            body[property] = value?.DeepClone();
        }
        body["name"] = name;
        body["description"] = description;
        body["settings"] = settings;
        return body;
    }

    private async Task<JsonObject> GetPolicyDocumentAsync(string id, string token, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, PolicyUri(id), token, null, cancellationToken);
        return await ReadJsonObjectAsync(response, cancellationToken);
    }

    private async Task<List<JsonObject>> GetSettingsAsync(string id, string token, CancellationToken cancellationToken)
    {
        var nextLink = PolicyUri(id) + "/settings?$top=1000";
        var settings = new List<JsonObject>();
        while (!string.IsNullOrEmpty(nextLink))
        {
            using var response = await SendAsync(HttpMethod.Get, nextLink, token, null, cancellationToken);
            var root = await ReadJsonObjectAsync(response, cancellationToken);
            if (root["value"] is JsonArray values)
                foreach (var value in values.OfType<JsonObject>()) settings.Add((JsonObject)value.DeepClone());
            nextLink = GetString(root, "@odata.nextLink");
        }
        return settings;
    }

    private static List<JsonObject> FindSystemSettingsInstances(IEnumerable<JsonObject> settings)
    {
        return settings.Where(item =>
            string.Equals(GetString(item["settingInstance"] as JsonObject, "settingDefinitionId"), SettingTypeDefinitionId, StringComparison.Ordinal)).ToList();
    }

    private static JsonObject GetSettingInstance(JsonObject setting) => setting["settingInstance"] as JsonObject
        ?? throw new InvalidOperationException("Graph returned a system settings item without a settingInstance.");

    private static JsonObject? FindChild(JsonArray? children, string definitionId) => children?.OfType<JsonObject>()
        .SingleOrDefault(child => string.Equals(GetString(child, "settingDefinitionId"), definitionId, StringComparison.Ordinal));

    private static JsonObject ValidationOption(string suffix) => new()
    {
        ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationChoiceSettingValue",
        ["value"] = ElevationValidationDefinitionId + "_" + suffix,
        ["children"] = new JsonArray()
    };

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, string token, JsonObject? body,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(uri, UriKind.Absolute);
        if (requestUri.Scheme != Uri.UriSchemeHttps || requestUri.Host != "graph.microsoft.com")
            throw new InvalidOperationException("Graph returned an unexpected paging URL; the request was stopped.");
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task<JsonObject> ReadJsonObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidOperationException("Graph returned an invalid JSON object.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        string? message = null;
        try { message = GetString(JsonNode.Parse(text)?["error"] as JsonObject, "message"); }
        catch (JsonException) { }
        throw new HttpRequestException($"Graph request failed ({(int)response.StatusCode}): {message ?? "No error details were returned."}", null, response.StatusCode);
    }

    private static string PolicyUri(string id) => $"{GraphRoot}('{Uri.EscapeDataString(id)}')";
    private static void ValidatePolicyId(string id)
    {
        if (!Guid.TryParse(id, out _)) throw new InvalidOperationException("The selected policy has no valid Graph ID.");
    }
    private static string? GetString(JsonObject? obj, string property) => obj?[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

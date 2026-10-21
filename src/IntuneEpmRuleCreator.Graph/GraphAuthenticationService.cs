using System.Net.Http.Headers;
using System.Text.Json;
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

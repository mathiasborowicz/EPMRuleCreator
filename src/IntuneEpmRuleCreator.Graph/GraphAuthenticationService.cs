using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace IntuneEpmRuleCreator.Graph;

public sealed record GraphUserProfile(string DisplayName, string UserPrincipalName, string TenantId);

/// <summary>Interactive delegated authentication for the Microsoft Graph global cloud.</summary>
public sealed class GraphAuthenticationService : IAsyncDisposable
{
    private static readonly string[] Scopes = ["User.Read"];
    private readonly HttpClient _httpClient = new();
    private IPublicClientApplication? _client;
    private MsalCacheHelper? _cacheHelper;
    private IAccount? _account;

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
        return await GetUserProfileAsync(authResult.AccessToken, authResult.TenantId, cancellationToken);
    }

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

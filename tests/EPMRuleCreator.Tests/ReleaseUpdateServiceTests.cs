using System.Net;
using System.Text.Json;
using EPMRuleCreator.Core.Models;
using EPMRuleCreator.Core.Services;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class ReleaseUpdateServiceTests
{
    [Theory]
    [InlineData("v0.9.1", true)]
    [InlineData("v0.9.0", false)]
    [InlineData("v0.8.9", false)]
    [InlineData("v0.10.0", true)]
    [InlineData("v0.9.1-beta", false)]
    [InlineData("build-49", false)]
    [InlineData("v0.9.1/evil", false)]
    [InlineData("v0.9.1.0", false)]
    public async Task ComparesStableSemanticTags(string tag, bool expected)
    {
        using var client = Client(JsonSerializer.Serialize(new { tag_name = tag, draft = false, prerelease = false, html_url = "https://untrusted.example" }));
        var update = await new ReleaseUpdateService(client).CheckAsync(new Version(0, 9, 0, 0));
        Assert.Equal(expected, update is not null);
        if (update is not null)
            Assert.Equal($"https://github.com/mathiasborowicz/EPMRuleCreator/releases/tag/{tag}", update.ReleaseUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task IgnoresDraftAndPrerelease(bool draft, bool prerelease)
    {
        using var client = Client(JsonSerializer.Serialize(new { tag_name = "v1.0.0", draft, prerelease }));
        Assert.Null(await new ReleaseUpdateService(client).CheckAsync(new Version(0, 9, 0)));
    }

    [Fact]
    public async Task NoReleaseIsNotAnUpdate()
    {
        using var client = Client("{}", HttpStatusCode.NotFound);
        Assert.Null(await new ReleaseUpdateService(client).CheckAsync(new Version(0, 9, 0)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ApiFailuresAreNotReportedAsUpToDate(HttpStatusCode status)
    {
        using var client = Client("{}", status);
        await Assert.ThrowsAsync<HttpRequestException>(() => new ReleaseUpdateService(client).CheckAsync(new Version(0, 9, 0)));
    }

    [Fact]
    public async Task MalformedResponseAndCancellationArePropagated()
    {
        using var client = Client("not json");
        await Assert.ThrowsAsync<JsonException>(() => new ReleaseUpdateService(client).CheckAsync(new Version(0, 9, 0)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ReleaseUpdateService(client).CheckAsync(new Version(0, 9, 0), cancellation.Token));
    }

    [Fact]
    public void StartupPreferenceDefaultsOnAndRemembersOptOutWithoutChangingOtherSettings()
    {
        var legacy = JsonSerializer.Deserialize<LocalAppSettings>("{\"ClientId\":\"test\",\"TenantId\":\"organizations\",\"IsDarkTheme\":true}")!;
        Assert.True(legacy.CheckForUpdatesAtStartup);
        var restored = JsonSerializer.Deserialize<LocalAppSettings>(JsonSerializer.Serialize(legacy with { CheckForUpdatesAtStartup = false }))!;
        Assert.False(restored.CheckForUpdatesAtStartup);
        Assert.Equal(legacy.ClientId, restored.ClientId);
        Assert.Equal(legacy.TenantId, restored.TenantId);
        Assert.Equal(legacy.IsDarkTheme, restored.IsDarkTheme);
    }

    private static HttpClient Client(string body, HttpStatusCode status = HttpStatusCode.OK) => new(new Handler(body, status));

    private sealed class Handler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(ReleaseUpdateService.LatestReleaseEndpoint, request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Headers.Authorization);
            Assert.Contains("EPMRuleCreator", request.Headers.UserAgent.ToString());
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}

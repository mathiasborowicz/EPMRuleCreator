using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace EPMRuleCreator.Core.Services;

public sealed record ReleaseUpdate(Version Version, Uri ReleaseUrl, Uri? DownloadUrl = null, string? Sha256 = null);

/// <summary>Public release metadata only. Never uses Graph credentials or downloads executables.</summary>
public sealed class ReleaseUpdateService(HttpClient client)
{
    public const string LatestReleaseEndpoint = "https://api.github.com/repos/mathiasborowicz/EPMRuleCreator/releases/latest";

    public async Task<ReleaseUpdate?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
        request.Headers.UserAgent.ParseAdd("EPMRuleCreator/update-check");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<ReleaseMetadata>(cancellationToken);
        if (release is null || release.Draft || release.Prerelease || !TryParseVersion(release.Tag, out var latest)) return null;
        var current = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
        if (latest <= current) return null;
        var expectedDownload = $"https://github.com/mathiasborowicz/EPMRuleCreator/releases/download/{release.Tag}/EPMRuleCreator-win-x64.zip";
        var asset = release.Assets?.FirstOrDefault(asset => asset.Name == "EPMRuleCreator-win-x64.zip"
            && asset.DownloadUrl == expectedDownload && IsSha256Digest(asset.Digest));
        return new ReleaseUpdate(latest, new Uri($"https://github.com/mathiasborowicz/EPMRuleCreator/releases/tag/{release.Tag}"),
            asset is null ? null : new Uri(expectedDownload), asset?.Digest?[7..]);
    }

    private static bool IsSha256Digest(string? digest) => digest is { Length: 71 }
        && digest.StartsWith("sha256:", StringComparison.Ordinal) && digest[7..].All(Uri.IsHexDigit);

    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrEmpty(tag) || tag[0] != 'v') return false;
        var parts = tag[1..].Split('.');
        if (parts.Length != 3 || parts.Any(part => part.Length == 0 || part.Any(c => c < '0' || c > '9'))) return false;
        if (!Version.TryParse(tag[1..], out var parsed)) return false;
        version = parsed;
        return true;
    }

    private sealed record ReleaseMetadata(
        [property: JsonPropertyName("tag_name")] string? Tag,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] ReleaseAsset[]? Assets);

    private sealed record ReleaseAsset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("browser_download_url")] string? DownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);
}

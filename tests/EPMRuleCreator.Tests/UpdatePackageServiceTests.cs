using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using EPMRuleCreator.Core.Services;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class UpdatePackageServiceTests
{
    [Theory]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [InlineData(null, false)]
    [InlineData("sha256:invalid", false)]
    public async Task OnlyOfficialAssetWithValidDigestCanBeInstalled(string? digest, bool expected)
    {
        var body = JsonSerializer.Serialize(new
        {
            tag_name = "v0.9.1",
            assets = new[] { new { name = "EPMRuleCreator-win-x64.zip", browser_download_url = Url, digest } }
        });
        using var client = new HttpClient(new Handler(System.Text.Encoding.UTF8.GetBytes(body)));
        var update = await new ReleaseUpdateService(client).CheckAsync(new Version(0, 9, 0));
        Assert.NotNull(update);
        Assert.Equal(expected, update.DownloadUrl is not null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VerifiesDownloadedBytesBeforeInstallation(bool correctDigest)
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var digest = correctDigest ? Convert.ToHexString(SHA256.HashData(bytes)) : new string('0', 64);
        using var client = new HttpClient(new Handler(bytes));
        var directory = Directory.CreateTempSubdirectory("epm-update-test-");
        var path = Path.Combine(directory.FullName, "update.zip");
        try
        {
            var update = new ReleaseUpdate(new Version(0, 9, 1), new Uri("https://github.com"), new Uri(Url), digest);
            if (correctDigest)
            {
                await UpdatePackageService.DownloadAsync(client, update, path, CancellationToken.None);
                Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            else await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePackageService.DownloadAsync(client, update, path, CancellationToken.None));
        }
        finally { File.Delete(path); directory.Delete(); }
    }

    [Fact]
    public async Task RejectsUntrustedDownloadBeforeMakingRequest()
    {
        using var client = new HttpClient(new Handler([]));
        var update = new ReleaseUpdate(new Version(0, 9, 1), new Uri("https://github.com"), new Uri("https://untrusted.example/app.zip"), new string('a', 64));
        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdatePackageService.DownloadAsync(client, update, "unused.zip", CancellationToken.None));
        Assert.False(File.Exists("unused.zip"));
    }

    [Theory]
    [InlineData("EPMRuleCreator.exe", true)]
    [InlineData("../EPMRuleCreator.exe", false)]
    [InlineData("subdir/EPMRuleCreator.exe", false)]
    public void ExtractsOnlyExpectedRootExecutable(string entryName, bool valid)
    {
        var directory = Directory.CreateTempSubdirectory("epm-update-test-");
        var archivePath = Path.Combine(directory.FullName, "update.zip");
        var exePath = Path.Combine(directory.FullName, "app.exe");
        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            using (var entry = archive.CreateEntry(entryName).Open()) entry.Write("MZtest"u8);
            if (valid)
            {
                UpdatePackageService.ExtractExecutable(archivePath, exePath);
                Assert.Equal("MZtest"u8.ToArray(), File.ReadAllBytes(exePath));
                Assert.Throws<IOException>(() => UpdatePackageService.ExtractExecutable(archivePath, exePath));
            }
            else Assert.Throws<InvalidDataException>(() => UpdatePackageService.ExtractExecutable(archivePath, exePath));
        }
        finally { File.Delete(exePath); File.Delete(archivePath); directory.Delete(); }
    }

    private const string Url = "https://github.com/mathiasborowicz/EPMRuleCreator/releases/download/v0.9.1/EPMRuleCreator-win-x64.zip";
    private sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}

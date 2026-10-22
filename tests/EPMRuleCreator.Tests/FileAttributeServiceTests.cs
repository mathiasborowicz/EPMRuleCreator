using System.Security.Cryptography;
using EPMRuleCreator.Core.Services;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class FileAttributeServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EpmAnalysisTests-" + Guid.NewGuid().ToString("N"));
    private string CreateFile()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "sample.ps1");
        File.WriteAllText(path, "# Only read, never executed\nWrite-Output 'test'\n");
        return path;
    }

    [Fact]
    public async Task DefaultAnalysisNeverLoadsInstalledEpmCmdlets()
    {
        var reader = new FakeReader { Error = new InvalidOperationException("Must not be loaded") };
        var result = await new FileAttributeService(new(), reader).AnalyzeAsync(CreateFile());
        Assert.Equal(0, reader.Calls);
        Assert.Null(result.EpmFileHash);
        Assert.Equal(64, result.Sha256.Length);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("EpmCmdlets"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OptionalModuleFailurePreservesLocalAnalysis(bool accessDenied)
    {
        var reader = new FakeReader { Error = accessDenied ? new UnauthorizedAccessException("Access denied") : new InvalidOperationException("Import-Module failed") };
        var path = CreateFile();
        var result = await new FileAttributeService(new(), reader).AnalyzeAsync(path, useEpmCmdlets: true);
        Assert.Equal(1, reader.Calls);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), result.Sha256);
        Assert.Null(result.EpmFileHash);
        Assert.Contains(result.Warnings, warning => warning.Contains("Using local SHA256"));
    }

    [Fact]
    public async Task MissingModuleIsUnavailableAndIsNotLoadedEvenIfRequested()
    {
        var reader = new FakeReader { IsAvailable = false };
        var service = new FileAttributeService(new(), reader);
        Assert.False(service.IsEpmCmdletsAvailable);
        var result = await service.AnalyzeAsync(CreateFile(), useEpmCmdlets: true);
        Assert.Equal(0, reader.Calls);
        Assert.Contains(result.Warnings, warning => warning.Contains("EpmCmdlets.dll is unavailable"));
    }

    [Fact]
    public async Task ExplicitOptInReadsEpmAttributesButNeverReplacesIndependentHash()
    {
        var reader = new FakeReader
        {
            Values = new Dictionary<string, string?> { ["FileHash"] = new string('A', 64), ["ProductName"] = "EPM metadata" }
        };
        var path = CreateFile();
        var result = await new FileAttributeService(new(), reader).AnalyzeAsync(path, useEpmCmdlets: true);
        Assert.Equal(1, reader.Calls);
        Assert.Equal("EPM metadata", result.ProductName);
        Assert.Equal(new string('A', 64), result.EpmFileHash);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), result.Sha256);
        Assert.Contains(result.Warnings, warning => warning.Contains("differs"));
    }

    [Fact]
    public async Task CancellationIsNotTreatedAsAnOptionalModuleFailure()
    {
        var reader = new FakeReader { Error = new OperationCanceledException() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileAttributeService(new(), reader).AnalyzeAsync(CreateFile(), useEpmCmdlets: true));
    }

    [Fact]
    public void RealModuleAvailabilityChecksTheDocumentedPath()
    {
        Assert.Equal(@"C:\Program Files\Microsoft EPM Agent\EpmTools\EpmCmdlets.dll", EpmCmdletsReader.ModulePath);
        Assert.Equal(OperatingSystem.IsWindows() && File.Exists(EpmCmdletsReader.ModulePath), new EpmCmdletsReader().IsAvailable);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeReader : IEpmCmdletsReader
    {
        public bool IsAvailable { get; init; } = true;
        public int Calls { get; private set; }
        public Exception? Error { get; init; }
        public IReadOnlyDictionary<string, string?> Values { get; init; } = new Dictionary<string, string?>();
        public Task<IReadOnlyDictionary<string, string?>> ReadAsync(string path, CancellationToken cancellationToken)
        {
            Calls++;
            return Error is null ? Task.FromResult(Values) : Task.FromException<IReadOnlyDictionary<string, string?>>(Error);
        }
    }
}

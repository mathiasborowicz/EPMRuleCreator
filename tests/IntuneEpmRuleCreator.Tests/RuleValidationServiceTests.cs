using IntuneEpmRuleCreator.Core.Models;
using IntuneEpmRuleCreator.Core.Services;
using Xunit;
using System.Text;

namespace IntuneEpmRuleCreator.Tests;

public sealed class RuleValidationServiceTests
{
    private readonly RuleValidationService _service = new();

    [Fact]
    public void Sha256Rule_RequiresCorrectHash()
    {
        var rule = new ElevationRule { Name = "Editor", FileName = "editor.exe", MinimumFileVersion = "1.0", FileHash = new string('a', 64) };
        Assert.True(_service.Validate(rule, 0, true).CanSubmit);
        Assert.False(_service.Validate(rule with { FileHash = "bad" }, 0, true).CanSubmit);
    }

    [Fact]
    public void PolicyAtLimit_BlocksSubmission()
    {
        var rule = new ElevationRule { Name = "Editor", FileName = "editor.exe", MinimumFileVersion = "1.0", FileHash = new string('a', 64) };
        Assert.Contains(_service.Validate(rule, 100, true).Issues, issue => issue.Code == "policy.limit.reached" && issue.IsBlocking);
    }

    [Theory]
    [InlineData("installer.msi")]
    [InlineData("deploy.ps1")]
    public void SupportedFileTypes_AreAccepted(string fileName)
    {
        var rule = new ElevationRule { Name = "Rule", FileName = fileName, MinimumFileVersion = "1.0", FileHash = new string('b', 64) };
        Assert.True(_service.Validate(rule, 0, true).CanSubmit);
    }

    [Fact]
    public void UnsupportedType_IsBlocked()
    {
        var rule = new ElevationRule { Name = "Rule", FileName = "document.pdf", MinimumFileVersion = "1.0", FileHash = new string('b', 64) };
        Assert.Contains(_service.Validate(rule, 0, true).Issues, issue => issue.Code == "file.type.unsupported");
    }

    [Fact]
    public void CertificateRule_RequiresASelectedCertificateSource()
    {
        var rule = new ElevationRule
        {
            Name = "Signed app",
            FileName = "signed.exe",
            MinimumFileVersion = "1.0",
            IdentificationStrategy = IdentificationStrategy.CertificateAndAttributes
        };

        Assert.Contains(_service.Validate(rule, 0, true).Issues,
            issue => issue.Code == "certificate.required" && issue.IsBlocking);
        Assert.True(_service.Validate(rule with { ReusableCertificateSettingId = "reusable-id" }, 0, true).CanSubmit);
    }

    [Fact]
    public void CertificateRule_AcceptsAnalyzedPublisherCertificate()
    {
        var certificate = new CertificateAttributes("CN=Publisher", "CN=Issuer", "thumbprint",
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), true);
        var rule = new ElevationRule
        {
            Name = "Signed app",
            FileName = "signed.exe",
            MinimumFileVersion = "1.0",
            IdentificationStrategy = IdentificationStrategy.CertificateAndAttributes,
            PublisherCertificate = certificate
        };

        Assert.True(_service.Validate(rule, 0, true).CanSubmit);
    }

    [Fact]
    public void FileVersion_IsRequiredAndLimitedToThirtyCharacters()
    {
        var rule = new ElevationRule
        {
            Name = "Editor",
            FileName = "editor.exe",
            FileHash = new string('a', 64)
        };

        Assert.Contains(_service.Validate(rule, 0, true).Issues,
            issue => issue.Code == "file.version.required" && issue.IsBlocking);
        Assert.Contains(_service.Validate(rule with { MinimumFileVersion = new string('1', 31) }, 0, true).Issues,
            issue => issue.Code == "file.version.too_long" && issue.IsBlocking);
        Assert.True(_service.Validate(rule with { MinimumFileVersion = new string('1', 30) }, 0, true).CanSubmit);
    }

    [Fact]
    public async Task Sha256Hasher_CalculatesExpectedDigest()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("epm-rule-test"));
            var hash = await new Sha256FileHasher().ComputeAsync(path);
            Assert.Equal("6FC3184D56C5E19F4BE46DD689B38FCC7F3F1ABDF4322E19F114ADC78019F8DB", hash);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

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
        var rule = new ElevationRule { Name = "Editor", FileName = "editor.exe", MinimumFileVersion = "1.0.0.0", FileHash = new string('a', 64) };
        Assert.True(_service.Validate(rule, 0, true).CanSubmit);
        Assert.False(_service.Validate(rule with { FileHash = "bad" }, 0, true).CanSubmit);
        Assert.Contains(_service.Validate(rule with { FileName = string.Empty }, 0, true).Issues,
            issue => issue.Code == "file.name.required" && issue.IsBlocking);
        Assert.Contains(_service.Validate(rule with { FileHash = null }, 0, true).Issues,
            issue => issue.Code == "file.sha256.required" && issue.IsBlocking);
    }

    [Fact]
    public void PolicyAtLimit_BlocksSubmission()
    {
        var rule = new ElevationRule { Name = "Editor", FileName = "editor.exe", MinimumFileVersion = "1.0.0.0", FileHash = new string('a', 64) };
        Assert.Contains(_service.Validate(rule, 100, true).Issues, issue => issue.Code == "policy.limit.reached" && issue.IsBlocking);
    }

    [Fact]
    public void DenyAllChildProcessBehavior_IsAcceptedForUpload()
    {
        var rule = new ElevationRule
        {
            Name = "Blocked child processes",
            FileName = "editor.exe",
            FileHash = new string('c', 64),
            ChildProcessBehavior = ChildProcessBehavior.DenyAll
        };

        Assert.True(_service.Validate(rule, 0, true).CanSubmit);
    }

    [Theory]
    [InlineData("installer.msi")]
    [InlineData("deploy.ps1")]
    public void SupportedFileTypes_AreAccepted(string fileName)
    {
        var rule = new ElevationRule { Name = "Rule", FileName = fileName, MinimumFileVersion = "1.0.0.0", FileHash = new string('b', 64) };
        Assert.True(_service.Validate(rule, 0, true).CanSubmit);
    }

    [Fact]
    public void UnsupportedType_IsBlocked()
    {
        var rule = new ElevationRule { Name = "Rule", FileName = "document.pdf", MinimumFileVersion = "1.0.0.0", FileHash = new string('b', 64) };
        Assert.Contains(_service.Validate(rule, 0, true).Issues, issue => issue.Code == "file.type.unsupported");
    }

    [Fact]
    public void CertificateRule_RequiresASelectedCertificateSource()
    {
        var rule = new ElevationRule
        {
            Name = "Signed app",
            FileName = "signed.exe",
            MinimumFileVersion = "1.0.0.0",
            SignatureSource = SignatureSource.CertificateFile
        };

        Assert.Contains(_service.Validate(rule, 0, true).Issues,
            issue => issue.Code == "certificate.required" && issue.IsBlocking);
        Assert.True(_service.Validate(rule with { SignatureSource = SignatureSource.ReusableSettings, ReusableCertificateSettingId = "reusable-id" }, 0, true).CanSubmit);
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
            MinimumFileVersion = "1.0.0.0",
            SignatureSource = SignatureSource.CertificateFile,
            PublisherCertificate = certificate
        };

        Assert.True(_service.Validate(rule, 0, true).CanSubmit);
    }

    [Fact]
    public void CertificateRule_DoesNotRequireFileNameOrHash()
    {
        var certificate = new CertificateAttributes("CN=Publisher", "CN=Issuer", "thumbprint",
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), true);
        var rule = new ElevationRule
        {
            Name = "Signed app",
            SignatureSource = SignatureSource.CertificateFile,
            PublisherCertificate = certificate
        };

        Assert.True(_service.Validate(rule, 0, true).CanSubmit);
    }

    [Fact]
    public void LongFileDescription_IsNonBlockingAndWarnsThatUploadWillShortenIt()
    {
        var rule = new ElevationRule
        {
            Name = "Signed app",
            SignatureSource = SignatureSource.CertificateFile,
            PublisherCertificate = new CertificateAttributes("CN=Publisher", "CN=Issuer", "thumbprint",
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), true),
            FileDescription = new string('x', 60)
        };

        var result = _service.Validate(rule, 0, true);
        Assert.True(result.CanSubmit);
        Assert.Contains(result.Issues, issue => issue.Code == "file.description.truncated" && !issue.IsBlocking);
    }

    [Fact]
    public void RestrictArguments_RequiresAtLeastOneValidArgument()
    {
        var rule = new ElevationRule { Name = "Rule", FileName = "app.exe", FileHash = new string('a', 64), RestrictArguments = RestrictArgumentsMode.Allow };
        Assert.Contains(_service.Validate(rule, 0, true).Issues, issue => issue.Code == "arguments.required" && issue.IsBlocking);
        Assert.True(_service.Validate(rule with { ArgumentList = ["/quiet"] }, 0, true).CanSubmit);
        Assert.Contains(_service.Validate(rule with { ArgumentList = [new string('x', 252)] }, 0, true).Issues,
            issue => issue.Code == "arguments.invalid" && issue.IsBlocking);
    }

    [Fact]
    public void FileVersion_IsOptionalButMustMatchFourPartNumericFormatAndThirtyCharacterLimit()
    {
        var rule = new ElevationRule
        {
            Name = "Editor",
            FileName = "editor.exe",
            FileHash = new string('a', 64)
        };

        Assert.True(_service.Validate(rule, 0, true).CanSubmit);
        Assert.Contains(_service.Validate(rule with { MinimumFileVersion = "12345678.12345678.12345678.12345678" }, 0, true).Issues,
            issue => issue.Code == "file.version.too_long" && issue.IsBlocking);
        Assert.Contains(_service.Validate(rule with { MinimumFileVersion = "1.0" }, 0, true).Issues,
            issue => issue.Code == "file.version.invalid_format" && issue.IsBlocking);
        Assert.True(_service.Validate(rule with { MinimumFileVersion = "12345678.12345678.1234567.1234" }, 0, true).CanSubmit);
        Assert.Contains(_service.Validate(rule with { MinimumFileVersion = "1.1.0" }, 0, true).Issues,
            issue => issue.Code == "file.version.invalid_format" && issue.IsBlocking);
        Assert.True(RuleValidationService.IsValidMinimumFileVersion(null));
        Assert.False(RuleValidationService.IsValidMinimumFileVersion("1.1.0"));
        Assert.True(RuleValidationService.IsValidMinimumFileVersion("1.1.0.0"));
        Assert.Equal("1.1.0.0", RuleValidationService.NormalizeMinimumFileVersionToFourParts("1.1.0"));
        Assert.Equal("12.3.5.5", RuleValidationService.NormalizeMinimumFileVersionToFourParts("12.3.5.5"));
        Assert.Equal(string.Empty, RuleValidationService.NormalizeMinimumFileVersionToFourParts(string.Empty));
        Assert.True(RuleValidationService.IsValidMinimumFileVersion(RuleValidationService.NormalizeMinimumFileVersionToFourParts("1.1.0")));
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

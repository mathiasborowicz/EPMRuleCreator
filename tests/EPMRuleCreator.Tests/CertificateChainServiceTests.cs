using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EPMRuleCreator.Core.Models;
using EPMRuleCreator.Core.Services;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class CertificateChainServiceTests
{
    [Fact]
    public void BuildsCompleteChainFromProvidedIntermediatesAndRootWithoutInstallingThem()
    {
        using var certificates = new TestChain();
        var result = CertificateChainService.Build(certificates.Signer,
            [certificates.Root, certificates.Intermediate, certificates.Intermediate]);
        Assert.True(result.IsComplete);
        Assert.Equal(new[] { certificates.Signer.Thumbprint, certificates.Intermediate.Thumbprint, certificates.Root.Thumbprint },
            result.Certificates.Select(certificate => certificate.Thumbprint));
    }

    [Fact]
    public void MissingIssuerIsReportedAsIncompleteAndSignerIsPreserved()
    {
        using var certificates = new TestChain();
        var result = CertificateChainService.Build(certificates.Signer);
        Assert.False(result.IsComplete);
        Assert.Single(result.Certificates);
        Assert.Equal(certificates.Signer.CertificateBase64, result.Certificates[0].CertificateBase64);
        Assert.Contains(result.Status, status => status.Contains("PartialChain"));
    }

    [Theory]
    [InlineData(CertificateChainExportFormat.Pkcs7)]
    [InlineData(CertificateChainExportFormat.Pem)]
    public void ExportsEveryChainCertificateAndNoPrivateKeys(CertificateChainExportFormat format)
    {
        using var certificates = new TestChain();
        var result = CertificateChainService.Build(certificates.Signer, [certificates.Intermediate, certificates.Root]);
        var bytes = CertificateChainService.Export(result, format);
        var imported = new X509Certificate2Collection();
        try
        {
            if (format == CertificateChainExportFormat.Pem) imported.ImportFromPem(Encoding.ASCII.GetString(bytes));
            else
            {
#pragma warning disable SYSLIB0057 // PKCS#7 bundle import is not replaced by the single-certificate loader.
                imported.Import(bytes);
#pragma warning restore SYSLIB0057
            }
            Assert.Equal(3, imported.Count);
            Assert.All(imported.Cast<X509Certificate2>(), certificate => Assert.False(certificate.HasPrivateKey));
            Assert.Equal(result.Certificates.Select(certificate => certificate.Thumbprint).Order(),
                imported.Cast<X509Certificate2>().Select(certificate => certificate.Thumbprint).Order());
            if (format == CertificateChainExportFormat.Pem)
                Assert.Equal(result.Certificates.Select(certificate => certificate.Thumbprint), imported.Cast<X509Certificate2>().Select(certificate => certificate.Thumbprint));
        }
        finally { foreach (var certificate in imported) certificate.Dispose(); }
    }

    [Fact]
    public void EmptyChainCannotBeExported()
        => Assert.Throws<InvalidOperationException>(() => CertificateChainService.Export(new([], false, []), CertificateChainExportFormat.Pkcs7));

    [Fact]
    public void IndividualExportProducesOnePublicDerCerPerCertificateInChainOrder()
    {
        using var certificates = new TestChain();
        var chain = CertificateChainService.Build(certificates.Signer, [certificates.Intermediate, certificates.Root]);
        var files = CertificateChainService.ExportIndividualCertificates(chain);
        Assert.Equal(3, files.Count);
        Assert.StartsWith("01-signing-", files[0].FileName);
        Assert.StartsWith("02-intermediate-", files[1].FileName);
        Assert.StartsWith("03-root-", files[2].FileName);
        for (var i = 0; i < files.Count; i++)
        {
            Assert.Equal(Path.GetFileName(files[i].FileName), files[i].FileName);
            Assert.EndsWith(".cer", files[i].FileName);
            using var certificate = X509CertificateLoader.LoadCertificate(files[i].Data);
            Assert.Equal(chain.Certificates[i].Thumbprint, certificate.Thumbprint);
            Assert.False(certificate.HasPrivateKey);
        }
    }

    [Fact]
    public void PartialIndividualExportDoesNotInventMissingCertificates()
    {
        using var certificates = new TestChain();
        var chain = CertificateChainService.Build(certificates.Signer);
        var files = CertificateChainService.ExportIndividualCertificates(chain);
        Assert.Single(files);
        Assert.Equal(Convert.FromBase64String(certificates.Signer.CertificateBase64), files[0].Data);
    }

    [Fact]
    public void EmptyChainCannotBeExportedAsIndividualFiles()
        => Assert.Throws<InvalidOperationException>(() => CertificateChainService.ExportIndividualCertificates(new([], false, [])));

    private sealed class TestChain : IDisposable
    {
        private readonly RSA _rootKey = RSA.Create(2048);
        private readonly RSA _intermediateKey = RSA.Create(2048);
        private readonly RSA _signerKey = RSA.Create(2048);
        private readonly X509Certificate2 _root;
        private readonly X509Certificate2 _intermediate;
        private readonly X509Certificate2 _signer;
        public CertificateAttributes Root => CertificateChainService.Describe(_root);
        public CertificateAttributes Intermediate => CertificateChainService.Describe(_intermediate);
        public CertificateAttributes Signer => CertificateChainService.Describe(_signer);

        public TestChain()
        {
            var prefix = Guid.NewGuid().ToString("N");
            var start = DateTimeOffset.UtcNow.AddDays(-1);
            var end = DateTimeOffset.UtcNow.AddDays(30);
            var rootRequest = Request($"CN=Test Root {prefix}", _rootKey, ca: true);
            _root = rootRequest.CreateSelfSigned(start, end);
            var intermediateRequest = Request($"CN=Test Intermediate {prefix}", _intermediateKey, ca: true);
            using var publicIntermediate = intermediateRequest.Create(_root, start.AddHours(1), end.AddDays(-1), RandomNumberGenerator.GetBytes(16));
            _intermediate = publicIntermediate.CopyWithPrivateKey(_intermediateKey);
            var signerRequest = Request($"CN=Test Signer {prefix}", _signerKey, ca: false);
            _signer = signerRequest.Create(_intermediate, start.AddHours(2), end.AddDays(-2), RandomNumberGenerator.GetBytes(16));
        }

        private static CertificateRequest Request(string subject, RSA key, bool ca)
        {
            var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(ca ? X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign : X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            return request;
        }

        public void Dispose()
        {
            _root.Dispose(); _intermediate.Dispose(); _signer.Dispose();
            _rootKey.Dispose(); _intermediateKey.Dispose(); _signerKey.Dispose();
        }
    }
}

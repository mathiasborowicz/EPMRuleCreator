using EPMRuleCreator.Core.Models;

namespace EPMRuleCreator.Core.Services;

public sealed class AnalyzedCertificateSelection
{
    public IReadOnlyList<CertificateAttributes> Certificates { get; private set; } = [];
    public CertificateAttributes? Selected { get; private set; }

    public void Load(CertificateAttributes? signer, CertificateChainResult? chain)
    {
        IEnumerable<CertificateAttributes> candidates = signer is null ? [] : [signer];
        Certificates = candidates.Concat(chain?.Certificates ?? [])
            .DistinctBy(Identity, StringComparer.Ordinal).ToArray();
        Selected = Certificates.FirstOrDefault();
    }

    public bool Select(CertificateAttributes certificate)
    {
        var candidate = Certificates.FirstOrDefault(item => Identity(item) == Identity(certificate));
        if (candidate is null) return false;
        Selected = candidate;
        return true;
    }

    private static string Identity(CertificateAttributes certificate) => string.IsNullOrEmpty(certificate.CertificateBase64)
        ? certificate.Thumbprint + "|" + certificate.Subject : certificate.CertificateBase64;
}

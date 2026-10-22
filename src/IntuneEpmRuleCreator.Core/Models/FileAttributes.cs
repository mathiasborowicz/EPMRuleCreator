namespace IntuneEpmRuleCreator.Core.Models;

/// <summary>File metadata with provenance kept explicit so EPM output is never implied by fallback data.</summary>
public sealed record AnalyzedFileAttributes
{
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public required string Sha256 { get; init; }
    public string? EpmFileHash { get; init; }
    public string? EpmHashAlgorithm { get; init; }
    public string? ProductName { get; init; }
    public string? InternalName { get; init; }
    public string? Version { get; init; }
    public string? Description { get; init; }
    public string? CompanyName { get; init; }
    public string? SignatureStatus { get; init; }
    public CertificateAttributes? PublisherCertificate { get; init; }
    public CertificateChainResult? CertificateChain { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Available public certificates, signer first. Completeness is not a trust or revocation verdict.</summary>
public sealed record CertificateChainResult(
    IReadOnlyList<CertificateAttributes> Certificates,
    bool IsComplete,
    IReadOnlyList<string> Status);

public sealed record CertificateAttributes(
    string Subject,
    string Issuer,
    string Thumbprint,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool IsWithinValidityPeriod)
{
    /// <summary>Public X.509 certificate only; kept in memory for a future EPM payload.</summary>
    public string CertificateBase64 { get; init; } = string.Empty;
}

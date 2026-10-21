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
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record CertificateAttributes(
    string Subject,
    string Issuer,
    string Thumbprint,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool IsWithinValidityPeriod);

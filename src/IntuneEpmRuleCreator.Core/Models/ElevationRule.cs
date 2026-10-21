namespace IntuneEpmRuleCreator.Core.Models;

public enum IdentificationStrategy { Sha256, CertificateAndAttributes, Custom }
public enum ElevationType { UserConfirmed, Automatic, SupportApproved, Deny, CurrentUser }
public enum ChildProcessBehavior { RequireRuleToElevate, DenyAll, AllowAllElevated }

public sealed record ElevationRule
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string? FilePath { get; init; }
    public string? FileHash { get; init; }
    public string HashAlgorithm { get; init; } = "SHA256";
    public string? ProductName { get; init; }
    public string? FileDescription { get; init; }
    public string? InternalName { get; init; }
    public string? MinimumFileVersion { get; init; }
    public IdentificationStrategy IdentificationStrategy { get; init; } = IdentificationStrategy.Sha256;
    public CertificateAttributes? PublisherCertificate { get; init; }
    public string? ReusableCertificateSettingId { get; init; }
    public ElevationType ElevationType { get; init; } = ElevationType.UserConfirmed;
    public bool RequireBusinessJustification { get; init; }
    public bool RequireWindowsAuthentication { get; init; }
    public ChildProcessBehavior ChildProcessBehavior { get; init; } = ChildProcessBehavior.RequireRuleToElevate;
}

public sealed record ValidationIssue(string Code, string Message, bool IsBlocking);
public sealed record RuleValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    public bool CanSubmit => Issues.All(issue => !issue.IsBlocking);
}

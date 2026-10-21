using System.IO;
using System.Text.RegularExpressions;
using IntuneEpmRuleCreator.Core.Models;

namespace IntuneEpmRuleCreator.Core.Services;

public sealed class RuleValidationService
{
    private static readonly Regex Sha256Pattern = new("\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".ps1" };

    public RuleValidationResult Validate(ElevationRule rule, int existingRuleCount, bool hasPolicy)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(rule.Name))
            issues.Add(new("rule.name.required", "A rule name is required.", true));
        if (!hasPolicy)
            issues.Add(new("policy.required", "Select an existing policy or create a new one.", true));
        if (existingRuleCount < 0)
            issues.Add(new("policy.count.invalid", "The policy rule count is unavailable.", true));
        else if (existingRuleCount >= 100)
            issues.Add(new("policy.limit.reached", "An EPM Elevation Rules policy cannot contain more than 100 rules.", true));

        if (string.IsNullOrWhiteSpace(rule.FileName))
            issues.Add(new("file.name.required", "A file name is required.", true));
        else if (!SupportedExtensions.Contains(Path.GetExtension(rule.FileName)))
            issues.Add(new("file.type.unsupported", "Only .exe, .msi and .ps1 files are supported by this application.", true));

        if (rule.IdentificationStrategy == IdentificationStrategy.Sha256 &&
            (rule.HashAlgorithm != "SHA256" || string.IsNullOrWhiteSpace(rule.FileHash) || !Sha256Pattern.IsMatch(rule.FileHash)))
            issues.Add(new("file.sha256.invalid", "A 64-character SHA256 hash is required for hash-based identification.", true));

        if (rule.IdentificationStrategy == IdentificationStrategy.CertificateAndAttributes)
            issues.Add(new("certificate.unverified", "Certificate-based rule payload support has not been verified against the current Graph schema.", true));

        if (rule.ChildProcessBehavior == ChildProcessBehavior.AllowAllElevated)
            issues.Add(new("security.child-process.broad", "All child processes would run elevated. Review this high-risk choice carefully.", false));
        if (rule.ElevationType == ElevationType.Automatic)
            issues.Add(new("security.automatic-elevation", "Automatic elevation increases privilege escalation risk.", false));

        if (rule.FilePath is { Length: > 0 } path && IsWritableLocation(path))
            issues.Add(new("security.path.writable", "The file is in a user-writable or temporary location; path-based matching may be unsafe.", false));

        return new(issues);
    }

    private static bool IsWritableLocation(string path)
    {
        var normalized = path.Replace('/', '\\');
        return normalized.Contains("\\Users\\", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("\\Temp\\", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("\\AppData\\", StringComparison.OrdinalIgnoreCase);
    }
}

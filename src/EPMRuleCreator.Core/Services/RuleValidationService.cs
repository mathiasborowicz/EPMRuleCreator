using System.IO;
using System.Text.RegularExpressions;
using EPMRuleCreator.Core.Models;

namespace EPMRuleCreator.Core.Services;

public sealed class RuleValidationService
{
    private static readonly Regex Sha256Pattern = new("\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex FileVersionPattern = new("\\A[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+\\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
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

        var hashIdentification = rule.SignatureSource == SignatureSource.NotConfigured;
        if (hashIdentification && string.IsNullOrWhiteSpace(rule.FileName))
            issues.Add(new("file.name.required", "A file name is required.", true));
        else if (!string.IsNullOrWhiteSpace(rule.FileName) && !SupportedExtensions.Contains(Path.GetExtension(rule.FileName)))
            issues.Add(new("file.type.unsupported", "Only .exe, .msi and .ps1 files are supported by this application.", true));

        if (!string.IsNullOrWhiteSpace(rule.MinimumFileVersion) &&
            (NormalizeMinimumFileVersionToFourParts(rule.MinimumFileVersion)?.Length ?? 0) > 30)
            issues.Add(new("file.version.too_long", "The file version must not exceed 30 characters.", true));
        else if (!string.IsNullOrWhiteSpace(rule.MinimumFileVersion) && !FileVersionPattern.IsMatch(rule.MinimumFileVersion))
            issues.Add(new("file.version.invalid_format", "The file version must contain exactly four numeric parts separated by dots, for example 1.1.0.0 or 12.3.5.5.", true));

        if (rule.FileDescription?.Length > 50)
            issues.Add(new("file.description.truncated", "Intune allows at most 50 characters for File description; the uploaded value will be shortened to 50 characters.", false));

        if (hashIdentification && string.IsNullOrWhiteSpace(rule.FileHash))
            issues.Add(new("file.sha256.required", "A SHA256 hash is required when no certificate is selected.", true));
        else if (!string.IsNullOrWhiteSpace(rule.FileHash) &&
            (rule.HashAlgorithm != "SHA256" || !Sha256Pattern.IsMatch(rule.FileHash)))
            issues.Add(new("file.sha256.invalid", "A supplied file hash must be a 64-character SHA256 value.", true));

        if (rule.SignatureSource == SignatureSource.CertificateFile && rule.PublisherCertificate is null)
            issues.Add(new("certificate.required", "Select a certificate.", true));
        if (rule.SignatureSource == SignatureSource.ReusableSettings && string.IsNullOrWhiteSpace(rule.ReusableCertificateSettingId))
            issues.Add(new("certificate.reusable.required", "Select an EPM reusable certificate setting.", true));
        if (rule.RestrictArguments == RestrictArgumentsMode.Allow)
        {
            if (rule.ArgumentList.Count == 0)
                issues.Add(new("arguments.required", "Add at least one Argument List entry when Restrict Arguments is set to Allow.", true));
            if (rule.ArgumentList.Any(argument => string.IsNullOrWhiteSpace(argument) || argument.Length > 251))
                issues.Add(new("arguments.invalid", "Each Argument List entry must contain 1 to 251 characters.", true));
            if (rule.ArgumentList.Count > 600)
                issues.Add(new("arguments.too_many", "Argument List cannot contain more than 600 entries.", true));
        }

        if (rule.ChildProcessBehavior == ChildProcessBehavior.AllowAllElevated)
            issues.Add(new("security.child-process.broad", "All child processes would run elevated. Review this high-risk choice carefully.", false));
        if (rule.ElevationType == ElevationType.Automatic)
            issues.Add(new("security.automatic-elevation", "Automatic elevation increases privilege escalation risk.", false));

        if (rule.FilePath is { Length: > 0 } path && IsWritableLocation(path))
            issues.Add(new("security.path.writable", "The file is in a user-writable or temporary location; path-based matching may be unsafe.", false));

        return new(issues);
    }

    public static bool IsValidMinimumFileVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) ||
        (version.Length <= 30 && FileVersionPattern.IsMatch(version));

    public static string? NormalizeMinimumFileVersionToFourParts(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return version;
        var parts = version.Trim().Split('.');
        if (parts.Length > 4 || parts.Any(part => part.Length == 0 || part.Any(character => character < '0' || character > '9')))
            return version;
        return string.Join(".", parts.Concat(Enumerable.Repeat("0", 4 - parts.Length)));
    }

    private static bool IsWritableLocation(string path)
    {
        var normalized = path.Replace('/', '\\');
        return normalized.Contains("\\Users\\", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("\\Temp\\", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("\\AppData\\", StringComparison.OrdinalIgnoreCase);
    }
}

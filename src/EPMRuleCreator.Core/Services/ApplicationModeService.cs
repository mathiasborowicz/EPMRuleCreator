using Microsoft.Win32;
using System.Security;
using System.Runtime.Versioning;
using System.Diagnostics;

namespace EPMRuleCreator.Core.Services;

public enum ApplicationMode { Portable, Msi }

public static class ApplicationModeService
{
    public const string MsiInstallerRegistryPath = @"Software\EPMRuleCreator\Installer";
    private const string WindowsInstallerUninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static bool IsMsiInstalled(string executablePath) =>
        OperatingSystem.IsWindows() && FindMsiRegistration(executablePath);

    public static bool IsInstalled(string executablePath, string localAppData) => IsMsiInstalled(executablePath);

    public static ApplicationMode Resolve(string executablePath, string localAppData)
    {
        if (IsMsiInstalled(executablePath)) return ApplicationMode.Msi;
        return ApplicationMode.Portable;
    }

    // Kept public so the registry matching rule can be tested without modifying a user's registry.
    public static bool MatchesMsiInstallation(
        string executablePath,
        string registeredExecutablePath,
        string productCode,
        string uninstallProductCode,
        string? executableVersion = null,
        string? registeredVersion = null,
        string? uninstallVersion = null,
        string? registeredInstallLocation = null,
        string? uninstallInstallLocation = null)
    {
        return PathsEqual(executablePath, registeredExecutablePath)
            && ProductCodesEqual(productCode, uninstallProductCode)
            && VersionsEqual(executableVersion, registeredVersion)
            && VersionsEqual(registeredVersion, uninstallVersion)
            && InstallLocationsMatch(executablePath, registeredInstallLocation, uninstallInstallLocation);
    }

    [SupportedOSPlatform("windows")]
    private static bool FindMsiRegistration(string executablePath)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var installer = root.OpenSubKey(MsiInstallerRegistryPath);
                var registeredExecutablePath = installer?.GetValue("ExecutablePath") as string;
                var productCode = installer?.GetValue("ProductCode") as string;
                var registeredVersion = installer?.GetValue("Version") as string;
                var registeredInstallLocation = installer?.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(registeredExecutablePath)
                    || string.IsNullOrWhiteSpace(productCode)) continue;
                if (TryGetMatchingUninstallEntry(productCode, out var uninstallProductCode, out var uninstallVersion, out var uninstallInstallLocation)
                    && MatchesMsiInstallation(
                        executablePath,
                        registeredExecutablePath,
                        productCode,
                        uninstallProductCode,
                        GetExecutableVersion(executablePath),
                        registeredVersion,
                        uninstallVersion,
                        registeredInstallLocation,
                        uninstallInstallLocation))
                    return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException
                or PlatformNotSupportedException)
            {
                // Registry access is optional for portable use and must not prevent local analysis.
            }
        }
        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetMatchingUninstallEntry(
        string productCode,
        out string uninstallProductCode,
        out string uninstallVersion,
        out string uninstallInstallLocation)
    {
        uninstallProductCode = string.Empty;
        uninstallVersion = string.Empty;
        uninstallInstallLocation = string.Empty;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var uninstallRoot = root.OpenSubKey(WindowsInstallerUninstallRegistryPath);
                if (uninstallRoot is null) continue;
                foreach (var subKeyName in uninstallRoot.GetSubKeyNames())
                {
                    if (!ProductCodesEqual(productCode, subKeyName)) continue;
                    using var uninstallEntry = uninstallRoot.OpenSubKey(subKeyName);
                    var registeredProductCode = uninstallEntry?.GetValue("ProductCode") as string;
                    if (!string.IsNullOrWhiteSpace(registeredProductCode)
                        && !ProductCodesEqual(productCode, registeredProductCode)) continue;
                    uninstallProductCode = registeredProductCode ?? subKeyName;
                    uninstallVersion = uninstallEntry?.GetValue("DisplayVersion") as string
                        ?? uninstallEntry?.GetValue("Version") as string
                        ?? string.Empty;
                    uninstallInstallLocation = uninstallEntry?.GetValue("InstallLocation") as string ?? string.Empty;
                    return true;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException
                or PlatformNotSupportedException or ArgumentException)
            {
                // An inaccessible uninstall hive is treated as not matching.
            }
        }
        return false;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            var normalizedLeft = Path.GetFullPath(left.Trim().Trim('"'));
            var normalizedRight = Path.GetFullPath(right.Trim().Trim('"'));
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool ProductCodesEqual(string left, string right) =>
        string.Equals(left.Trim().Trim('{', '}'), right.Trim().Trim('{', '}'), StringComparison.OrdinalIgnoreCase);

    private static string? GetExecutableVersion(string executablePath)
    {
        try { return FileVersionInfo.GetVersionInfo(executablePath).FileVersion; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool VersionsEqual(string? left, string? right)
    {
        // Version is optional because the MSI marker contract requires only ExecutablePath and ProductCode.
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return true;
        return Version.TryParse(left.Trim(), out var leftVersion)
            && Version.TryParse(right.Trim(), out var rightVersion)
            && leftVersion == rightVersion;
    }

    private static bool InstallLocationsMatch(string executablePath, string? registeredInstallLocation, string? uninstallInstallLocation)
    {
        var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        return !string.IsNullOrWhiteSpace(executableDirectory)
            && (string.IsNullOrWhiteSpace(registeredInstallLocation) || PathsEqual(executableDirectory, registeredInstallLocation))
            && (string.IsNullOrWhiteSpace(uninstallInstallLocation) || PathsEqual(executableDirectory, uninstallInstallLocation));
    }
}

namespace EPMRuleCreator.Core.Services;

public enum ApplicationMode { Choose, Portable, Installed }

public static class ApplicationModeService
{
    public static string PortableMarker(string executablePath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executablePath))!, "EPMRuleCreator.portable");

    public static bool IsInstalled(string executablePath, string localAppData) => string.Equals(
        Path.GetFullPath(executablePath), ExplorerApplicationDeployment.GetInstalledPath(localAppData), StringComparison.OrdinalIgnoreCase);

    public static ApplicationMode Resolve(string executablePath, string localAppData) => IsInstalled(executablePath, localAppData)
        ? ApplicationMode.Installed : File.Exists(PortableMarker(executablePath)) ? ApplicationMode.Portable : ApplicationMode.Choose;

    public static void RememberPortable(string executablePath)
    {
        var marker = PortableMarker(executablePath);
        if (!File.Exists(marker))
        {
            using var file = new FileStream(marker, FileMode.CreateNew, FileAccess.Write);
        }
    }
}

using EPMRuleCreator.Core.Services;

namespace EPMRuleCreator.UI;

internal static class ApplicationInstallation
{
    internal static ApplicationMode CurrentMode => Environment.ProcessPath is { } path
        ? ApplicationModeService.Resolve(path, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
        : ApplicationMode.Portable;
    internal static bool IsMsiInstalled => CurrentMode == ApplicationMode.Msi;
    internal static bool IsInstalled => IsMsiInstalled;
}

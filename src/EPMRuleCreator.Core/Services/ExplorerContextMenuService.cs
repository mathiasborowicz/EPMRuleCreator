using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace EPMRuleCreator.Core.Services;

public static class ExplorerContextMenuService
{
    private static readonly string[] Extensions = [".exe", ".msi", ".ps1"];
    private static string MenuKey(string extension) =>
        @"Software\Classes\SystemFileAssociations\" + extension + @"\shell\CreateEPMRule";
    private static string LegacyMenuKey(string extension) =>
        @"Software\Classes\SystemFileAssociations\" + extension + @"\shell\CreateIntuneEpmRule";

    public static IReadOnlyList<string> GetOwnedMenuKeys() => Extensions.SelectMany(extension => new[] { MenuKey(extension), LegacyMenuKey(extension) }).ToArray();

    [SupportedOSPlatform("windows")]
    public static bool IsRegistered()
    {
        foreach (var menuKey in GetOwnedMenuKeys())
        {
            using var key = Registry.CurrentUser.OpenSubKey(menuKey);
            if (key is not null) return true; // Also allow removing a partial registration.
        }
        return false;
    }

    public static string BuildCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.IndexOfAny(['"', '\r', '\n', '\0']) >= 0)
            throw new ArgumentException("The executable path contains invalid characters.", nameof(executablePath));
        return $"\"{executablePath}\" \"%1\"";
    }

    [SupportedOSPlatform("windows")]
    public static void Register(string executablePath)
    {
        var fullPath = ExplorerApplicationDeployment.Install(executablePath,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var command = BuildCommand(fullPath);
        foreach (var extension in Extensions)
        {
            using var key = Registry.CurrentUser.CreateSubKey(MenuKey(extension));
            key.SetValue(string.Empty, "Create EPM Rule", RegistryValueKind.String);
            key.SetValue("Icon", $"\"{fullPath}\",0", RegistryValueKind.String);
            using var commandKey = key.CreateSubKey("command");
            commandKey.SetValue(string.Empty, command, RegistryValueKind.String);
        }
        // Only remove legacy entries after the replacement is deployed and registered.
        foreach (var extension in Extensions)
            Registry.CurrentUser.DeleteSubKeyTree(LegacyMenuKey(extension), throwOnMissingSubKey: false);
        NotifyExplorer();
    }

    [SupportedOSPlatform("windows")]
    public static void Remove()
    {
        foreach (var menuKey in GetOwnedMenuKeys())
            Registry.CurrentUser.DeleteSubKeyTree(menuKey, throwOnMissingSubKey: false);
        NotifyExplorer();
    }

    [SupportedOSPlatform("windows")]
    private static void NotifyExplorer() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}

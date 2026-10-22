using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace IntuneEpmRuleCreator.Core.Services;

public static class ExplorerContextMenuService
{
    private static readonly string[] Extensions = [".exe", ".msi", ".ps1"];
    private static string MenuKey(string extension) =>
        @"Software\Classes\SystemFileAssociations\" + extension + @"\shell\CreateIntuneEpmRule";

    [SupportedOSPlatform("windows")]
    public static bool IsRegistered()
    {
        foreach (var extension in Extensions)
        {
            using var key = Registry.CurrentUser.OpenSubKey(MenuKey(extension));
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
            key.SetValue(string.Empty, "Create Intune EPM Rule", RegistryValueKind.String);
            key.SetValue("Icon", $"\"{fullPath}\",0", RegistryValueKind.String);
            using var commandKey = key.CreateSubKey("command");
            commandKey.SetValue(string.Empty, command, RegistryValueKind.String);
        }
        NotifyExplorer();
    }

    [SupportedOSPlatform("windows")]
    public static void Remove()
    {
        foreach (var extension in Extensions)
            Registry.CurrentUser.DeleteSubKeyTree(MenuKey(extension), throwOnMissingSubKey: false);
        NotifyExplorer();
    }

    [SupportedOSPlatform("windows")]
    private static void NotifyExplorer() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}

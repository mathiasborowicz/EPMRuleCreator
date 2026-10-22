using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using EPMRuleCreator.Core.Services;

namespace EPMRuleCreator.UI;

internal static class ApplicationInstallation
{
    internal static string InstalledPath => ExplorerApplicationDeployment.GetInstalledPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    internal static bool Exists => File.Exists(InstalledPath);
    internal static bool IsInstalled => Environment.ProcessPath is { } path && ApplicationModeService.IsInstalled(path, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static void AlignExistingMenu(string target)
    {
        if (ExplorerContextMenuService.IsRegistered()) ExplorerContextMenuService.Register(target);
    }

    internal static void InstallOrOpen(IEnumerable<string> arguments)
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("The application path is unavailable.");
        // A downloaded or old portable copy must never silently replace an existing installation.
        var installed = ExplorerApplicationDeployment.Install(source, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), overwriteExisting: false);
        CreateShortcut(installed);
        AlignExistingMenu(installed);
        var start = new ProcessStartInfo(installed) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(installed)! };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not open the installed app.");
    }

    private static void CreateShortcut(string executable)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        Directory.CreateDirectory(programs);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcuts are unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        object? shortcutObject = null;
        try
        {
            dynamic shortcut = shell.CreateShortcut(Path.Combine(programs, "EPMRuleCreator.lnk"));
            shortcutObject = shortcut;
            shortcut.TargetPath = executable;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
            shortcut.IconLocation = executable + ",0";
            shortcut.Description = "EPMRuleCreator";
            shortcut.Save();
        }
        finally
        {
            if (shortcutObject is not null) Marshal.FinalReleaseComObject(shortcutObject);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}

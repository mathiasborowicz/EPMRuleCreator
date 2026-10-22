using System.Windows;
using System.IO;
using EPMRuleCreator.Core.Services;

namespace EPMRuleCreator.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Apply(ThemeService.ResolveStartupTheme(null));
        var executable = Environment.ProcessPath;
        if (executable is not null && Path.GetFileName(executable).Equals("EPMRuleCreator.exe", StringComparison.OrdinalIgnoreCase)
            && ApplicationModeService.Resolve(executable, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) == ApplicationMode.Choose)
        {
            var choice = new LaunchModeWindow();
            if (choice.ShowDialog() != true) { Shutdown(); return; }
            if (choice.UseInstallation) { Shutdown(); return; }
        }
        var window = new MainWindow();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }
}

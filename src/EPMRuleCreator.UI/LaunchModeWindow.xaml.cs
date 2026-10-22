using System.IO;
using System.Windows;
using EPMRuleCreator.Core.Services;

namespace EPMRuleCreator.UI;

public partial class LaunchModeWindow : Window
{
    private readonly string[] _arguments;
    public bool UseInstallation { get; private set; }

    public LaunchModeWindow() : this(Environment.GetCommandLineArgs().Skip(1)) { }

    public LaunchModeWindow(IEnumerable<string> arguments)
    {
        _arguments = arguments.ToArray();
        InitializeComponent();
        InstallButton.ToolTip = ApplicationInstallation.InstalledPath;
        try { AddContextMenuCheckBox.IsChecked = ExplorerContextMenuService.IsRegistered(); }
        catch { /* Optional integration must not prevent choosing a mode. */ }
        SourceInitialized += (_, _) => ThemeService.ApplyWindowFrame(this, ThemeService.ResolveStartupTheme(null));
        if (ApplicationInstallation.Exists)
        {
            InstallButton.Content = "Open installed app";
        }
    }

    private void Portable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplicationModeService.RememberPortable(Environment.ProcessPath!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Read-only media still support portable use, but the choice cannot be remembered.
        }
        DialogResult = true;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplicationInstallation.InstallOrOpen(_arguments, AddContextMenuCheckBox.IsChecked == true);
            UseInstallation = true;
            DialogResult = true;
        }
        catch (Exception exception) { Status.Text = exception.Message; }
    }
}

using System.IO;
using System.Windows;
using EPMRuleCreator.Core.Services;

namespace EPMRuleCreator.UI;

public partial class LaunchModeWindow : Window
{
    public bool UseInstallation { get; private set; }

    public LaunchModeWindow()
    {
        InitializeComponent();
        InstallButton.ToolTip = ApplicationInstallation.InstalledPath;
        ExistingInstallationHint.Text = "After installation, you can delete the downloaded source EXE. Start the app from the Start menu.";
        SourceInitialized += (_, _) => ThemeService.ApplyWindowFrame(this, ThemeService.ResolveStartupTheme(null));
        if (ApplicationInstallation.Exists)
        {
            InstallButton.Content = "Open installed app";
            ExistingInstallationHint.Text = "An installation already exists. It will be opened, not overwritten. Use Updates there for a newer release.";
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
        try { ApplicationInstallation.AlignExistingMenu(Environment.ProcessPath!); }
        catch (Exception exception) { Status.Text = exception.Message; return; }
        DialogResult = true;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplicationInstallation.InstallOrOpen(Environment.GetCommandLineArgs().Skip(1));
            UseInstallation = true;
            DialogResult = true;
        }
        catch (Exception exception) { Status.Text = exception.Message; }
    }
}

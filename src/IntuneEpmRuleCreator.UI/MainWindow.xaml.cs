using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using IntuneEpmRuleCreator.UI.ViewModels;

namespace IntuneEpmRuleCreator.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;
        var selectedPath = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            viewModel.FilePath = selectedPath;
            Loaded += (_, _) => viewModel.AnalyzeCommand.Execute(null);
        }
    }

    private void AccountPopup_Opened(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel { GraphConnected: true })
            CloseAccountButton.Focus();
        else
            ClientIdInput.Focus();
    }

    private void CloseAccount_Click(object sender, RoutedEventArgs e) => CloseAccountPanel();

    private void AccountPopup_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseAccountPanel();
        e.Handled = true;
    }

    private void CloseAccountPanel()
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.IsAccountMenuOpen = false;
        AccountButton.Focus();
    }

    private async void ReusableCertificates_DropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            await viewModel.EnsureReusableCertificateSettingsAsync();
    }

    private async void ElevationPolicies_DropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            await viewModel.EnsureElevationPoliciesAsync();
    }

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select an elevation rule file",
            Filter = "Supported files (*.exe;*.msi;*.ps1)|*.exe;*.msi;*.ps1|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true && DataContext is MainViewModel viewModel)
            viewModel.FilePath = dialog.FileName;
    }

    private void ChooseCertificateFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a certificate file",
            Filter = "Certificate files (*.cer;*.crt;*.pem;*.der)|*.cer;*.crt;*.pem;*.der|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true && DataContext is MainViewModel viewModel)
        {
            try { viewModel.LoadCertificateFile(dialog.FileName); }
            catch (Exception exception) { MessageBox.Show(this, $"Could not read the certificate file: {exception.Message}", "Certificate file", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}

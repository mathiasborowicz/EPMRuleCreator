using System.Windows;
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
}

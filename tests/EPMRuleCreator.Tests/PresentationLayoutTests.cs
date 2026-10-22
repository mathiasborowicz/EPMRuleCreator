using System.Text.RegularExpressions;
using System.Xml.Linq;
using EPMRuleCreator.Core.Models;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class PresentationLayoutTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void FilePickerInputAndButtonsShareTheSameRowHeightAndVerticalAlignment()
    {
        var window = XDocument.Load(Source("MainWindow.xaml"));
        var controls = window.Descendants().Where(element => new[] { "SelectedFileInput", "ChooseFileButton", "AnalyzeFileButton" }
            .Contains((string?)element.Attribute(Xaml + "Name"))).ToArray();
        Assert.Equal(3, controls.Length);
        Assert.All(controls, control =>
        {
            Assert.Equal("1", (string?)control.Attribute("Grid.Row"));
            Assert.Equal("40", (string?)control.Attribute("Height"));
            Assert.Equal("Center", (string?)control.Attribute("VerticalAlignment"));
            Assert.Equal("FilePickerGrid", (string?)control.Parent?.Attribute(Xaml + "Name"));
        });
        var option = window.Descendants(Presentation + "CheckBox").Single(element => (string?)element.Attribute("Content") == "Use EpmCmdlets");
        Assert.Equal("2", (string?)option.Attribute("Grid.Row"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothPalettesDefineEverySharedBrush(bool dark)
    {
        var app = XDocument.Load(Source("App.xaml"));
        var brushes = app.Descendants(Presentation + "SolidColorBrush").Select(element => (string)element.Attribute(Xaml + "Key")!).Order().ToArray();
        Assert.Equal(brushes, ThemePalette.GetColors(dark).Keys.Order());
        foreach (var file in new[] { "App.xaml", "MainWindow.xaml" })
            Assert.DoesNotMatch(@"\{StaticResource \w+Brush\}", File.ReadAllText(Source(file)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextColorsHaveReadableContrastInBothThemes(bool dark)
    {
        var colors = ThemePalette.GetColors(dark);
        foreach (var text in new[] { "InkBrush", "MutedBrush", "WarningBrush", "ErrorBrush" })
            Assert.True(Contrast(colors[text], colors["CardBrush"]) >= 4.5, $"{text} must have readable contrast.");
        Assert.True(Contrast(colors["OnPrimaryBrush"], colors["PrimaryBrush"]) >= 4.5);
    }

    [Fact]
    public void TemplatesRetainTextEditingDropdownAndKeyboardFocusSupport()
    {
        var app = File.ReadAllText(Source("App.xaml"));
        Assert.Contains("PART_ContentHost", app);
        Assert.Contains("PART_Popup", app);
        Assert.Contains("SelectionBoxItemTemplate", app);
        Assert.Contains("IsKeyboardFocused", app);
        var window = File.ReadAllText(Source("MainWindow.xaml"));
        Assert.Contains("{Binding IsDarkTheme, Mode=TwoWay}", window);
    }

    [Fact]
    public void UpdateSettingsAreAvailableWithoutEntraAndStartupCheckIsOptional()
    {
        var window = XDocument.Load(Source("MainWindow.xaml"));
        Assert.Single(window.Descendants(Presentation + "CheckBox"), element =>
            (string?)element.Attribute("IsChecked") == "{Binding CheckForUpdatesAtStartup, Mode=TwoWay}");
        Assert.Single(window.Descendants(Presentation + "Button"), element =>
            (string?)element.Attribute("Command") == "{Binding UpdateNowCommand}");
        var viewModel = File.ReadAllText(Source("ViewModels/MainViewModel.cs"));
        Assert.Contains("return CheckForUpdatesAtStartup ? CheckForUpdatesAsync() : Task.CompletedTask;", viewModel);
        Assert.Contains("_startupUpdateChecked", viewModel);
    }

    [Fact]
    public void UpdaterVerifiesBeforeUnblockingAndWaitsForExitBeforeReplacing()
    {
        var installer = File.ReadAllText(Source("UpdateInstaller.cs"));
        Assert.True(installer.IndexOf("UpdatePackageService.DownloadAsync", StringComparison.Ordinal)
            < installer.IndexOf("File.Delete(executable + \":Zone.Identifier\")", StringComparison.Ordinal));
        Assert.Contains("fileVersion.ProductName != \"EPMRuleCreator\"", installer);
        Assert.DoesNotContain("runas", installer, StringComparison.OrdinalIgnoreCase);
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source("UpdateInstaller.cs"))!, "../.."));
        var script = File.ReadAllText(Path.Combine(root, "scripts", "Install-AppUpdate.ps1"));
        Assert.True(script.IndexOf("WaitForExit", StringComparison.Ordinal) < script.IndexOf("[IO.File]::Replace", StringComparison.Ordinal));
        Assert.True(script.IndexOf("[IO.File]::Replace", StringComparison.Ordinal) < script.IndexOf("[IO.File]::Delete($destination + ':Zone.Identifier')", StringComparison.Ordinal));
        Assert.Contains("$destination + '.previous'", script);
        Assert.Contains("[IO.File]::Copy($backup, $destination, $true)", script);
        Assert.DoesNotContain("-Recurse", script);
        Assert.DoesNotContain("ExecutionPolicy", installer);
    }

    private static double Contrast(string a, string b)
    {
        static double Luminance(string color)
        {
            var channels = Regex.Matches(color[1..], "..").Select(match => Convert.ToInt32(match.Value, 16) / 255.0)
                .Select(value => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4)).ToArray();
            return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
        }
        var first = Luminance(a);
        var second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static string Source(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "EPMRuleCreator.sln")))
                return Path.Combine(directory.FullName, "src", "EPMRuleCreator.UI", file);
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

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
        foreach (var surface in new[] { "CardBrush", "SurfaceBrush", "HeaderStartBrush", "HeaderEndBrush" })
        {
            Assert.True(Contrast(colors["InkBrush"], colors[surface]) >= 4.5, $"Text must remain readable on {surface}.");
            Assert.True(Contrast(colors["AccentBrush"], colors[surface]) >= 4.5, $"Accent text must remain readable on {surface}.");
        }
    }

    [Fact]
    public void TemplatesRetainTextEditingDropdownAndKeyboardFocusSupport()
    {
        var app = File.ReadAllText(Source("App.xaml"));
        Assert.Contains("PART_ContentHost", app);
        Assert.Contains("PART_Popup", app);
        Assert.Contains("SelectionBoxItemTemplate", app);
        Assert.Contains("ContentTemplateSelector=\"{TemplateBinding ItemTemplateSelector}\"", app);
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
        Assert.True(script.IndexOf("[IO.File]::Replace", StringComparison.Ordinal) < script.IndexOf("Unblock-File -LiteralPath $destination -ErrorAction Stop", StringComparison.Ordinal));
        Assert.DoesNotContain("[IO.File]::Delete($destination + ':Zone.Identifier')", script);
        Assert.Contains("$destination + '.previous'", script);
        Assert.Contains("[IO.File]::Copy($backup, $destination, $true)", script);
        Assert.DoesNotContain("-Recurse", script);
        Assert.DoesNotContain("ExecutionPolicy", installer);
    }

    [Fact]
    public void UpdatesButtonSharesTheStandardButtonTemplateAndThemeStates()
    {
        var app = XDocument.Load(Source("App.xaml"));
        var button = app.Descendants(Presentation + "Style").Single(style => (string?)style.Attribute("TargetType") == "Button");
        var toggle = app.Descendants(Presentation + "Style").Single(style => (string?)style.Attribute(Xaml + "Key") == "HeaderMenuButtonStyle");
        Assert.Equal("{StaticResource ActionButtonStyle}", (string?)button.Attribute("BasedOn"));
        Assert.Equal((string?)button.Attribute("BasedOn"), (string?)toggle.Attribute("BasedOn"));
        var shared = app.Descendants(Presentation + "Style").Single(style => (string?)style.Attribute(Xaml + "Key") == "ActionButtonStyle");
        Assert.Equal("ButtonBase", (string?)shared.Attribute("TargetType"));
        Assert.Contains(shared.Descendants(Presentation + "Trigger"), trigger => (string?)trigger.Attribute("Property") == "IsKeyboardFocused");
        Assert.Contains(shared.Descendants(Presentation + "Trigger"), trigger => (string?)trigger.Attribute("Property") == "IsMouseOver");
        Assert.Contains(toggle.Descendants(Presentation + "Trigger"), trigger => (string?)trigger.Attribute("Property") == "IsChecked");
        var window = XDocument.Load(Source("MainWindow.xaml"));
        var updates = window.Descendants(Presentation + "ToggleButton").Single(element => (string?)element.Attribute(Xaml + "Name") == "UpdatesButton");
        Assert.Equal("{StaticResource HeaderMenuButtonStyle}", (string?)updates.Attribute("Style"));
        Assert.Equal("{Binding IsUpdatesMenuOpen, Mode=TwoWay}", (string?)updates.Attribute("IsChecked"));
    }

    [Fact]
    public void ModeChoiceRunsBeforeMainWindowAndMenuRegistrationNeverDeploys()
    {
        var app = XDocument.Load(Source("App.xaml"));
        Assert.Null(app.Root!.Attribute("StartupUri"));
        Assert.Equal("OnExplicitShutdown", (string?)app.Root.Attribute("ShutdownMode"));
        var startup = File.ReadAllText(Source("App.xaml.cs"));
        Assert.True(startup.IndexOf("choice.ShowDialog()", StringComparison.Ordinal) < startup.IndexOf("new MainWindow()", StringComparison.Ordinal));
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source("App.xaml.cs"))!, "../.."));
        var menu = File.ReadAllText(Path.Combine(root, "src", "EPMRuleCreator.Core", "Services", "ExplorerContextMenuService.cs"));
        Assert.DoesNotContain("ExplorerApplicationDeployment.Install", menu);
        var script = File.ReadAllText(Path.Combine(root, "scripts", "Register-ExplorerMenu.ps1"));
        Assert.DoesNotContain("[IO.File]::Copy", script);
        Assert.DoesNotContain("managedPath", script);
        var installation = File.ReadAllText(Source("ApplicationInstallation.cs"));
        Assert.Contains("overwriteExisting: false", installation);
        Assert.Contains("Environment.SpecialFolder.Programs", installation);
        Assert.Contains("start.ArgumentList.Add(argument)", installation);
    }

    [Fact]
    public void BrandedHeaderUsesLiveThemeResourcesAndKeepsExistingActions()
    {
        var window = XDocument.Load(Source("MainWindow.xaml"));
        var header = window.Descendants(Presentation + "Border").Single(element => (string?)element.Attribute(Xaml + "Name") == "AppHeader");
        Assert.Equal("{DynamicResource HeaderBackgroundBrush}", (string?)header.Attribute("Background"));
        foreach (var name in new[] { "ThemeSwitch", "UpdatesButton", "AccountButton" })
            Assert.Contains(header.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == name);
        Assert.DoesNotContain(header.Descendants(Presentation + "TextBlock"), element => (string?)element.Attribute("Text") == "ZERO TRUST STORIES");
        Assert.Contains("app.Resources[\"HeaderBackgroundBrush\"] = new LinearGradientBrush", File.ReadAllText(Source("ThemeService.cs")));
    }

    [Fact]
    public void HeaderUsesHighResolutionLogoAndCreditsStayInSmallFooter()
    {
        var window = XDocument.Load(Source("MainWindow.xaml"));
        var image = window.Descendants(Presentation + "Image").Single(element => (string?)element.Attribute(Xaml + "Name") == "HeaderLogo");
        Assert.Equal("Assets/epm-rule-creator.png", (string?)image.Attribute("Source"));
        Assert.Equal("HighQuality", (string?)image.Attribute("RenderOptions.BitmapScalingMode"));
        var project = XDocument.Load(Source("EPMRuleCreator.UI.csproj"));
        Assert.Contains(project.Descendants("Resource"), element => (string?)element.Attribute("Link") == "Assets/epm-rule-creator.png");
        var footer = window.Descendants(Presentation + "TextBlock").Single(element => (string?)element.Attribute(Xaml + "Name") == "AuthorFooter");
        Assert.Equal("3", (string?)footer.Attribute("Grid.Row"));
        Assert.Equal("11", (string?)footer.Attribute("FontSize"));
        Assert.Contains(footer.Descendants(Presentation + "Run"), element => (string?)element.Attribute("Text") == "Built by Mathias Borowicz");
        Assert.Equal(new[] { "https://github.com/mathiasborowicz/EPMRuleCreator", "https://zerotruststories.com/" },
            footer.Descendants(Presentation + "Hyperlink").Select(element => (string?)element.Attribute("NavigateUri")));
        Assert.All(footer.Descendants(Presentation + "Hyperlink"), link => Assert.Equal("AuthorLink_RequestNavigate", (string?)link.Attribute("RequestNavigate")));
    }

    [Fact]
    public void SetupOffersInstalledOnlyMenuCheckboxWithoutExtraHint()
    {
        var setup = XDocument.Load(Source("LaunchModeWindow.xaml"));
        var installed = setup.Descendants(Presentation + "StackPanel").Single(element => (string?)element.Attribute(Xaml + "Name") == "InstalledModePanel");
        var checkbox = Assert.Single(setup.Descendants(Presentation + "CheckBox"));
        Assert.Equal("AddContextMenuCheckBox", (string?)checkbox.Attribute(Xaml + "Name"));
        Assert.Contains(installed, checkbox.Ancestors());
        Assert.Equal("False", (string?)checkbox.Attribute("IsChecked"));
        Assert.DoesNotContain(setup.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "ExistingInstallationHint");
        var text = File.ReadAllText(Source("LaunchModeWindow.xaml"));
        Assert.Contains("No admin permissions required.", text);
        Assert.DoesNotContain("admin rights", text);
        var code = File.ReadAllText(Source("LaunchModeWindow.xaml.cs"));
        Assert.Contains("InstallOrOpen(_arguments, AddContextMenuCheckBox.IsChecked == true)", code);
        var portable = code[code.IndexOf("private void Portable_Click", StringComparison.Ordinal)..code.IndexOf("private void Install_Click", StringComparison.Ordinal)];
        Assert.DoesNotContain("ExplorerContextMenuService", portable);
        Assert.DoesNotContain("AlignExistingMenu", portable);
        var installation = File.ReadAllText(Source("ApplicationInstallation.cs"));
        Assert.Contains("if (registerContextMenu) ExplorerContextMenuService.Register(installed);", installation);
        Assert.Contains("else ExplorerContextMenuService.Remove();", installation);
    }

    [Fact]
    public void PortableMenuCommandsAreHiddenAndTenantStartsEmpty()
    {
        var window = XDocument.Load(Source("MainWindow.xaml"));
        var menu = window.Descendants(Presentation + "Button").Single(element => (string?)element.Attribute("Command") == "{Binding ToggleExplorerMenuCommand}");
        Assert.Equal("{Binding IsInstalledApplication, Converter={StaticResource BooleanToVisibilityConverter}}", (string?)menu.Attribute("Visibility"));
        var code = File.ReadAllText(Source("ViewModels/MainViewModel.cs"));
        Assert.Contains("private string _tenantId = string.Empty;", code);
        Assert.Contains("_tenantId = TenantIdPreference.ForDisplay(settings.TenantId);", code);
        Assert.Contains("if (!IsInstalledApplication) return;", code);
        Assert.Contains("HasExplorerMenu => IsInstalledApplication && _explorerMenuRegistered", code);
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source("App.xaml.cs"))!, "../.."));
        var registration = File.ReadAllText(Path.Combine(root, "src", "EPMRuleCreator.Core", "Services", "ExplorerContextMenuService.cs"));
        Assert.Contains("if (!ApplicationModeService.IsInstalled(fullPath", registration);
        var script = File.ReadAllText(Path.Combine(root, "scripts", "Register-ExplorerMenu.ps1"));
        Assert.Contains("if ($resolvedPath -ine $installedPath)", script);
    }

    [Fact]
    public void GeneralTooltipsStayShortAndPreserveActionWarnings()
    {
        foreach (var file in new[] { "MainWindow.xaml", "LaunchModeWindow.xaml" })
        {
            var document = XDocument.Load(Source(file));
            foreach (var tooltip in document.Descendants().Attributes("ToolTip").Where(attribute => !attribute.Value.StartsWith('{')))
                Assert.True(tooltip.Value.Length <= 80, $"Tooltip is too long: {tooltip.Value}");
        }
        var window = XDocument.Load(Source("MainWindow.xaml"));
        foreach (var command in new[] { "UpdateNowCommand", "InstallApplicationCommand" })
        {
            var button = window.Descendants(Presentation + "Button").Single(element => (string?)element.Attribute("Command") == $"{{Binding {command}}}");
            Assert.Contains("Unsaved rule edits will be lost.", (string?)button.Attribute("ToolTip"));
        }
        var upload = window.Descendants(Presentation + "Button").Single(element => (string?)element.Attribute("Command") == "{Binding UploadRuleCommand}");
        Assert.Contains("Writes immediately.", (string?)upload.Attribute("ToolTip"));
        Assert.Contains("no assignments", (string?)upload.Attribute("ToolTip"));
    }

    [Fact]
    public void MenuTooltipOnlyDescribesTheAction()
    {
        var code = File.ReadAllText(Source("ViewModels/MainViewModel.cs"));
        var expression = Regex.Match(code, @"public string ExplorerMenuToolTip =>[\s\S]*?;").Value;
        Assert.Equal(new[] { "Remove the context menu entry.", "Add the context menu entry." },
            Regex.Matches(expression, "\"([^\"]*)\"").Select(match => match.Groups[1].Value));
        Assert.DoesNotContain("+", expression);
        Assert.Contains("ExplorerMenuRepairToolTip => \"Repair the context menu entry.\";", code);
        Assert.Contains("Trust not verified.", code);
        Assert.Contains("Could not load certificates. See Logs.", code);
    }

    [Fact]
    public void TextBoxPaddingIsNotAppliedTwiceToTheContentHost()
    {
        var app = XDocument.Load(Source("App.xaml"));
        var style = app.Descendants(Presentation + "Style").Single(element => (string?)element.Attribute("TargetType") == "TextBox");
        var padding = style.Elements(Presentation + "Setter").Single(element => (string?)element.Attribute("Property") == "Padding");
        Assert.Equal("10,8", (string?)padding.Attribute("Value"));
        var host = style.Descendants(Presentation + "ScrollViewer").Single(element => (string?)element.Attribute(Xaml + "Name") == "PART_ContentHost");
        Assert.Null(host.Attribute("Margin"));
        Assert.Null(host.Parent!.Attribute("Padding"));
        Assert.Equal("False", (string?)host.Attribute("Focusable"));
        Assert.Equal("{TemplateBinding HorizontalScrollBarVisibility}", (string?)host.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("{TemplateBinding VerticalScrollBarVisibility}", (string?)host.Attribute("VerticalScrollBarVisibility"));
        var window = XDocument.Load(Source("MainWindow.xaml"));
        var file = window.Descendants(Presentation + "TextBox").Single(element => (string?)element.Attribute(Xaml + "Name") == "SelectedFileInput");
        Assert.Equal("Center", (string?)file.Attribute("VerticalContentAlignment"));
        Assert.Equal("True", (string?)file.Attribute("IsReadOnly"));
    }

    [Fact]
    public void VersionIsNormalizedWhenLeavingTheFieldAndBeforeBuildingARule()
    {
        var window = XDocument.Load(Source("MainWindow.xaml"));
        var field = window.Descendants(Presentation + "TextBox").Single(element => (string?)element.Attribute("Text") == "{Binding RuleFileVersion, UpdateSourceTrigger=PropertyChanged}");
        Assert.Equal("MinimumVersion_LostFocus", (string?)field.Attribute("LostFocus"));
        Assert.Equal("30", (string?)field.Attribute("MaxLength"));
        var code = File.ReadAllText(Source("ViewModels/MainViewModel.cs"));
        var build = code[code.IndexOf("private ElevationRule BuildRule()", StringComparison.Ordinal)..code.IndexOf("private void Validate()", StringComparison.Ordinal)];
        Assert.True(build.IndexOf("NormalizeRuleFileVersion();", StringComparison.Ordinal) < build.IndexOf("MinimumFileVersion = RuleFileVersion", StringComparison.Ordinal));
        Assert.Contains("NormalizeMinimumFileVersionToFourParts(result.Version?.Trim())", code);
    }

    [Fact]
    public void SourceDisplaysSelectedDnAndAllAnalyzedCertificatesAreSelectable()
    {
        var window = XDocument.Load(Source("MainWindow.xaml"));
        XNamespace local = "clr-namespace:EPMRuleCreator.UI";
        var source = window.Descendants(Presentation + "ComboBox").Single(element => (string?)element.Attribute("ItemsSource") == "{Binding SignatureSourceOptions}");
        Assert.Equal("{Binding SignatureSourceSelectionText}", (string?)source.Attribute(local + "ComboBoxDisplay.SelectionText"));
        var certificates = window.Descendants(Presentation + "ComboBox").Single(element => (string?)element.Attribute("ItemsSource") == "{Binding AnalyzedCertificateOptions}");
        Assert.Equal("Subject", (string?)certificates.Attribute("DisplayMemberPath"));
        Assert.Equal("{Binding SelectedAnalyzedCertificate, Mode=TwoWay}", (string?)certificates.Attribute("SelectedItem"));
        foreach (var items in new[] { "CertificateTypeOptions", "ElevationTypes" })
        {
            var control = window.Descendants(Presentation + "ComboBox").Single(element => (string?)element.Attribute("ItemsSource") == $"{{Binding {items}}}");
            Assert.Equal("DisplayName", (string?)control.Attribute("DisplayMemberPath"));
            Assert.Null(control.Attribute(local + "ComboBoxDisplay.SelectionText"));
        }
        var code = File.ReadAllText(Source("ViewModels/MainViewModel.cs"));
        Assert.Contains("SignatureSource.CertificateFile => PublisherCertificate?.Subject", code);
        Assert.Contains("PublisherCertificate = uploaded ? _uploadedCertificate : SelectedAnalyzedCertificate;", code);
        Assert.Contains("PublisherCertificate = SignatureSource == global::EPMRuleCreator.Core.Models.SignatureSource.CertificateFile ? PublisherCertificate : null", code);
        Assert.Contains("selected.IsCertificateAuthority", code);
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

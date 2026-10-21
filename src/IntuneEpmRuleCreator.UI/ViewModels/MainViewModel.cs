using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using IntuneEpmRuleCreator.Core.Models;
using IntuneEpmRuleCreator.Core.Services;
using IntuneEpmRuleCreator.Graph;
using System.Text.Json;

namespace IntuneEpmRuleCreator.UI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly FileAttributeService _fileService = new(new Sha256FileHasher());
    private readonly RuleValidationService _validator = new();
    private string _filePath = string.Empty;
    private string _sha256 = string.Empty;
    private string _productName = string.Empty;
    private string _version = string.Empty;
    private string _companyName = string.Empty;
    private string _epmFileHash = string.Empty;
    private string _internalName = string.Empty;
    private string _fileDescription = string.Empty;
    private string _certificateSummary = string.Empty;
    private string _signatureStatus = "Not analyzed";
    private string _analysisWarnings = string.Empty;
    private string _ruleName = string.Empty;
    private string _ruleDescription = string.Empty;
    private string _ruleFileName = string.Empty;
    private string _ruleFileHash = string.Empty;
    private string _ruleFilePath = string.Empty;
    private string _ruleProductName = string.Empty;
    private string _ruleInternalName = string.Empty;
    private string _validationSummary = "Choose a supported file and analyze it before validation.";
    private IdentificationStrategy _identificationStrategy = IdentificationStrategy.Sha256;
    private ElevationType _elevationType = ElevationType.UserConfirmed;
    private ChildProcessBehavior _childProcessBehavior = ChildProcessBehavior.RequireRuleToElevate;
    private bool _busy;
    private readonly GraphAuthenticationService _graphAuthentication = new();
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntuneEpmRuleCreator", "settings.json");
    private string _clientId = typeof(MainViewModel).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "GraphClientId")?.Value ?? string.Empty;
    private string _tenantId = "organizations";
    private string _graphConnectionStatus = "Optional: sign in only if you want to connect to Microsoft Graph. Local file analysis works without sign-in.";
    private string _graphAccount = string.Empty;
    private bool _graphConnected;

    public MainViewModel()
    {
        IdentificationStrategies = Enum.GetValues<IdentificationStrategy>();
        ElevationTypes = Enum.GetValues<ElevationType>();
        ChildProcessBehaviors = Enum.GetValues<ChildProcessBehavior>();
        AnalyzeCommand = new AsyncCommand(AnalyzeAsync, () => !Busy && !string.IsNullOrWhiteSpace(FilePath));
        ValidateCommand = new RelayCommand(Validate);
        ConnectGraphCommand = new AsyncCommand(ConnectGraphAsync, () => !Busy && !GraphConnected);
        DisconnectGraphCommand = new AsyncCommand(DisconnectGraphAsync, () => !Busy && GraphConnected);
        LoadSettings();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Array IdentificationStrategies { get; }
    public Array ElevationTypes { get; }
    public Array ChildProcessBehaviors { get; }
    public ICommand AnalyzeCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand ConnectGraphCommand { get; }
    public ICommand DisconnectGraphCommand { get; }
    public ObservableCollection<string> ValidationMessages { get; } = [];

    public string FilePath { get => _filePath; set { Set(ref _filePath, value); Raise(nameof(AnalyzeCommand)); } }
    public string Sha256 { get => _sha256; set => Set(ref _sha256, value); }
    public string ProductName { get => _productName; set => Set(ref _productName, value); }
    public string Version { get => _version; set => Set(ref _version, value); }
    public string CompanyName { get => _companyName; set => Set(ref _companyName, value); }
    public string EpmFileHash { get => _epmFileHash; set => Set(ref _epmFileHash, value); }
    public string InternalName { get => _internalName; set => Set(ref _internalName, value); }
    public string FileDescription { get => _fileDescription; set => Set(ref _fileDescription, value); }
    public string CertificateSummary { get => _certificateSummary; set => Set(ref _certificateSummary, value); }
    public string SignatureStatus { get => _signatureStatus; set => Set(ref _signatureStatus, value); }
    public string AnalysisWarnings { get => _analysisWarnings; set => Set(ref _analysisWarnings, value); }
    public string RuleName { get => _ruleName; set => Set(ref _ruleName, value); }
    public string RuleDescription { get => _ruleDescription; set => Set(ref _ruleDescription, value); }
    public string RuleFileName { get => _ruleFileName; set => Set(ref _ruleFileName, value); }
    public string RuleFileHash { get => _ruleFileHash; set => Set(ref _ruleFileHash, value); }
    public string RuleFilePath { get => _ruleFilePath; set => Set(ref _ruleFilePath, value); }
    public string RuleProductName { get => _ruleProductName; set => Set(ref _ruleProductName, value); }
    public string RuleInternalName { get => _ruleInternalName; set => Set(ref _ruleInternalName, value); }
    public string ValidationSummary { get => _validationSummary; set => Set(ref _validationSummary, value); }
    public IdentificationStrategy IdentificationStrategy { get => _identificationStrategy; set => Set(ref _identificationStrategy, value); }
    public ElevationType ElevationType { get => _elevationType; set => Set(ref _elevationType, value); }
    public ChildProcessBehavior ChildProcessBehavior { get => _childProcessBehavior; set => Set(ref _childProcessBehavior, value); }
    public bool Busy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(AnalyzeCommand)); } }
    public string ClientId { get => _clientId; set { Set(ref _clientId, value); SaveSettings(); } }
    public string TenantId { get => _tenantId; set { Set(ref _tenantId, value); SaveSettings(); } }
    public string GraphConnectionStatus { get => _graphConnectionStatus; private set => Set(ref _graphConnectionStatus, value); }
    public string GraphAccount { get => _graphAccount; private set => Set(ref _graphAccount, value); }
    public bool GraphConnected { get => _graphConnected; private set { Set(ref _graphConnected, value); Raise(nameof(ConnectGraphCommand)); Raise(nameof(DisconnectGraphCommand)); } }

    private async Task AnalyzeAsync()
    {
        Busy = true;
        try
        {
            var result = await _fileService.AnalyzeAsync(FilePath);
            Sha256 = result.Sha256;
            ProductName = result.ProductName ?? string.Empty;
            Version = result.Version ?? string.Empty;
            CompanyName = result.CompanyName ?? string.Empty;
            EpmFileHash = result.EpmFileHash ?? string.Empty;
            InternalName = result.InternalName ?? string.Empty;
            FileDescription = result.Description ?? string.Empty;
            CertificateSummary = result.PublisherCertificate is { } certificate
                ? $"Publisher: {certificate.Subject}{Environment.NewLine}Issuer: {certificate.Issuer}{Environment.NewLine}Thumbprint: {certificate.Thumbprint}{Environment.NewLine}Valid dates: {certificate.NotBefore:g} – {certificate.NotAfter:g} (valid now: {certificate.IsWithinValidityPeriod})"
                : "Publisher certificate details unavailable.";
            SignatureStatus = result.SignatureStatus ?? "Not available";
            AnalysisWarnings = string.Join(Environment.NewLine, result.Warnings);
            RuleFileName = result.FileName;
            RuleFileHash = result.Sha256;
            RuleFilePath = result.FilePath;
            RuleProductName = result.ProductName ?? string.Empty;
            RuleInternalName = result.InternalName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(RuleName))
                RuleName = $"{result.ProductName ?? Path.GetFileNameWithoutExtension(result.FileName)} - Elevation Rule";
            ValidationSummary = "File analysis complete. Review and validate the rule.";
        }
        catch (Exception exception)
        {
            ValidationSummary = "File analysis failed.";
            MessageBox.Show(exception.Message, "File analysis", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ConnectGraphAsync()
    {
        Busy = true;
        try
        {
            var window = Application.Current?.MainWindow;
            var profile = await _graphAuthentication.SignInAsync(ClientId, TenantId, window is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(window).Handle);
            GraphConnected = true;
            GraphAccount = $"{profile.DisplayName} ({profile.UserPrincipalName})\nTenant ID: {profile.TenantId}";
            GraphConnectionStatus = "Connected to Microsoft Graph. No Intune policy changes are made by this connection.";
            TenantId = profile.TenantId;
        }
        catch (Exception exception)
        {
            GraphConnectionStatus = $"Connection failed: {exception.Message}";
        }
        finally { Busy = false; }
    }

    private async Task DisconnectGraphAsync()
    {
        await _graphAuthentication.RemoveAccountAsync();
        GraphConnected = false;
        GraphAccount = string.Empty;
        GraphConnectionStatus = "Disconnected. Local file analysis remains available offline.";
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath));
            if (settings is null) return;
            if (!string.IsNullOrWhiteSpace(settings.ClientId))
                _clientId = settings.ClientId;
            _tenantId = string.IsNullOrWhiteSpace(settings.TenantId) ? "organizations" : settings.TenantId;
        }
        catch { /* Invalid or inaccessible optional settings must not block offline use. */ }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new AppSettings(ClientId, TenantId)));
        }
        catch { /* Settings are optional and never block local functionality. */ }
    }

    private sealed record AppSettings(string? ClientId, string? TenantId);

    private void Validate()
    {
        var rule = new ElevationRule
        {
            Name = RuleName, Description = RuleDescription, FileName = RuleFileName,
            FilePath = RuleFilePath, FileHash = RuleFileHash, ProductName = RuleProductName,
            InternalName = RuleInternalName, IdentificationStrategy = IdentificationStrategy,
            ElevationType = ElevationType, ChildProcessBehavior = ChildProcessBehavior,
        };
        var result = _validator.Validate(rule, existingRuleCount: 0, hasPolicy: false);
        ValidationMessages.Clear();
        foreach (var issue in result.Issues)
            ValidationMessages.Add($"{(issue.IsBlocking ? "Error" : "Warning")}: {issue.Message}");
        ValidationSummary = result.CanSubmit ? "Local validation passed." : "Resolve the blocking errors before this rule can be submitted.";
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
    }

    private void Raise(string? name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        CommandManager.InvalidateRequerySuggested();
    }

    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }

    private sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
        public bool CanExecute(object? parameter) => canExecute();
        public async void Execute(object? parameter) => await execute();
    }
}

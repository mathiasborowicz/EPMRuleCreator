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
using System.Text;
using System.Security.Cryptography.X509Certificates;

namespace IntuneEpmRuleCreator.UI.ViewModels;

public sealed record CertificateTypeOption(CertificateType Value, string DisplayName);
public sealed record SignatureSourceOption(SignatureSource Value, string DisplayName);
public sealed record RestrictArgumentsOption(RestrictArgumentsMode Value, string DisplayName);
public sealed record ElevationTypeOption(ElevationType Value, string DisplayName);

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
    private CertificateAttributes? _publisherCertificate;
    private string _signatureStatus = "Not analyzed";
    private string _analysisWarnings = string.Empty;
    private string _ruleName = string.Empty;
    private string _ruleDescription = string.Empty;
    private string _ruleFileName = string.Empty;
    private string _ruleFileVersion = string.Empty;
    private string _ruleFileHash = string.Empty;
    private string _ruleFileDescription = string.Empty;
    private string _ruleFilePath = string.Empty;
    private string _ruleProductName = string.Empty;
    private string _ruleInternalName = string.Empty;
    private string _validationSummary = "Choose a supported file and analyze it before validation.";
    private SignatureSource _signatureSource = SignatureSource.NotConfigured;
    private RestrictArgumentsMode _restrictArguments = RestrictArgumentsMode.NotConfigured;
    private string _argumentListText = string.Empty;
    private string _selectedCertificateFile = string.Empty;
    private ElevationType _elevationType = ElevationType.UserConfirmed;
    private ChildProcessBehavior _childProcessBehavior = ChildProcessBehavior.RequireRuleToElevate;
    private bool _busy;
    private readonly GraphAuthenticationService _graphAuthentication = new();
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntuneEpmRuleCreator", "settings.json");
    private string _clientId = string.Empty;
    private string _tenantId = "organizations";
    private string _graphConnectionStatus = "Optional: sign in only if you want to connect to Microsoft Graph. Local file analysis works without sign-in.";
    private string _graphAccount = string.Empty;
    private string _accountInitials = string.Empty;
    private bool _isAccountMenuOpen;
    private bool _graphConnected;
    private ElevationRulePolicy? _selectedElevationPolicy;
    private string _policyLoadStatus = "Use the account button at the top right to sign in and load EPM policies.";
    private int? _selectedPolicyRuleCount;
    private int? _selectedPolicyRuleLimit;
    private bool _requireBusinessJustification;
    private bool _requireWindowsAuthentication;
    private EpmReusableCertificateSetting? _selectedReusableCertificateSetting;

    public MainViewModel()
    {
        ChildProcessBehaviors = Enum.GetValues<ChildProcessBehavior>();
        AnalyzeCommand = new AsyncCommand(AnalyzeAsync, () => !Busy && !string.IsNullOrWhiteSpace(FilePath));
        ValidateCommand = new RelayCommand(Validate);
        UploadRuleCommand = new AsyncCommand(UploadRuleAsync, () => !Busy && GraphConnected && SelectedElevationPolicy is not null);
        ConnectGraphCommand = new AsyncCommand(ConnectGraphAsync, () => !Busy && !GraphConnected);
        DisconnectGraphCommand = new AsyncCommand(DisconnectGraphAsync, () => !Busy && GraphConnected);
        LoadPoliciesCommand = new AsyncCommand(LoadPoliciesAsync, () => !Busy && GraphConnected);
        LoadSelectedPolicyCountCommand = new AsyncCommand(LoadSelectedPolicyCountAsync, () => !Busy && GraphConnected && SelectedElevationPolicy is not null);
        LoadReusableCertificateSettingsCommand = new AsyncCommand(LoadReusableCertificateSettingsAsync, () => !Busy && GraphConnected);
        LoadSettings();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<ElevationTypeOption> ElevationTypes { get; } =
    [
        new(ElevationType.UserConfirmed, "User confirmed"),
        new(ElevationType.Automatic, "Automatic"),
        new(ElevationType.SupportApproved, "Support approved"),
        new(ElevationType.Deny, "Deny"),
        new(ElevationType.CurrentUser, "Elevate as current user")
    ];
    public Array ChildProcessBehaviors { get; }
    public IReadOnlyList<SignatureSourceOption> SignatureSourceOptions { get; } =
    [
        new(SignatureSource.NotConfigured, "Not configured"),
        new(SignatureSource.CertificateFile, "Upload a certificate file"),
        new(SignatureSource.ReusableSettings, "Use a certificate file in reusable settings")
    ];
    public IReadOnlyList<RestrictArgumentsOption> RestrictArgumentsOptions { get; } =
    [
        new(RestrictArgumentsMode.NotConfigured, "Not configured"),
        new(RestrictArgumentsMode.Allow, "Allow")
    ];
    public IReadOnlyList<CertificateTypeOption> CertificateTypeOptions { get; } =
    [
        new(global::IntuneEpmRuleCreator.Core.Models.CertificateType.Publisher, "Publisher"),
        new(global::IntuneEpmRuleCreator.Core.Models.CertificateType.CertificateAuthority, "Certificate authority")
    ];
    public ICommand AnalyzeCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand UploadRuleCommand { get; }
    public ICommand ConnectGraphCommand { get; }
    public ICommand DisconnectGraphCommand { get; }
    public ICommand LoadPoliciesCommand { get; }
    public ICommand LoadSelectedPolicyCountCommand { get; }
    public ICommand LoadReusableCertificateSettingsCommand { get; }
    public ObservableCollection<string> ValidationMessages { get; } = [];
    public ObservableCollection<ElevationRulePolicy> ElevationPolicies { get; } = [];
    public ObservableCollection<EpmReusableCertificateSetting> ReusableCertificateSettings { get; } = [];

    public string FilePath { get => _filePath; set { Set(ref _filePath, value); Raise(nameof(AnalyzeCommand)); } }
    public string Sha256 { get => _sha256; set => Set(ref _sha256, value); }
    public string ProductName { get => _productName; set => Set(ref _productName, value); }
    public string Version { get => _version; set => Set(ref _version, value); }
    public string CompanyName { get => _companyName; set => Set(ref _companyName, value); }
    public string EpmFileHash { get => _epmFileHash; set => Set(ref _epmFileHash, value); }
    public string InternalName { get => _internalName; set => Set(ref _internalName, value); }
    public string FileDescription { get => _fileDescription; set => Set(ref _fileDescription, value); }
    public string CertificateSummary { get => _certificateSummary; set => Set(ref _certificateSummary, value); }
    public CertificateAttributes? PublisherCertificate
    {
        get => _publisherCertificate;
        private set
        {
            if (!Set(ref _publisherCertificate, value)) return;
            Raise(nameof(HasPublisherCertificate));
            Raise(nameof(PublisherCertificateChoiceText));
        }
    }
    public string SignatureStatus { get => _signatureStatus; set => Set(ref _signatureStatus, value); }
    public string AnalysisWarnings { get => _analysisWarnings; set => Set(ref _analysisWarnings, value); }
    public string RuleName { get => _ruleName; set => Set(ref _ruleName, value); }
    public string RuleDescription { get => _ruleDescription; set => Set(ref _ruleDescription, value); }
    public string RuleFileName { get => _ruleFileName; set => Set(ref _ruleFileName, value); }
    public string RuleFileVersion { get => _ruleFileVersion; set => Set(ref _ruleFileVersion, value); }
    public string RuleFileHash { get => _ruleFileHash; set => Set(ref _ruleFileHash, value); }
    public string RuleFileDescription { get => _ruleFileDescription; set => Set(ref _ruleFileDescription, value); }
    public string RuleFilePath { get => _ruleFilePath; set => Set(ref _ruleFilePath, value); }
    public string RuleProductName { get => _ruleProductName; set => Set(ref _ruleProductName, value); }
    public string RuleInternalName { get => _ruleInternalName; set => Set(ref _ruleInternalName, value); }
    public string ValidationSummary { get => _validationSummary; set => Set(ref _validationSummary, value); }
    public SignatureSource SignatureSource
    {
        get => _signatureSource;
        set
        {
            if (!Set(ref _signatureSource, value)) return;
            Raise(nameof(ShowCertificateSelection));
            Raise(nameof(ShowCertificateFileSource));
            Raise(nameof(ShowReusableCertificateSource));
            Raise(nameof(FileNameLabel));
            Raise(nameof(FileHashLabel));
        }
    }
    public bool ShowCertificateSelection => SignatureSource != global::IntuneEpmRuleCreator.Core.Models.SignatureSource.NotConfigured;
    public bool ShowCertificateFileSource => SignatureSource == global::IntuneEpmRuleCreator.Core.Models.SignatureSource.CertificateFile;
    public bool ShowReusableCertificateSource => SignatureSource == global::IntuneEpmRuleCreator.Core.Models.SignatureSource.ReusableSettings;
    private CertificateType _certificateType = global::IntuneEpmRuleCreator.Core.Models.CertificateType.Publisher;
    public CertificateType CertificateType { get => _certificateType; set => Set(ref _certificateType, value); }
    public string FileNameLabel => SignatureSource == global::IntuneEpmRuleCreator.Core.Models.SignatureSource.NotConfigured
        ? "File name *"
        : "File name";
    public string FileHashLabel => SignatureSource == global::IntuneEpmRuleCreator.Core.Models.SignatureSource.NotConfigured
        ? "File hash (SHA256) *"
        : "File hash (SHA256)";
    public bool HasPublisherCertificate => PublisherCertificate is not null;
    public string PublisherCertificateChoiceText => !string.IsNullOrWhiteSpace(SelectedCertificateFile)
        ? $"Selected certificate file: {Path.GetFileName(SelectedCertificateFile)}"
        : PublisherCertificate is { } certificate
            ? $"Using analyzed file's publisher certificate — {certificate.Subject} (thumbprint {certificate.Thumbprint})"
            : "No certificate selected. Choose a certificate file or analyze a signed file.";
    public string SelectedCertificateFile { get => _selectedCertificateFile; private set => Set(ref _selectedCertificateFile, value); }
    public RestrictArgumentsMode RestrictArguments { get => _restrictArguments; set { if (Set(ref _restrictArguments, value)) Raise(nameof(ShowArgumentList)); } }
    public bool ShowArgumentList => RestrictArguments == RestrictArgumentsMode.Allow;
    public string ArgumentListText { get => _argumentListText; set => Set(ref _argumentListText, value); }
    public EpmReusableCertificateSetting? SelectedReusableCertificateSetting
    {
        get => _selectedReusableCertificateSetting;
        set => Set(ref _selectedReusableCertificateSetting, value);
    }
    public ElevationType ElevationType { get => _elevationType; set { if (Set(ref _elevationType, value)) { Raise(nameof(IsUserConfirmed)); Raise(nameof(IsCurrentUser)); } } }
    public bool IsUserConfirmed => ElevationType == global::IntuneEpmRuleCreator.Core.Models.ElevationType.UserConfirmed;
    public bool IsCurrentUser => ElevationType == global::IntuneEpmRuleCreator.Core.Models.ElevationType.CurrentUser;
    public bool RequireBusinessJustification { get => _requireBusinessJustification; set => Set(ref _requireBusinessJustification, value); }
    public bool RequireWindowsAuthentication { get => _requireWindowsAuthentication; set => Set(ref _requireWindowsAuthentication, value); }
    public ChildProcessBehavior ChildProcessBehavior { get => _childProcessBehavior; set => Set(ref _childProcessBehavior, value); }
    public bool Busy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(AnalyzeCommand)); Raise(nameof(UploadRuleCommand)); } }
    public string ClientId { get => _clientId; set { Set(ref _clientId, value); SaveSettings(); } }
    public string TenantId { get => _tenantId; set { Set(ref _tenantId, value); SaveSettings(); } }
    public string GraphConnectionStatus { get => _graphConnectionStatus; private set => Set(ref _graphConnectionStatus, value); }
    public string GraphAccount { get => _graphAccount; private set { if (Set(ref _graphAccount, value)) Raise(nameof(AccountButtonLabel)); } }
    public string AccountInitials { get => _accountInitials; private set => Set(ref _accountInitials, value); }
    public bool IsAccountMenuOpen { get => _isAccountMenuOpen; set => Set(ref _isAccountMenuOpen, value); }
    public string AccountButtonLabel => GraphConnected ? $"Microsoft Entra account: {GraphAccount}" : "Sign in to Microsoft Entra (optional)";
    public bool GraphConnected { get => _graphConnected; private set { Set(ref _graphConnected, value); Raise(nameof(AccountButtonLabel)); Raise(nameof(ConnectGraphCommand)); Raise(nameof(DisconnectGraphCommand)); Raise(nameof(UploadRuleCommand)); } }
    public ElevationRulePolicy? SelectedElevationPolicy
    {
        get => _selectedElevationPolicy;
        set
        {
            if (EqualityComparer<ElevationRulePolicy?>.Default.Equals(_selectedElevationPolicy, value)) return;
            Set(ref _selectedElevationPolicy, value);
            _selectedPolicyRuleCount = null;
            _selectedPolicyRuleLimit = null;
            Raise(nameof(SelectedPolicySummary));
            Raise(nameof(LoadSelectedPolicyCountCommand));
            Raise(nameof(UploadRuleCommand));
        }
    }
    public string PolicyLoadStatus { get => _policyLoadStatus; private set => Set(ref _policyLoadStatus, value); }
    public string SelectedPolicySummary => SelectedElevationPolicy is null
        ? "No policy selected."
        : _selectedPolicyRuleCount is null
            ? $"{SelectedElevationPolicy.Name}\nLast modified: {SelectedElevationPolicy.LastModifiedDateTime?.ToLocalTime().ToString("g") ?? "unknown"} | Assigned: {(SelectedElevationPolicy.IsAssigned ? "Yes" : "No")}\nGraph settingCount: {SelectedElevationPolicy.SettingCount} (settings, not rules). Load the rule count before validation."
            : $"{SelectedElevationPolicy.Name}\nLast modified: {SelectedElevationPolicy.LastModifiedDateTime?.ToLocalTime().ToString("g") ?? "unknown"} | Assigned: {(SelectedElevationPolicy.IsAssigned ? "Yes" : "No")}\nElevation rules: {_selectedPolicyRuleCount}/{_selectedPolicyRuleLimit ?? 100}.";

    private async Task AnalyzeAsync()
    {
        Busy = true;
        try
        {
            var result = await _fileService.AnalyzeAsync(FilePath);
            SelectedCertificateFile = string.Empty;
            PublisherCertificate = result.PublisherCertificate;
            Sha256 = result.Sha256;
            ProductName = result.ProductName ?? string.Empty;
            Version = result.Version ?? string.Empty;
            CompanyName = result.CompanyName ?? string.Empty;
            EpmFileHash = result.EpmFileHash ?? string.Empty;
            InternalName = result.InternalName ?? string.Empty;
            FileDescription = result.Description ?? string.Empty;
            RuleFileDescription = result.Description ?? string.Empty;
            CertificateSummary = result.PublisherCertificate is { } certificate
                ? $"Publisher: {certificate.Subject}{Environment.NewLine}Issuer: {certificate.Issuer}{Environment.NewLine}Thumbprint: {certificate.Thumbprint}{Environment.NewLine}Valid dates: {certificate.NotBefore:g} – {certificate.NotAfter:g} (valid now: {certificate.IsWithinValidityPeriod})"
                : "Publisher certificate details unavailable.";
            SignatureStatus = result.SignatureStatus ?? "Not available";
            AnalysisWarnings = string.Join(Environment.NewLine, result.Warnings);
            RuleFileName = result.FileName;
            // Preserve detected metadata in the editable rule field; validation reports
            // unsupported version formats instead of silently blanking the prefilled value.
            RuleFileVersion = RuleValidationService.NormalizeMinimumFileVersionToFourParts(result.Version?.Trim()) ?? string.Empty;
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
            var nameParts = (string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.UserPrincipalName : profile.DisplayName)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            AccountInitials = nameParts.Length == 0 ? "?"
                : (nameParts[0][..1] + (nameParts.Length > 1 ? nameParts[^1][..1] : string.Empty)).ToUpperInvariant();
            GraphConnectionStatus = "Connected to Microsoft Graph. Intune is changed only when you explicitly upload a rule.";
            TenantId = profile.TenantId;
            IsAccountMenuOpen = false;
            await LoadPoliciesAsync();
        }
        catch (Exception exception)
        {
            GraphConnectionStatus = $"Connection failed: {exception.Message}";
            IsAccountMenuOpen = true;
        }
        finally { Busy = false; }
    }

    private async Task DisconnectGraphAsync()
    {
        await _graphAuthentication.RemoveAccountAsync();
        GraphConnected = false;
        ElevationPolicies.Clear();
        ReusableCertificateSettings.Clear();
        SelectedReusableCertificateSetting = null;
        Raise(nameof(ReusableCertificateSelectionHint));
        SelectedElevationPolicy = null;
        _selectedPolicyRuleCount = null;
        _selectedPolicyRuleLimit = null;
        GraphAccount = string.Empty;
        AccountInitials = string.Empty;
        IsAccountMenuOpen = false;
        GraphConnectionStatus = "Disconnected. Local file analysis remains available offline.";
        PolicyLoadStatus = "Use the account button at the top right to sign in and load EPM policies.";
    }

    private async Task LoadPoliciesAsync()
    {
        Busy = true;
        PolicyLoadStatus = "Loading EPM policies…";
        try
        {
            var policies = await _graphAuthentication.GetElevationRulePoliciesAsync();
            ElevationPolicies.Clear();
            foreach (var policy in policies.OrderBy(policy => policy.Name, StringComparer.CurrentCultureIgnoreCase))
                ElevationPolicies.Add(policy);
            SelectedElevationPolicy = null;
            PolicyLoadStatus = policies.Count == 0
                ? "No Elevation Rules policies were found in this tenant."
                : $"Loaded {policies.Count} Elevation Rules policy/policies.";
        }
        catch (Exception exception)
        {
            PolicyLoadStatus = $"Could not load policies: {exception.Message}";
        }
        finally { Busy = false; }
    }

    private async Task LoadSelectedPolicyCountAsync()
    {
        if (SelectedElevationPolicy is null) return;
        Busy = true;
        PolicyLoadStatus = $"Reading rules in '{SelectedElevationPolicy.Name}'…";
        try
        {
            var collection = await _graphAuthentication.GetElevationRuleCollectionInfoAsync(SelectedElevationPolicy);
            _selectedPolicyRuleCount = collection.RuleCount;
            _selectedPolicyRuleLimit = collection.MaximumRuleCount;
            Raise(nameof(SelectedPolicySummary));
            PolicyLoadStatus = $"Read {collection.RuleCount} existing elevation rule(s); maximum is {collection.MaximumRuleCount}.";
        }
        catch (Exception exception)
        {
            _selectedPolicyRuleCount = null;
            _selectedPolicyRuleLimit = null;
            Raise(nameof(SelectedPolicySummary));
            PolicyLoadStatus = $"Could not read rule count: {exception.Message}";
        }
        finally { Busy = false; }
    }

    private async Task LoadReusableCertificateSettingsAsync()
    {
        Busy = true;
        PolicyLoadStatus = "Loading reusable EPM certificate settings…";
        try
        {
            var settings = await _graphAuthentication.GetEpmReusableCertificateSettingsAsync();
            ReusableCertificateSettings.Clear();
            SelectedReusableCertificateSetting = null;
            foreach (var setting in settings.OrderBy(setting => setting.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                ReusableCertificateSettings.Add(setting);
            Raise(nameof(ReusableCertificateSelectionHint));
            PolicyLoadStatus = settings.Count == 0
                ? "No reusable EPM certificate settings were found. Non-EPM reusable settings are filtered out."
                : $"Loaded {settings.Count} reusable EPM certificate setting(s).";
        }
        catch (Exception exception)
        {
            PolicyLoadStatus = $"Could not load reusable EPM certificate settings: {exception.Message}";
        }
        finally { Busy = false; }
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

    public string ReusableCertificateSelectionHint => ReusableCertificateSettings.Count == 0
        ? "No reusable EPM certificate settings are loaded. Load them in the Graph section first."
        : "Choose the reusable EPM certificate setting to reference in this rule.";

    public void LoadCertificateFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var certificate = bytes.Length > 0 && Encoding.UTF8.GetString(bytes).Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
            ? X509Certificate2.CreateFromPem(Encoding.UTF8.GetString(bytes))
            : X509CertificateLoader.LoadCertificate(bytes);
        PublisherCertificate = new CertificateAttributes(
            certificate.Subject,
            certificate.Issuer,
            certificate.Thumbprint ?? string.Empty,
            new DateTimeOffset(certificate.NotBefore.ToUniversalTime()),
            new DateTimeOffset(certificate.NotAfter.ToUniversalTime()),
            DateTime.UtcNow >= certificate.NotBefore.ToUniversalTime() && DateTime.UtcNow <= certificate.NotAfter.ToUniversalTime())
        {
            CertificateBase64 = Convert.ToBase64String(certificate.RawData)
        };
        SelectedCertificateFile = path;
        CertificateSummary = $"Selected certificate: {certificate.Subject}{Environment.NewLine}Issuer: {certificate.Issuer}{Environment.NewLine}Thumbprint: {certificate.Thumbprint}";
        Raise(nameof(PublisherCertificateChoiceText));
    }

    private ElevationRule BuildRule() => new()
    {
        Name = RuleName,
        Description = RuleDescription,
        FileName = RuleFileName,
        MinimumFileVersion = RuleFileVersion,
        FilePath = RuleFilePath,
        FileHash = RuleFileHash,
        ProductName = RuleProductName,
        FileDescription = RuleFileDescription,
        InternalName = RuleInternalName,
        SignatureSource = SignatureSource,
        PublisherCertificate = SignatureSource == global::IntuneEpmRuleCreator.Core.Models.SignatureSource.CertificateFile ? PublisherCertificate : null,
        ReusableCertificateSettingId = SignatureSource == global::IntuneEpmRuleCreator.Core.Models.SignatureSource.ReusableSettings ? SelectedReusableCertificateSetting?.Id : null,
        CertificateType = CertificateType,
        ElevationType = ElevationType,
        RequireBusinessJustification = RequireBusinessJustification,
        RequireWindowsAuthentication = IsCurrentUser || RequireWindowsAuthentication,
        ChildProcessBehavior = ChildProcessBehavior,
        RestrictArguments = RestrictArguments,
        ArgumentList = ArgumentListText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    };

    private void Validate()
    {
        var result = _validator.Validate(BuildRule(), existingRuleCount: _selectedPolicyRuleCount ?? -1, hasPolicy: SelectedElevationPolicy is not null);
        ValidationMessages.Clear();
        foreach (var issue in result.Issues)
            ValidationMessages.Add($"{(issue.IsBlocking ? "Error" : "Warning")}: {issue.Message}");
        ValidationSummary = result.CanSubmit ? "Local validation passed." : "Resolve the blocking errors before this rule can be submitted.";
    }

    private async Task UploadRuleAsync()
    {
        var policy = SelectedElevationPolicy;
        if (policy is null) return;
        var rule = BuildRule();
        Busy = true;
        try
        {
            PolicyLoadStatus = $"Refreshing rules in '{policy.Name}' before upload…";
            var collection = await _graphAuthentication.GetElevationRuleCollectionInfoAsync(policy);
            if (SelectedElevationPolicy?.Id == policy.Id)
            {
                _selectedPolicyRuleCount = collection.RuleCount;
                _selectedPolicyRuleLimit = collection.MaximumRuleCount;
                Raise(nameof(SelectedPolicySummary));
            }

            var result = _validator.Validate(rule, collection.RuleCount, hasPolicy: true);
            ValidationMessages.Clear();
            foreach (var issue in result.Issues)
                ValidationMessages.Add($"{(issue.IsBlocking ? "Error" : "Warning")}: {issue.Message}");
            if (!result.CanSubmit)
            {
                ValidationSummary = "Resolve the blocking errors before uploading this rule.";
                PolicyLoadStatus = "Upload stopped because validation failed.";
                return;
            }

            var warningText = result.Issues.Where(issue => !issue.IsBlocking).Select(issue => issue.Message);
            var details = string.Join(Environment.NewLine, warningText);
            var confirmMessage = $"Append '{rule.Name}' to policy '{policy.Name}'?\n\nCurrent rules: {collection.RuleCount}/{collection.MaximumRuleCount}. This changes the Intune policy.";
            if (!string.IsNullOrWhiteSpace(details)) confirmMessage += $"\n\nWarnings:\n{details}";
            if (MessageBox.Show(confirmMessage, "Confirm Intune upload", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                PolicyLoadStatus = "Upload cancelled; no policy changes were sent.";
                return;
            }

            PolicyLoadStatus = $"Uploading '{rule.Name}' to '{policy.Name}'…";
            var updatedCount = await _graphAuthentication.AppendElevationRuleAsync(policy, rule);
            if (SelectedElevationPolicy?.Id == policy.Id)
            {
                _selectedPolicyRuleCount = updatedCount;
                _selectedPolicyRuleLimit = collection.MaximumRuleCount;
                Raise(nameof(SelectedPolicySummary));
            }
            ValidationSummary = "Rule uploaded successfully.";
            PolicyLoadStatus = $"Uploaded '{rule.Name}'. The policy now contains {updatedCount}/{collection.MaximumRuleCount} rules.";
        }
        catch (Exception exception)
        {
            ValidationSummary = "Rule upload failed.";
            PolicyLoadStatus = $"Could not upload the rule: {exception.Message}";
        }
        finally { Busy = false; }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
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

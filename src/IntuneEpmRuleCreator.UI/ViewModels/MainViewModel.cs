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
public sealed record ElevationPolicyOption(ElevationRulePolicy? Policy)
{
    public string Name => Policy?.Name ?? "+ Add new";
    public bool IsNewPolicy => Policy is null;
}

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
    private CertificateAttributes? _analyzedPublisherCertificate;
    private CertificateChainResult? _analyzedCertificateChain;
    private bool _useEpmCmdlets;
    private CertificateAttributes? _publisherCertificate;
    private CertificateAttributes? _uploadedCertificate;
    private bool _useUploadedCertificate;
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
    private string _validationSummary = "Select a file.";
    private SignatureSource _signatureSource = SignatureSource.NotConfigured;
    private RestrictArgumentsMode _restrictArguments = RestrictArgumentsMode.NotConfigured;
    private string _argumentListText = string.Empty;
    private string _selectedCertificateFile = string.Empty;
    private ElevationType _elevationType = ElevationType.UserConfirmed;
    private ChildProcessBehavior _childProcessBehavior = ChildProcessBehavior.RequireRuleToElevate;
    private bool _busy;
    private int _fileRevision;
    private CancellationTokenSource? _analysisCancellation;
    private bool _hasAnalyzedFile;
    private bool _explorerMenuRegistered;
    private string _signatureDetails = string.Empty;
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
    private readonly ElevationPolicyOption _newPolicyOption = new(null);
    private ElevationPolicyOption? _selectedPolicyOption;
    private string _newPolicyName = string.Empty;
    private string _newPolicyDescription = string.Empty;
    private string _policyLoadStatus = "Sign in to select a policy.";
    private int? _selectedPolicyRuleCount;
    private int? _selectedPolicyRuleLimit;
    private bool _policiesLoaded;
    private bool _policiesLoading;
    private bool _policyCountLoading;
    private int _policiesGeneration;
    private int _policyCountRequest;
    private bool _requireBusinessJustification;
    private bool _requireWindowsAuthentication;
    private EpmReusableCertificateSetting? _selectedReusableCertificateSetting;
    private bool _reusableCertificatesLoaded;
    private bool _reusableCertificatesLoading;
    private string? _reusableCertificatesLoadError;
    private int _reusableCertificatesGeneration;

    public MainViewModel()
    {
        ChildProcessBehaviors = Enum.GetValues<ChildProcessBehavior>();
        AnalyzeCommand = new AsyncCommand(AnalyzeAsync, () => !Busy && !string.IsNullOrWhiteSpace(FilePath));
        ValidateCommand = new RelayCommand(Validate, () => !Busy && _hasAnalyzedFile);
        UploadRuleCommand = new AsyncCommand(UploadRuleAsync, () => !Busy && _hasAnalyzedFile && !_policiesLoading && !_policyCountLoading && GraphConnected && (IsCreatingPolicy || SelectedElevationPolicy is not null));
        ToggleExplorerMenuCommand = new RelayCommand(ToggleExplorerMenu, () => !Busy);
        RepairExplorerMenuCommand = new RelayCommand(() => SetExplorerMenu(registered: true), () => !Busy && _explorerMenuRegistered);
        ValidationMessages.CollectionChanged += (_, _) =>
        {
            Raise(nameof(BlockingValidationMessages));
            Raise(nameof(ValidationWarningsLabel));
            Raise(nameof(ValidationWarningsDetails));
        };
        try { _explorerMenuRegistered = ExplorerContextMenuService.IsRegistered(); }
        catch { /* An inaccessible optional registration must not block startup. */ }
        ConnectGraphCommand = new AsyncCommand(ConnectGraphAsync, () => !Busy && !GraphConnected);
        DisconnectGraphCommand = new AsyncCommand(DisconnectGraphAsync, () => !Busy && GraphConnected);
        ElevationPolicyOptions.Add(_newPolicyOption);
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
    public ICommand ToggleExplorerMenuCommand { get; }
    public ICommand RepairExplorerMenuCommand { get; }
    public ICommand UploadRuleCommand { get; }
    public ICommand ConnectGraphCommand { get; }
    public ICommand DisconnectGraphCommand { get; }
    public ObservableCollection<string> ValidationMessages { get; } = [];
    public ObservableCollection<ElevationPolicyOption> ElevationPolicyOptions { get; } = [];
    public ObservableCollection<EpmReusableCertificateSetting> ReusableCertificateSettings { get; } = [];

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (!Set(ref _filePath, value)) return;
            _fileRevision++;
            _analysisCancellation?.Cancel();
            ResetFileFields(resetPolicyDraft: true);
            Raise(nameof(AnalyzeCommand));
        }
    }
    public string ExplorerMenuButtonText => _explorerMenuRegistered ? "Remove menu" : "Add menu";
    public bool HasExplorerMenu => _explorerMenuRegistered;
    public string ExplorerMenuToolTip => (_explorerMenuRegistered
        ? "Remove 'Create Intune EPM Rule' from the Explorer context menu."
        : "Add 'Create Intune EPM Rule' to the Explorer context menu for EXE, MSI and PS1 files.") +
        " Uses a fixed application copy in Local AppData, so the downloaded EXE can be moved or deleted. Current user only; no admin rights required. On Windows 11: Show more options.";
    public string ExplorerMenuRepairToolTip => "Repair the context menu or update its fixed application copy using this EXE. Target: " +
        ExplorerApplicationDeployment.GetInstalledPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) +
        ". Close other app instances launched from Explorer before updating.";
    public IEnumerable<string> BlockingValidationMessages => ValidationMessages.Where(message => message.StartsWith("Error:", StringComparison.Ordinal));
    public string ValidationWarningsLabel
    {
        get
        {
            var count = ValidationMessages.Count(message => message.StartsWith("Warning:", StringComparison.Ordinal));
            return count > 0 ? $"{count} warning(s)" : string.Empty;
        }
    }
    public string ValidationWarningsDetails => string.Join(Environment.NewLine, ValidationMessages.Where(message => message.StartsWith("Warning:", StringComparison.Ordinal)));
    public string Sha256 { get => _sha256; set => Set(ref _sha256, value); }
    public string ProductName { get => _productName; set => Set(ref _productName, value); }
    public string Version { get => _version; set => Set(ref _version, value); }
    public string CompanyName { get => _companyName; set => Set(ref _companyName, value); }
    public string EpmFileHash { get => _epmFileHash; set { if (Set(ref _epmFileHash, value)) Raise(nameof(HasEpmFileHash)); } }
    public bool HasEpmFileHash => !string.IsNullOrWhiteSpace(EpmFileHash);
    public bool IsEpmCmdletsAvailable => _fileService.IsEpmCmdletsAvailable;
    public bool CanUseEpmCmdlets => IsEpmCmdletsAvailable && !Busy;
    public bool UseEpmCmdlets
    {
        get => _useEpmCmdlets;
        set => Set(ref _useEpmCmdlets, value && IsEpmCmdletsAvailable);
    }
    public string EpmCmdletsToolTip => IsEpmCmdletsAvailable
        ? "Optional PowerShell file attributes. The module may require administrator rights. Import errors do not block local analysis. Off by default."
        : $"Unavailable: {EpmCmdletsReader.ModulePath} was not found. Local analysis does not need it.";
    public void RefreshEpmCmdletsAvailability()
    {
        if (!IsEpmCmdletsAvailable) UseEpmCmdlets = false;
        Raise(nameof(IsEpmCmdletsAvailable));
        Raise(nameof(CanUseEpmCmdlets));
        Raise(nameof(EpmCmdletsToolTip));
    }
    public string InternalName { get => _internalName; set => Set(ref _internalName, value); }
    public string FileDescription { get => _fileDescription; set => Set(ref _fileDescription, value); }
    public CertificateAttributes? AnalyzedPublisherCertificate
    {
        get => _analyzedPublisherCertificate;
        private set
        {
            if (!Set(ref _analyzedPublisherCertificate, value)) return;
            Raise(nameof(HasAnalyzedPublisherCertificate));
            Raise(nameof(AnalyzedCertificateValidity));
            Raise(nameof(AnalyzedCertificateDetails));
            Raise(nameof(UseAnalyzedCertificate));
        }
    }
    public bool HasAnalyzedPublisherCertificate => AnalyzedPublisherCertificate is not null;
    public CertificateChainResult? AnalyzedCertificateChain
    {
        get => _analyzedCertificateChain;
        private set
        {
            if (!Set(ref _analyzedCertificateChain, value)) return;
            Raise(nameof(CanExportCertificateChain));
            Raise(nameof(CertificateChainLabel));
            Raise(nameof(CertificateChainDetails));
        }
    }
    public bool CanExportCertificateChain => !Busy && AnalyzedCertificateChain is { Certificates.Count: > 0 };
    public string CertificateChainLabel => AnalyzedCertificateChain is { } chain
        ? $"{chain.Certificates.Count} certificate(s) - {(chain.IsComplete ? "Complete chain" : "Incomplete chain")}" : string.Empty;
    public string CertificateChainDetails => AnalyzedCertificateChain is { } chain
        ? string.Join("\n", chain.Certificates.Select((certificate, index) => $"{index + 1}. {certificate.Subject}"))
            + "\n\n" + string.Join("\n", chain.Status)
            + "\n\nBuilt from embedded certificates and local stores. No downloads or revocation checks. Completeness does not establish signature trust. Export contains public certificates only."
        : string.Empty;
    public string AnalyzedCertificateValidity => AnalyzedPublisherCertificate is { } certificate
        ? $"{certificate.NotBefore.ToLocalTime():d} – {certificate.NotAfter.ToLocalTime():d}" +
            (certificate.IsWithinValidityPeriod ? string.Empty : " · Outside validity period")
        : string.Empty;
    public string AnalyzedCertificateDetails => AnalyzedPublisherCertificate is { } certificate
        ? $"Publisher: {certificate.Subject}\nIssuer: {certificate.Issuer}\nThumbprint: {certificate.Thumbprint}\nValidity: {AnalyzedCertificateValidity}\n\nExtracted from the analyzed file. Signature trust has not been verified."
        : string.Empty;
    public CertificateAttributes? PublisherCertificate
    {
        get => _publisherCertificate;
        private set
        {
            if (!Set(ref _publisherCertificate, value)) return;
            Raise(nameof(HasPublisherCertificate));
            Raise(nameof(PublisherCertificateChoiceText));
            Raise(nameof(CertificateSelectionLabel));
        }
    }
    public string SignatureStatus { get => _signatureStatus; set => Set(ref _signatureStatus, value); }
    public string SignatureDetails { get => _signatureDetails; private set => Set(ref _signatureDetails, value); }
    public string AnalysisWarnings { get => _analysisWarnings; set { if (Set(ref _analysisWarnings, value)) Raise(nameof(HasAnalysisWarnings)); } }
    public bool HasAnalysisWarnings => !string.IsNullOrWhiteSpace(AnalysisWarnings);
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
            Raise(nameof(ReusableCertificateSelectionHint));
            Raise(nameof(ReusableCertificateStatusLabel));
            if (ShowReusableCertificateSource)
                _ = EnsureReusableCertificateSettingsAsync();
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
    public bool HasUploadedCertificate => _uploadedCertificate is not null;
    public bool UseAnalyzedCertificate
    {
        get => !_useUploadedCertificate && HasAnalyzedPublisherCertificate;
        set { if (value && HasAnalyzedPublisherCertificate) SelectCertificate(uploaded: false); }
    }
    public bool UseUploadedCertificate
    {
        get => _useUploadedCertificate && HasUploadedCertificate;
        set { if (value && HasUploadedCertificate) SelectCertificate(uploaded: true); }
    }
    public string UploadedCertificateLabel => HasUploadedCertificate ? Path.GetFileName(SelectedCertificateFile) : "Uploaded certificate";
    private void SelectCertificate(bool uploaded)
    {
        _useUploadedCertificate = uploaded;
        PublisherCertificate = uploaded ? _uploadedCertificate : AnalyzedPublisherCertificate;
        Raise(nameof(UseAnalyzedCertificate));
        Raise(nameof(UseUploadedCertificate));
        Raise(nameof(PublisherCertificateChoiceText));
        Raise(nameof(CertificateSelectionLabel));
    }
    public string PublisherCertificateChoiceText => UseUploadedCertificate
        ? $"Selected certificate file: {Path.GetFileName(SelectedCertificateFile)}"
        : PublisherCertificate is { } certificate
            ? $"Using analyzed file's publisher certificate - {certificate.Subject} (thumbprint {certificate.Thumbprint})"
            : "No certificate selected. Choose a certificate file or analyze a signed file.";
    public string CertificateSelectionLabel => UseUploadedCertificate ? Path.GetFileName(SelectedCertificateFile)
        : HasPublisherCertificate ? "Analyzed file certificate" : "No certificate selected";
    public string SelectedCertificateFile { get => _selectedCertificateFile; private set { if (Set(ref _selectedCertificateFile, value)) { Raise(nameof(CertificateSelectionLabel)); Raise(nameof(PublisherCertificateChoiceText)); } } }
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
    public bool Busy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(CanChooseFile)); Raise(nameof(CanSelectElevationPolicy)); Raise(nameof(AnalyzeCommand)); Raise(nameof(UploadRuleCommand)); Raise(nameof(CanUseEpmCmdlets)); Raise(nameof(CanExportCertificateChain)); } }
    public bool CanChooseFile => !Busy;
    public string ClientId { get => _clientId; set { Set(ref _clientId, value); SaveSettings(); } }
    public string TenantId { get => _tenantId; set { Set(ref _tenantId, value); SaveSettings(); } }
    public string GraphConnectionStatus { get => _graphConnectionStatus; private set { if (Set(ref _graphConnectionStatus, value)) Raise(nameof(GraphConnectionLabel)); } }
    public string GraphConnectionLabel => GraphConnectionStatus.StartsWith("Connection failed:", StringComparison.Ordinal)
        ? "Sign-in failed" : GraphConnected ? "Connected" : "Not signed in";
    public string GraphAccount { get => _graphAccount; private set { if (Set(ref _graphAccount, value)) Raise(nameof(AccountButtonLabel)); } }
    public string AccountInitials { get => _accountInitials; private set => Set(ref _accountInitials, value); }
    public bool IsAccountMenuOpen { get => _isAccountMenuOpen; set => Set(ref _isAccountMenuOpen, value); }
    public string AccountButtonLabel => GraphConnected ? $"Microsoft Entra account: {GraphAccount}" : "Sign in to Microsoft Entra (optional)";
    public bool GraphConnected { get => _graphConnected; private set { Set(ref _graphConnected, value); Raise(nameof(GraphConnectionLabel)); Raise(nameof(CanSelectElevationPolicy)); Raise(nameof(AccountButtonLabel)); Raise(nameof(ReusableCertificateSelectionHint)); Raise(nameof(ReusableCertificateStatusLabel)); Raise(nameof(ConnectGraphCommand)); Raise(nameof(DisconnectGraphCommand)); Raise(nameof(UploadRuleCommand)); } }
    public bool CanSelectElevationPolicy => GraphConnected && !Busy;
    public ElevationPolicyOption? SelectedPolicyOption
    {
        get => _selectedPolicyOption;
        set
        {
            if (!Set(ref _selectedPolicyOption, value)) return;
            SelectedElevationPolicy = value?.Policy;
            Raise(nameof(IsCreatingPolicy));
            Raise(nameof(UploadButtonText));
            Raise(nameof(UploadRuleCommand));
            if (IsCreatingPolicy)
                PolicyLoadStatus = string.Empty;
        }
    }
    public bool IsCreatingPolicy => SelectedPolicyOption?.IsNewPolicy == true;
    public string NewPolicyName { get => _newPolicyName; set => Set(ref _newPolicyName, value); }
    public string NewPolicyDescription { get => _newPolicyDescription; set => Set(ref _newPolicyDescription, value); }
    public string UploadButtonText => IsCreatingPolicy ? "Create policy and upload rule" : "Upload rule to selected Intune policy";
    public ElevationRulePolicy? SelectedElevationPolicy
    {
        get => _selectedElevationPolicy;
        set
        {
            if (EqualityComparer<ElevationRulePolicy?>.Default.Equals(_selectedElevationPolicy, value)) return;
            Set(ref _selectedElevationPolicy, value);
            _selectedPolicyRuleCount = null;
            _selectedPolicyRuleLimit = null;
            _policyCountRequest++;
            _policyCountLoading = false;
            Raise(nameof(SelectedPolicySummary));
            Raise(nameof(UploadRuleCommand));
            if (value is not null && GraphConnected && !Busy)
                _ = LoadSelectedPolicyCountAsync();
        }
    }
    public string PolicyLoadStatus { get => _policyLoadStatus; private set => Set(ref _policyLoadStatus, value); }
    public string SelectedPolicySummary => SelectedElevationPolicy is null || _selectedPolicyRuleCount is null
        ? string.Empty
        : $"Elevation rules: {_selectedPolicyRuleCount}/{_selectedPolicyRuleLimit ?? 100}.";

    private async Task AnalyzeAsync()
    {
        RefreshEpmCmdletsAvailability();
        var useEpmCmdlets = UseEpmCmdlets;
        var path = FilePath;
        var revision = _fileRevision;
        using var cancellation = new CancellationTokenSource();
        _analysisCancellation = cancellation;
        ResetFileFields();
        Busy = true;
        try
        {
            var result = await _fileService.AnalyzeAsync(path, useEpmCmdlets, cancellation.Token);
            if (revision != _fileRevision || cancellation.IsCancellationRequested) return;
            SelectedCertificateFile = string.Empty;
            AnalyzedPublisherCertificate = result.PublisherCertificate;
            AnalyzedCertificateChain = result.CertificateChain;
            PublisherCertificate = result.PublisherCertificate;
            Sha256 = result.Sha256;
            ProductName = result.ProductName ?? string.Empty;
            Version = result.Version ?? string.Empty;
            CompanyName = result.CompanyName ?? string.Empty;
            EpmFileHash = result.EpmFileHash ?? string.Empty;
            InternalName = result.InternalName ?? string.Empty;
            FileDescription = result.Description ?? string.Empty;
            RuleFileDescription = result.Description ?? string.Empty;
            SignatureStatus = result.PublisherCertificate is null ? "No certificate" : "Certificate available";
            SignatureDetails = result.SignatureStatus ?? "Signature information unavailable.";
            AnalysisWarnings = string.Join(Environment.NewLine, result.Warnings);
            RuleFileName = result.FileName;
            // Preserve detected metadata in the editable rule field; validation reports
            // unsupported version formats instead of silently blanking the prefilled value.
            RuleFileVersion = RuleValidationService.NormalizeMinimumFileVersionToFourParts(result.Version?.Trim()) ?? string.Empty;
            RuleFileHash = result.Sha256;
            RuleFilePath = result.FilePath;
            RuleProductName = result.ProductName ?? string.Empty;
            RuleInternalName = result.InternalName ?? string.Empty;
            RuleName = $"{result.ProductName ?? Path.GetFileNameWithoutExtension(result.FileName)} - Elevation Rule";
            _hasAnalyzedFile = true;
            ValidationSummary = "Ready.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (revision != _fileRevision) return;
            ValidationSummary = "File analysis failed.";
            MessageBox.Show(exception.Message, "File analysis", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (ReferenceEquals(_analysisCancellation, cancellation))
                _analysisCancellation = null;
            Busy = false;
        }
    }

    private void ResetFileFields(bool resetPolicyDraft = false)
    {
        _hasAnalyzedFile = false;
        Sha256 = EpmFileHash = ProductName = Version = CompanyName = InternalName = FileDescription = string.Empty;
        PublisherCertificate = null;
        AnalyzedPublisherCertificate = null;
        AnalyzedCertificateChain = null;
        _uploadedCertificate = null;
        _useUploadedCertificate = false;
        SelectedCertificateFile = SignatureDetails = AnalysisWarnings = string.Empty;
        Raise(nameof(HasUploadedCertificate));
        Raise(nameof(UploadedCertificateLabel));
        Raise(nameof(UseAnalyzedCertificate));
        Raise(nameof(UseUploadedCertificate));
        SignatureStatus = "Not analyzed";
        RuleName = RuleDescription = RuleFileName = RuleFileVersion = RuleFileHash = RuleFileDescription = string.Empty;
        RuleFilePath = RuleProductName = RuleInternalName = ArgumentListText = string.Empty;
        if (resetPolicyDraft)
            NewPolicyName = NewPolicyDescription = string.Empty;
        SelectedReusableCertificateSetting = null;
        SignatureSource = global::IntuneEpmRuleCreator.Core.Models.SignatureSource.NotConfigured;
        CertificateType = global::IntuneEpmRuleCreator.Core.Models.CertificateType.Publisher;
        ElevationType = global::IntuneEpmRuleCreator.Core.Models.ElevationType.UserConfirmed;
        ChildProcessBehavior = global::IntuneEpmRuleCreator.Core.Models.ChildProcessBehavior.RequireRuleToElevate;
        RestrictArguments = RestrictArgumentsMode.NotConfigured;
        RequireBusinessJustification = RequireWindowsAuthentication = false;
        ValidationMessages.Clear();
        ValidationSummary = string.IsNullOrWhiteSpace(FilePath) ? "Select a file." : "Ready to analyze.";
        if (!_policiesLoading && !_policyCountLoading)
            PolicyLoadStatus = GraphConnected ? string.Empty : "Sign in to select a policy.";
        Raise(nameof(UploadRuleCommand));
    }

    private void ToggleExplorerMenu() => SetExplorerMenu(registered: !_explorerMenuRegistered);

    private void SetExplorerMenu(bool registered)
    {
        try
        {
            if (registered) ExplorerContextMenuService.Register(Environment.ProcessPath ?? throw new InvalidOperationException("The application path is unavailable."));
            else ExplorerContextMenuService.Remove();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Explorer context menu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { _explorerMenuRegistered = ExplorerContextMenuService.IsRegistered(); }
            catch { }
            Raise(nameof(ExplorerMenuButtonText));
            Raise(nameof(ExplorerMenuToolTip));
            Raise(nameof(HasExplorerMenu));
        }
    }

    private async Task ConnectGraphAsync()
    {
        Busy = true;
        try
        {
            var window = Application.Current?.MainWindow;
            var profile = await _graphAuthentication.SignInAsync(ClientId, TenantId, window is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(window).Handle);
            ResetReusableCertificateSettings();
            ResetElevationPolicies();
            GraphConnected = true;
            GraphAccount = $"{profile.DisplayName} ({profile.UserPrincipalName})";
            var nameParts = (string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.UserPrincipalName : profile.DisplayName)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            AccountInitials = nameParts.Length == 0 ? "?"
                : (nameParts[0][..1] + (nameParts.Length > 1 ? nameParts[^1][..1] : string.Empty)).ToUpperInvariant();
            GraphConnectionStatus = "Connected to Microsoft Graph. Intune is changed only when you explicitly upload a rule.";
            TenantId = profile.TenantId;
            IsAccountMenuOpen = false;
            await EnsureElevationPoliciesAsync();
            await EnsureReusableCertificateSettingsAsync();
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
        ResetElevationPolicies();
        ResetReusableCertificateSettings();
        SelectedElevationPolicy = null;
        _selectedPolicyRuleCount = null;
        _selectedPolicyRuleLimit = null;
        GraphAccount = string.Empty;
        AccountInitials = string.Empty;
        IsAccountMenuOpen = false;
        GraphConnectionStatus = "Disconnected. Local file analysis remains available offline.";
        PolicyLoadStatus = "Sign in to select a policy.";
    }

    public async Task EnsureElevationPoliciesAsync()
    {
        if (!GraphConnected || _policiesLoading) return;
        if (_policiesLoaded)
        {
            if (SelectedElevationPolicy is not null && _selectedPolicyRuleCount is null && !_policyCountLoading)
                await LoadSelectedPolicyCountAsync();
            return;
        }

        var generation = _policiesGeneration;
        _policiesLoading = true;
        Raise(nameof(UploadRuleCommand));
        PolicyLoadStatus = "Loading EPM policies…";
        try
        {
            var policies = await _graphAuthentication.GetElevationRulePoliciesAsync();
            if (generation != _policiesGeneration || !GraphConnected) return;
            var selectedId = SelectedElevationPolicy?.Id;
            var creatingPolicy = IsCreatingPolicy;
            ElevationPolicyOptions.Clear();
            foreach (var policy in policies.OrderBy(policy => policy.Name, StringComparer.CurrentCultureIgnoreCase))
                ElevationPolicyOptions.Add(new ElevationPolicyOption(policy));
            ElevationPolicyOptions.Add(_newPolicyOption);
            _policiesLoaded = true;
            PolicyLoadStatus = policies.Count == 0
                ? "No policies yet - choose + Add new."
                : string.Empty;
            SelectedPolicyOption = creatingPolicy ? _newPolicyOption
                : ElevationPolicyOptions.FirstOrDefault(option => option.Policy?.Id == selectedId && option.Policy is not null);
        }
        catch (Exception exception)
        {
            if (generation == _policiesGeneration)
                PolicyLoadStatus = $"Could not load policies: {exception.Message} Open the dropdown to retry.";
        }
        finally
        {
            if (generation == _policiesGeneration)
            {
                _policiesLoading = false;
                Raise(nameof(UploadRuleCommand));
            }
        }
    }

    private void ResetElevationPolicies()
    {
        _policiesGeneration++;
        _policyCountRequest++;
        _policiesLoaded = false;
        _policiesLoading = false;
        _policyCountLoading = false;
        ElevationPolicyOptions.Clear();
        ElevationPolicyOptions.Add(_newPolicyOption);
        SelectedPolicyOption = null;
        _selectedPolicyRuleCount = null;
        _selectedPolicyRuleLimit = null;
        Raise(nameof(SelectedPolicySummary));
    }

    private async Task LoadSelectedPolicyCountAsync()
    {
        if (SelectedElevationPolicy is not { } policy || !GraphConnected) return;
        var generation = _policiesGeneration;
        var request = ++_policyCountRequest;
        _policyCountLoading = true;
        Raise(nameof(UploadRuleCommand));
        PolicyLoadStatus = "Reading rule count…";
        try
        {
            var collection = await _graphAuthentication.GetElevationRuleCollectionInfoAsync(policy);
            if (generation != _policiesGeneration || request != _policyCountRequest) return;
            _selectedPolicyRuleCount = collection.RuleCount;
            _selectedPolicyRuleLimit = collection.MaximumRuleCount;
            Raise(nameof(SelectedPolicySummary));
            PolicyLoadStatus = string.Empty;
        }
        catch (Exception exception)
        {
            if (generation != _policiesGeneration || request != _policyCountRequest) return;
            _selectedPolicyRuleCount = null;
            _selectedPolicyRuleLimit = null;
            Raise(nameof(SelectedPolicySummary));
            PolicyLoadStatus = $"Could not read rule count: {exception.Message} Open the policy dropdown to retry.";
        }
        finally
        {
            if (generation == _policiesGeneration && request == _policyCountRequest)
            {
                _policyCountLoading = false;
                Raise(nameof(UploadRuleCommand));
            }
        }
    }

    public async Task EnsureReusableCertificateSettingsAsync()
    {
        if (!GraphConnected || !ShowReusableCertificateSource || _reusableCertificatesLoaded || _reusableCertificatesLoading)
            return;

        var generation = _reusableCertificatesGeneration;
        _reusableCertificatesLoading = true;
        _reusableCertificatesLoadError = null;
        Raise(nameof(ReusableCertificateSelectionHint));
        Raise(nameof(ReusableCertificateStatusLabel));
        try
        {
            var settings = await _graphAuthentication.GetEpmReusableCertificateSettingsAsync();
            // Discard a response from an account that has since signed out.
            if (generation != _reusableCertificatesGeneration || !GraphConnected) return;
            ReusableCertificateSettings.Clear();
            SelectedReusableCertificateSetting = null;
            foreach (var setting in settings.OrderBy(setting => setting.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                ReusableCertificateSettings.Add(setting);
            _reusableCertificatesLoaded = true;
        }
        catch (Exception exception)
        {
            if (generation == _reusableCertificatesGeneration)
                _reusableCertificatesLoadError = $"Could not load certificates: {exception.Message} Open the dropdown to retry.";
        }
        finally
        {
            if (generation == _reusableCertificatesGeneration)
            {
                _reusableCertificatesLoading = false;
                Raise(nameof(ReusableCertificateSelectionHint));
                Raise(nameof(ReusableCertificateStatusLabel));
            }
        }
    }

    private void ResetReusableCertificateSettings()
    {
        _reusableCertificatesGeneration++;
        _reusableCertificatesLoaded = false;
        _reusableCertificatesLoading = false;
        _reusableCertificatesLoadError = null;
        ReusableCertificateSettings.Clear();
        SelectedReusableCertificateSetting = null;
        Raise(nameof(ReusableCertificateSelectionHint));
        Raise(nameof(ReusableCertificateStatusLabel));
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

    public string ReusableCertificateStatusLabel => !GraphConnected ? "Sign in to load certificates."
        : _reusableCertificatesLoading ? "Loading certificates…"
        : _reusableCertificatesLoadError is not null ? "Could not load certificates."
        : _reusableCertificatesLoaded && ReusableCertificateSettings.Count == 0 ? "No certificates found." : string.Empty;

    public string ReusableCertificateSelectionHint => !GraphConnected
        ? "Sign in using the account button at the top right to choose a reusable certificate."
        : _reusableCertificatesLoading
            ? "Loading EPM certificates…"
            : _reusableCertificatesLoadError ?? (_reusableCertificatesLoaded && ReusableCertificateSettings.Count == 0
                ? "No reusable EPM certificates were found in this tenant."
                : "Choose a reusable EPM certificate from the dropdown.");

    public void LoadCertificateFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var certificate = bytes.Length > 0 && Encoding.UTF8.GetString(bytes).Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
            ? X509Certificate2.CreateFromPem(Encoding.UTF8.GetString(bytes))
            : X509CertificateLoader.LoadCertificate(bytes);
        _uploadedCertificate = new CertificateAttributes(
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
        Raise(nameof(HasUploadedCertificate));
        Raise(nameof(UploadedCertificateLabel));
        SelectCertificate(uploaded: true);
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
        var result = ValidateRuleForTarget(BuildRule());
        ValidationMessages.Clear();
        foreach (var issue in result.Issues)
            ValidationMessages.Add($"{(issue.IsBlocking ? "Error" : "Warning")}: {issue.Message}");
        ValidationSummary = result.CanSubmit ? "Validation passed." : "Validation failed.";
    }

    private async Task UploadRuleAsync()
    {
        if (IsCreatingPolicy)
        {
            await CreatePolicyAndRuleAsync();
            return;
        }
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
                ValidationSummary = "Validation failed.";
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

    private RuleValidationResult ValidateRuleForTarget(ElevationRule rule)
    {
        var result = _validator.Validate(rule, IsCreatingPolicy ? 0 : _selectedPolicyRuleCount ?? -1,
            hasPolicy: IsCreatingPolicy || SelectedElevationPolicy is not null);
        if (IsCreatingPolicy && string.IsNullOrWhiteSpace(NewPolicyName))
            return new RuleValidationResult([.. result.Issues,
                new ValidationIssue("policy.name.required", "Enter a name for the new Elevation Rules policy.", true)]);
        return result;
    }

    private async Task CreatePolicyAndRuleAsync()
    {
        var rule = BuildRule();
        var name = NewPolicyName.Trim();
        var description = NewPolicyDescription;
        ElevationRulePolicy? created = null;
        Busy = true;
        try
        {
            var validation = ValidateRuleForTarget(rule);
            ValidationMessages.Clear();
            foreach (var issue in validation.Issues)
                ValidationMessages.Add($"{(issue.IsBlocking ? "Error" : "Warning")}: {issue.Message}");
            if (!validation.CanSubmit)
            {
                ValidationSummary = "Validation failed.";
                return;
            }

            var message = $"Create Elevation Rules policy '{name}' with rule '{rule.Name}'?\n\nThe policy is created without assignments and with the default scope tag (0).";
            var warnings = string.Join(Environment.NewLine, validation.Issues.Where(issue => !issue.IsBlocking).Select(issue => issue.Message));
            if (!string.IsNullOrWhiteSpace(warnings)) message += $"\n\nWarnings:\n{warnings}";
            if (MessageBox.Show(message, "Create policy and upload rule", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                PolicyLoadStatus = "Creation cancelled; no policy was created.";
                return;
            }

            PolicyLoadStatus = $"Creating '{name}' with its first rule…";
            created = await _graphAuthentication.CreateElevationRulePolicyAsync(name, description, rule);
            var option = new ElevationPolicyOption(created);
            ElevationPolicyOptions.Insert(Math.Max(0, ElevationPolicyOptions.Count - 1), option);
            SelectedPolicyOption = option;
            PolicyLoadStatus = $"Policy '{created.Name}' created. Verifying its first rule…";
            var collection = await _graphAuthentication.GetElevationRuleCollectionInfoAsync(created);
            _selectedPolicyRuleCount = collection.RuleCount;
            _selectedPolicyRuleLimit = collection.MaximumRuleCount;
            Raise(nameof(SelectedPolicySummary));
            if (collection.RuleCount != 1)
                throw new InvalidOperationException($"Readback returned {collection.RuleCount} rules instead of 1.");
            ValidationSummary = "Policy created and rule uploaded successfully.";
            PolicyLoadStatus = $"Created '{created.Name}' without assignments. Configure assignments in Intune when ready.";
        }
        catch (Exception exception)
        {
            ValidationSummary = created is null ? "Policy creation failed." : "Policy created; verification failed.";
            PolicyLoadStatus = created is null ? exception.Message
                : $"Policy '{created.Name}' (ID: {created.Id}) was created. Check Intune before retrying: {exception.Message}";
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

    private sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
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

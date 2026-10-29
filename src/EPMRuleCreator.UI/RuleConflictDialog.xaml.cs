using System.Windows;
using EPMRuleCreator.Graph;
using EPMRuleCreator.UI.ViewModels;

namespace EPMRuleCreator.UI;

public partial class RuleConflictDialog : Window
{
    internal RuleConflictDecision? Decision { get; private set; }

    public RuleConflictDialog(IReadOnlyList<ElevationRuleMatch> matches, string incomingFileName, string? incomingVersion)
    {
        InitializeComponent();
        IncomingRuleText.Text = $"Incoming file: {incomingFileName}\nIncoming minimum version: {FormatVersion(incomingVersion)}";
        MatchesList.ItemsSource = matches.Select(match => new RuleMatchDisplay(
            match,
            match.Name,
            $"File: {match.FileName}    Hash: {FormatHash(match.FileHash)}    Signer: {FormatHash(match.Signature)}",
            $"{match.VersionDisplayReason}; stored minimum version: {FormatVersion(match.FileVersion)}; incoming: {FormatVersion(incomingVersion)}",
            match.MatchReason)).ToArray();
        UpdateSelectionWarning();
    }

    private void MatchesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateSelectionWarning();

    private void UpdateSelectionWarning()
    {
        if (MatchesList.SelectedItem is not RuleMatchDisplay selected)
        {
            SelectionWarningText.Text = "Select a matching rule to continue.";
            UpdateButton.IsEnabled = false;
            AddButton.IsEnabled = false;
            return;
        }

        UpdateButton.IsEnabled = selected.CanUpdate;
        AddButton.IsEnabled = selected.CanAdd;
        if (!selected.CanUpdate)
        {
            SelectionWarningText.Text = $"Name conflict: another existing rule is already named '{selected.Match.OtherRuleWithSameName}'. Rename the incoming rule before updating or adding it.";
        }
        else if (!selected.CanAdd)
        {
            SelectionWarningText.Text = "A rule with this name already exists. Updating the selected rule replaces it; adding another rule with the same name is unavailable.";
        }
        else if (!selected.Match.VersionMatches)
        {
            SelectionWarningText.Text = $"Version warning: the selected rule's stored minimum version differs from the incoming version. Updating replaces it with {selected.VersionDetails}.";
        }
        else
        {
            SelectionWarningText.Text = $"Selected rule: '{selected.Title}'. Updating replaces this rule; adding keeps it and creates a separate rule.";
        }
    }

    private void UpdateSelected_Click(object sender, RoutedEventArgs e)
    {
        if (MatchesList.SelectedItem is not RuleMatchDisplay { CanUpdate: true } selected) return;
        Decision = new RuleConflictDecision(true, selected.Match);
        DialogResult = true;
    }

    private void AddAdditional_Click(object sender, RoutedEventArgs e)
    {
        var selected = MatchesList.SelectedItem as RuleMatchDisplay;
        if (selected is not { CanAdd: true }) return;
        Decision = new RuleConflictDecision(false, selected?.Match);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Decision = null;
        DialogResult = false;
    }

    private static string FormatVersion(string? version) => string.IsNullOrWhiteSpace(version) ? "not set" : version;
    private static string FormatHash(string? hash) => string.IsNullOrWhiteSpace(hash) ? "not set" : hash;

    private sealed record RuleMatchDisplay(ElevationRuleMatch Match, string Title, string Details, string VersionDetails, string MatchReason)
    {
        public bool CanUpdate => string.IsNullOrWhiteSpace(Match.OtherRuleWithSameName);
        public bool CanAdd => !Match.IncomingNameAlreadyExists;
    }
}

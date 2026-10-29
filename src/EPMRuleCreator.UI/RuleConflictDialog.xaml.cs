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
    }

    private void UpdateSelected_Click(object sender, RoutedEventArgs e)
    {
        if (MatchesList.SelectedItem is not RuleMatchDisplay selected) return;
        Decision = new RuleConflictDecision(true, selected.Match);
        DialogResult = true;
    }

    private void AddAdditional_Click(object sender, RoutedEventArgs e)
    {
        var selected = MatchesList.SelectedItem as RuleMatchDisplay;
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

    private sealed record RuleMatchDisplay(ElevationRuleMatch Match, string Title, string Details, string VersionDetails, string MatchReason);
}

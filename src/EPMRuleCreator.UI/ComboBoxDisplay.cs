using System.Windows;

namespace EPMRuleCreator.UI;

/// <summary>Optional closed-dropdown text; leaves popup item labels unchanged.</summary>
public static class ComboBoxDisplay
{
    public static readonly DependencyProperty SelectionTextProperty = DependencyProperty.RegisterAttached(
        "SelectionText", typeof(string), typeof(ComboBoxDisplay), new FrameworkPropertyMetadata(null));

    public static string? GetSelectionText(DependencyObject element) => (string?)element.GetValue(SelectionTextProperty);
    public static void SetSelectionText(DependencyObject element, string? value) => element.SetValue(SelectionTextProperty, value);
}

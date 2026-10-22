namespace EPMRuleCreator.Core.Models;

public static class ThemePalette
{
    public static IReadOnlyDictionary<string, string> GetColors(bool dark) => new Dictionary<string, string>
    {
        ["CanvasBrush"] = dark ? "#0E1623" : "#F4F7FB",
        ["CardBrush"] = dark ? "#172231" : "#FFFFFF",
        ["InkBrush"] = dark ? "#E8EEF7" : "#182230",
        ["MutedBrush"] = dark ? "#A8B7C9" : "#58677A",
        ["AccentBrush"] = dark ? "#55D9D0" : "#0F766E",
        ["PrimaryBrush"] = dark ? "#117C72" : "#0F766E",
        ["OnPrimaryBrush"] = "#FFFFFF",
        ["AccentSoftBrush"] = dark ? "#183E3D" : "#E8F5F2",
        ["BorderBrush"] = dark ? "#34455B" : "#DCE4ED",
        ["SurfaceBrush"] = dark ? "#1E2B3D" : "#F8FAFC",
        ["InputHoverBrush"] = dark ? "#6E879E" : "#AABBCB",
        ["WarningBrush"] = dark ? "#F6C177" : "#8A4B00",
        ["ErrorBrush"] = dark ? "#FFB4A9" : "#B42318",
        ["HoverOverlayBrush"] = dark ? "#FFFFFF" : "#182230"
    };
}

namespace EPMRuleCreator.Core.Models;

public static class ThemePalette
{
    public static IReadOnlyDictionary<string, string> GetColors(bool dark) => new Dictionary<string, string>
    {
        // Inspired by the supplied Zero Trust Stories screenshot, not scraped CSS.
        ["CanvasBrush"] = dark ? "#0B1016" : "#F3F6FB",
        ["CardBrush"] = dark ? "#161D26" : "#FFFFFF",
        ["HeaderStartBrush"] = dark ? "#1A2635" : "#EAF1FB",
        ["HeaderEndBrush"] = dark ? "#121923" : "#FFFFFF",
        ["InkBrush"] = dark ? "#E7EDF6" : "#19283C",
        ["MutedBrush"] = dark ? "#A6BAD8" : "#52657E",
        ["AccentBrush"] = dark ? "#8AB4F8" : "#275BA1",
        ["PrimaryBrush"] = dark ? "#285FA8" : "#245EAA",
        ["OnPrimaryBrush"] = "#FFFFFF",
        ["AccentSoftBrush"] = dark ? "#21334C" : "#E7EFFB",
        ["BorderBrush"] = dark ? "#2D3A49" : "#D4DFED",
        ["SurfaceBrush"] = dark ? "#1C2836" : "#F6F8FC",
        ["InputHoverBrush"] = dark ? "#7992AF" : "#8DA8CA",
        ["WarningBrush"] = dark ? "#F6C177" : "#8A4B00",
        ["ErrorBrush"] = dark ? "#FFB4A9" : "#B42318",
        ["HoverOverlayBrush"] = dark ? "#FFFFFF" : "#182230"
    };
}

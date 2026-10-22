using EPMRuleCreator.Core.Services;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class AppDataPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EpmRenameTests-" + Guid.NewGuid().ToString("N"));
    private string Legacy => Path.Combine(_root, "IntuneEpmRuleCreator");

    [Fact]
    public void CopiesOnlyLegacySettingsAndKeepsOriginalAndTokenCacheUntouched()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "settings.json"), "{\"IsDarkTheme\":true}");
        File.WriteAllText(Path.Combine(Legacy, "msal.cache"), "dummy encrypted cache");
        var settings = AppDataPaths.PrepareSettingsPath(_root);
        Assert.Equal(Path.Combine(_root, "EPMRuleCreator", "settings.json"), settings);
        Assert.Equal("{\"IsDarkTheme\":true}", File.ReadAllText(settings));
        Assert.True(File.Exists(Path.Combine(Legacy, "settings.json")));
        Assert.Equal("dummy encrypted cache", File.ReadAllText(Path.Combine(Legacy, "msal.cache")));
        Assert.False(File.Exists(Path.Combine(AppDataPaths.GetRoot(_root), "msal.cache")));
    }

    [Fact]
    public void ExistingNewSettingsAreNeverOverwritten()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "settings.json"), "old settings");
        Directory.CreateDirectory(AppDataPaths.GetRoot(_root));
        var current = Path.Combine(AppDataPaths.GetRoot(_root), "settings.json");
        File.WriteAllText(current, "new settings");
        AppDataPaths.PrepareSettingsPath(_root);
        Assert.Equal("new settings", File.ReadAllText(current));
    }

    [Fact]
    public void FreshInstallUsesOnlyTheNewName()
    {
        Assert.Equal(Path.Combine(_root, "EPMRuleCreator", "settings.json"), AppDataPaths.PrepareSettingsPath(_root));
        Assert.Equal(Path.Combine(_root, "EPMRuleCreator", "App", "EPMRuleCreator.exe"), ExplorerApplicationDeployment.GetInstalledPath(_root));
    }

    [Fact]
    public void ContextMenuMigrationTargetsOnlyOwnedNewAndLegacyEntries()
    {
        var keys = ExplorerContextMenuService.GetOwnedMenuKeys();
        Assert.Equal(6, keys.Count);
        foreach (var extension in new[] { ".exe", ".msi", ".ps1" })
        {
            Assert.Contains(@"Software\Classes\SystemFileAssociations\" + extension + @"\shell\CreateEPMRule", keys);
            Assert.Contains(@"Software\Classes\SystemFileAssociations\" + extension + @"\shell\CreateIntuneEpmRule", keys);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

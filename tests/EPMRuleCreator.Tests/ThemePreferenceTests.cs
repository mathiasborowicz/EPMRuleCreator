using System.Security;
using System.Text.Json;
using EPMRuleCreator.Core.Models;
using EPMRuleCreator.Core.Services;
using Xunit;

namespace EPMRuleCreator.Tests;

public sealed class ThemePreferenceTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void WithoutManualChoiceUsesWindowsAppMode(int registryValue, bool expected)
        => Assert.Equal(expected, ThemePreference.Resolve(null, () => registryValue));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitChoiceWinsWithoutReadingWindows(bool choice)
        => Assert.Equal(choice, ThemePreference.Resolve(choice, () => throw new InvalidOperationException("Must not read Windows")));

    [Fact]
    public void MissingOrUnreadableWindowsSettingFallsBackToLight()
    {
        Assert.False(ThemePreference.Resolve(null, () => null));
        Assert.False(ThemePreference.Resolve(null, () => "0"));
        Assert.False(ThemePreference.Resolve(null, () => throw new UnauthorizedAccessException()));
        Assert.False(ThemePreference.Resolve(null, () => throw new SecurityException()));
    }

    [Fact]
    public void ExistingSignInSettingsWithoutThemeFollowWindows()
    {
        var settings = JsonSerializer.Deserialize<LocalAppSettings>("{\"ClientId\":\"test-client\",\"TenantId\":\"organizations\"}")!;
        Assert.Null(settings.IsDarkTheme);
        Assert.True(ThemePreference.Resolve(settings.IsDarkTheme, () => 0));
        var saved = JsonSerializer.Deserialize<LocalAppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Null(saved.IsDarkTheme);
        Assert.Equal(settings.ClientId, saved.ClientId);
        Assert.Equal(settings.TenantId, saved.TenantId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitThemeSurvivesSerializationAndWindowsChanges(bool choice)
    {
        var settings = new LocalAppSettings("test-client", "organizations", choice);
        var restored = JsonSerializer.Deserialize<LocalAppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(choice, restored.IsDarkTheme);
        Assert.Equal(choice, ThemePreference.Resolve(restored.IsDarkTheme, () => choice ? 1 : 0));
    }
}

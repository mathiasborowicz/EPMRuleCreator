using System.Security;

namespace IntuneEpmRuleCreator.Core.Services;

public static class ThemePreference
{
    public static bool Resolve(bool? manualChoice, Func<object?> readWindowsAppTheme)
    {
        if (manualChoice is { } choice) return choice;
        try
        {
            // Windows Personalize\AppsUseLightTheme is a DWORD: 0 = dark, 1 = light.
            return readWindowsAppTheme() is int value && value == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}

using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using IntuneEpmRuleCreator.Core.Models;
using IntuneEpmRuleCreator.Core.Services;
using Microsoft.Win32;

namespace IntuneEpmRuleCreator.UI;

public static class ThemeService
{
    public static bool ResolveStartupTheme(bool? manualChoice) => ThemePreference.Resolve(manualChoice, () =>
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
        return key?.GetValue("AppsUseLightTheme");
    });

    public static void Apply(bool dark)
    {
        if (Application.Current is not { } app) return;
        foreach (var (key, value) in ThemePalette.GetColors(dark))
            app.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        foreach (Window window in app.Windows) ApplyWindowFrame(window, dark);
    }

    internal static void ApplyWindowFrame(Window window, bool dark)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var colors = ThemePalette.GetColors(dark);
        var mode = dark ? 1 : 0;
        var caption = ColorRef(colors["CanvasBrush"]);
        var text = ColorRef(colors["InkBrush"]);
        // Documented Windows 11 DWM attributes: dark frame, caption color, caption text.
        _ = DwmSetWindowAttribute(handle, 20, ref mode, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
    }

    private static int ColorRef(string value)
    {
        var color = (Color)ColorConverter.ConvertFromString(value);
        return color.R | color.G << 8 | color.B << 16;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

// src/Memoria.App/Theming/SystemThemeReader.cs
using Microsoft.Win32;

namespace Memoria.App.Theming;

public static class SystemThemeReader
{
    public const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string AppsUseLightThemeValue = "AppsUseLightTheme";

    public static bool ParseAppsUseLightTheme(object? registryValue, bool fallbackIsLight = true) => registryValue switch
    {
        int i => i != 0,
        long l => l != 0,
        _ => fallbackIsLight,
    };

    public static bool ReadIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
        var value = key?.GetValue(AppsUseLightThemeValue);
        return ParseAppsUseLightTheme(value, fallbackIsLight: true);
    }
}

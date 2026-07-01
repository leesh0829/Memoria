// src/Memoria.App/Theming/ThemeResolver.cs
using System;
using Memoria.Core.Models;

namespace Memoria.App.Theming;

public static class ThemeResolver
{
    // 색 계열 프리셋. 각 계열은 Light/Dark 팔레트를 가진다(모드로 선택). default=중립 회색.
    public static readonly string[] Presets =
        { "default", "blue", "teal", "green", "yellow", "orange", "red", "pink", "purple" };

    public static ThemeMode ResolveEffectiveMode(ThemeMode mode, bool systemIsLight) => mode switch
    {
        ThemeMode.Light => ThemeMode.Light,
        ThemeMode.Dark => ThemeMode.Dark,
        ThemeMode.System => systemIsLight ? ThemeMode.Light : ThemeMode.Dark,
        _ => ThemeMode.Light,
    };

    public static string NormalizePreset(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
            return "default";

        var normalized = preset.Trim().ToLowerInvariant();
        return Array.IndexOf(Presets, normalized) >= 0 ? normalized : "default";
    }

    public static Uri ResolvePaletteUri(ThemeMode mode, string? preset, bool systemIsLight)
    {
        var effective = ResolveEffectiveMode(mode, systemIsLight);
        var normalized = NormalizePreset(preset);
        var presetName = char.ToUpperInvariant(normalized[0]) + normalized[1..];
        var variant = effective == ThemeMode.Light ? "Light" : "Dark";
        return new Uri($"Themes/{presetName}.{variant}.xaml", UriKind.Relative);
    }
}

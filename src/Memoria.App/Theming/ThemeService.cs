// src/Memoria.App/Theming/ThemeService.cs
using System;
using Memoria.Core;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.App.Theming;

public sealed class ThemeService : IThemeService, IDisposable
{
    private readonly ISettingsRepository _settings;
    private readonly IThemeApplier _applier;
    private readonly ISystemThemeSource _systemTheme;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;
    public string Preset { get; private set; } = "default";
    public string Accent { get; private set; } = AccentColor.Default;

    public event EventHandler? ThemeChanged;

    public ThemeService(ISettingsRepository settings, IThemeApplier applier, ISystemThemeSource systemTheme)
    {
        _settings = settings;
        _applier = applier;
        _systemTheme = systemTheme;
        _systemTheme.SystemThemeChanged += OnSystemThemeChanged;
    }

    public void Initialize()
    {
        var mode = ParseMode(_settings.GetOrDefault(SettingsKeys.ThemeMode, "system"));
        var preset = ThemeResolver.NormalizePreset(_settings.GetOrDefault(SettingsKeys.ThemePreset, "default"));
        var accent = AccentColor.Normalize(_settings.GetOrDefault(SettingsKeys.ThemeAccent, AccentColor.Default));
        ApplyInternal(mode, preset, accent, persist: false);
    }

    public void Apply(ThemeMode mode, string preset, string accent)
        => ApplyInternal(mode, ThemeResolver.NormalizePreset(preset), AccentColor.Normalize(accent), persist: true);

    private void ApplyInternal(ThemeMode mode, string preset, string accent, bool persist)
    {
        Mode = mode;
        Preset = preset;
        Accent = accent;

        var uri = ThemeResolver.ResolvePaletteUri(mode, preset, _systemTheme.IsLight());
        _applier.ApplyPalette(uri);
        _applier.ApplyAccent(accent);

        if (persist)
        {
            _settings.Set(SettingsKeys.ThemeMode, ModeToString(mode));
            _settings.Set(SettingsKeys.ThemePreset, preset);
            _settings.Set(SettingsKeys.ThemeAccent, accent);
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        if (Mode == ThemeMode.System)
            ApplyInternal(Mode, Preset, Accent, persist: false);
    }

    public static ThemeMode ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => ThemeMode.Light,
        "dark" => ThemeMode.Dark,
        _ => ThemeMode.System,
    };

    public static string ModeToString(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "light",
        ThemeMode.Dark => "dark",
        _ => "system",
    };

    public void Dispose() => _systemTheme.SystemThemeChanged -= OnSystemThemeChanged;
}

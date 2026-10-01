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
    private Uri? _appliedPalette;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;
    public string Preset { get; private set; } = "default";

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
        ApplyInternal(mode, preset, persist: false);
    }

    // 강조색은 각 팔레트(색 계열 × 모드)가 직접 정의한다(Brush.Accent). 별도 사용자 오버라이드 없음.
    public void Apply(ThemeMode mode, string preset)
        => ApplyInternal(mode, ThemeResolver.NormalizePreset(preset), persist: true);

    private void ApplyInternal(ThemeMode mode, string preset, bool persist)
    {
        Mode = mode;
        Preset = preset;

        var uri = ThemeResolver.ResolvePaletteUri(mode, preset, _systemTheme.IsLight());
        if (!Equals(_appliedPalette, uri))
        {
            _applier.ApplyPalette(uri);
            _appliedPalette = uri;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        if (persist)
        {
            _settings.SetMany(new Dictionary<string, string>
            {
                [SettingsKeys.ThemeMode] = ModeToString(mode),
                [SettingsKeys.ThemePreset] = preset,
            });
        }

    }

    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        if (Mode == ThemeMode.System)
            ApplyInternal(Mode, Preset, persist: false);
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

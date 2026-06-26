// src/Memoria.App/Theming/SystemEventsThemeSource.cs
using System;
using Microsoft.Win32;

namespace Memoria.App.Theming;

public sealed class SystemEventsThemeSource : ISystemThemeSource
{
    private bool _disposed;

    public event EventHandler? SystemThemeChanged;

    public SystemEventsThemeSource()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsLight() => SystemThemeReader.ReadIsLight();

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // General 범주에 테마(색) 변경이 포함된다.
        if (e.Category == UserPreferenceCategory.General)
            SystemThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _disposed = true;
    }
}

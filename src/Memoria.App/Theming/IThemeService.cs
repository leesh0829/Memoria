// src/Memoria.App/Theming/IThemeService.cs
using System;
using Memoria.Core.Models;

namespace Memoria.App.Theming;

public interface IThemeService
{
    ThemeMode Mode { get; }
    string Preset { get; }
    string Accent { get; }
    void Initialize();
    void Apply(ThemeMode mode, string preset, string accent);
    event EventHandler? ThemeChanged;
}

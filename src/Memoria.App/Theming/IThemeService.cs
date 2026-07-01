// src/Memoria.App/Theming/IThemeService.cs
using System;
using Memoria.Core.Models;

namespace Memoria.App.Theming;

public interface IThemeService
{
    ThemeMode Mode { get; }
    string Preset { get; }
    void Initialize();
    void Apply(ThemeMode mode, string preset);
    event EventHandler? ThemeChanged;
}

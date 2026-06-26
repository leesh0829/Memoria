// src/Memoria.App/Theming/ISystemThemeSource.cs
using System;

namespace Memoria.App.Theming;

public interface ISystemThemeSource : IDisposable
{
    bool IsLight();
    event EventHandler? SystemThemeChanged;
}

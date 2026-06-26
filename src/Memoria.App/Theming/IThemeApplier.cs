// src/Memoria.App/Theming/IThemeApplier.cs
using System;

namespace Memoria.App.Theming;

public interface IThemeApplier
{
    void ApplyPalette(Uri paletteUri);
    void ApplyAccent(string accentHex);
}

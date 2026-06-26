// tests/Memoria.Tests/Theming/FakeThemeCollaborators.cs
using System;
using Memoria.App.Theming;

namespace Memoria.Tests.Theming;

public sealed class FakeThemeApplier : IThemeApplier
{
    public Uri? LastPalette { get; private set; }
    public string? LastAccent { get; set; }
    public int PaletteApplyCount { get; private set; }

    public void ApplyPalette(Uri paletteUri)
    {
        LastPalette = paletteUri;
        PaletteApplyCount++;
    }

    public void ApplyAccent(string accentHex) => LastAccent = accentHex;
}

public sealed class FakeSystemThemeSource : ISystemThemeSource
{
    public bool Light { get; set; } = true;
    public event EventHandler? SystemThemeChanged;

    public bool IsLight() => Light;
    public void RaiseChanged() => SystemThemeChanged?.Invoke(this, EventArgs.Empty);
    public void Dispose() { }
}

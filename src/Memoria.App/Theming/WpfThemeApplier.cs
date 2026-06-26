// src/Memoria.App/Theming/WpfThemeApplier.cs
using System;
using System.Windows;
using System.Windows.Media;

namespace Memoria.App.Theming;

public sealed class WpfThemeApplier : IThemeApplier
{
    // App.xaml 규약: MergedDictionaries[0]=Base, [1]=팔레트 슬롯.
    private const int PaletteSlotIndex = 1;

    public void ApplyPalette(Uri paletteUri)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = paletteUri };

        if (dictionaries.Count > PaletteSlotIndex)
            dictionaries[PaletteSlotIndex] = palette; // 1개만 교체 → 깜빡임 최소화
        else
            dictionaries.Add(palette);
    }

    public void ApplyAccent(string accentHex)
    {
        var color = (Color)ColorConverter.ConvertFromString(AccentColor.Normalize(accentHex));
        var brush = new SolidColorBrush(color);
        brush.Freeze();   // 불변 브러시 → 크로스 스레드 안전 + 렌더 최적화
        Application.Current.Resources["Brush.Accent"] = brush;
    }
}

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Memoria.App.Converters;

/// "#RRGGBB" 문자열 → SolidColorBrush (테마 스와치 표시용).
public sealed class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                // 잘못된 색 문자열은 투명 처리.
            }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

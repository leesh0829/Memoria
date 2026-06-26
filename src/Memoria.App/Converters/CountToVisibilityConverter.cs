using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Memoria.App.Converters;

/// int > 0 → Visible, else Collapsed (검색 결과 패널 표시용).
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

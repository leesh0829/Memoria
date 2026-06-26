// src/Memoria.App/Converters/DateOnlyConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace Memoria.App.Converters;

public sealed class DateOnlyConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DateOnly d ? (DateTime?)d.ToDateTime(TimeOnly.MinValue) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DateTime dt ? DateOnly.FromDateTime(dt) : DateOnly.MinValue;
}

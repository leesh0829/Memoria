// src/Memoria.App/Converters/DateOnlyToDateTimeConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace Memoria.App.Converters;

/// DateOnly ↔ DateTime? 변환 (WPF DatePicker 바인딩용).
[ValueConversion(typeof(DateOnly), typeof(DateTime?))]
public sealed class DateOnlyToDateTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateOnly d)
            return d.ToDateTime(TimeOnly.MinValue);
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return DateOnly.FromDateTime(dt);
        return DateOnly.FromDateTime(DateTime.Today);
    }
}

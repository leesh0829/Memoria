// src/Memoria.App/Converters/EnumMatchConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace Memoria.App.Converters;

public sealed class EnumMatchConverter : IValueConverter
{
    public static readonly EnumMatchConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null && value.Equals(parameter);

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;
}

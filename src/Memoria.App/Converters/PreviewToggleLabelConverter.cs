using System;
using System.Globalization;
using System.Windows.Data;

namespace Memoria.App.Converters;

// IsPreviewMode == true → "편집"(누르면 편집으로), false → "미리보기".
public sealed class PreviewToggleLabelConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? "✎ 편집" : "👁 미리보기";
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

using System.Globalization;
using FluentAssertions;
using Memoria.App.Converters;
using Xunit;

namespace Memoria.Tests.Converters;

public class DateOnlyConverterTests
{
    [Fact]
    public void Convert_dateonly_to_datetime()
    {
        var c = new DateOnlyConverter();
        var result = c.Convert(new DateOnly(2026, 6, 22), typeof(DateTime?), null!, CultureInfo.InvariantCulture);
        result.Should().Be(new DateTime(2026, 6, 22));
    }

    [Fact]
    public void ConvertBack_datetime_to_dateonly()
    {
        var c = new DateOnlyConverter();
        var result = c.ConvertBack(new DateTime(2026, 6, 22, 10, 30, 0), typeof(DateOnly), null!, CultureInfo.InvariantCulture);
        result.Should().Be(new DateOnly(2026, 6, 22));
    }

    [Fact]
    public void ConvertBack_null_keeps_dateonly_minvalue()
    {
        var c = new DateOnlyConverter();
        var result = c.ConvertBack(null!, typeof(DateOnly), null!, CultureInfo.InvariantCulture);
        result.Should().Be(DateOnly.MinValue);
    }
}

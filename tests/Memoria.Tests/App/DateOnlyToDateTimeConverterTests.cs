using System;
using System.Globalization;
using System.Windows.Data;
using FluentAssertions;
using Memoria.App.Converters;
using Xunit;

namespace Memoria.Tests.App;

public class DateOnlyToDateTimeConverterTests
{
    private readonly DateOnlyToDateTimeConverter _sut = new();

    [Fact]
    public void ConvertBack_datetime_returns_dateonly()
    {
        var result = _sut.ConvertBack(new DateTime(2026, 7, 6), typeof(DateOnly), null, CultureInfo.InvariantCulture);
        result.Should().Be(new DateOnly(2026, 7, 6));
    }

    [Fact]
    public void ConvertBack_null_returns_binding_donothing()
    {
        var result = _sut.ConvertBack(null, typeof(DateOnly), null, CultureInfo.InvariantCulture);
        result.Should().BeSameAs(Binding.DoNothing);   // 비우기 = no-op(오늘로 안 튐)
    }

    [Fact]
    public void Convert_dateonly_returns_datetime()
    {
        var result = _sut.Convert(new DateOnly(2026, 7, 6), typeof(DateTime?), null, CultureInfo.InvariantCulture);
        result.Should().Be(new DateTime(2026, 7, 6));
    }
}

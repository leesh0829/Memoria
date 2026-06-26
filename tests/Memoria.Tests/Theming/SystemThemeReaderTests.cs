// tests/Memoria.Tests/Theming/SystemThemeReaderTests.cs
using FluentAssertions;
using Memoria.App.Theming;
using Xunit;

namespace Memoria.Tests.Theming;

public class SystemThemeReaderTests
{
    [Theory]
    [InlineData(1, true)]    // AppsUseLightTheme=1 → light
    [InlineData(0, false)]   // 0 → dark
    public void ParseAppsUseLightTheme_maps_int_value(int value, bool expected)
    {
        SystemThemeReader.ParseAppsUseLightTheme(value).Should().Be(expected);
    }

    [Fact]
    public void ParseAppsUseLightTheme_handles_long_value()
    {
        SystemThemeReader.ParseAppsUseLightTheme(0L).Should().BeFalse();
        SystemThemeReader.ParseAppsUseLightTheme(1L).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParseAppsUseLightTheme_uses_fallback_when_value_missing(bool fallback)
    {
        SystemThemeReader.ParseAppsUseLightTheme(null, fallbackIsLight: fallback).Should().Be(fallback);
    }

    [Fact]
    public void Constants_are_canonical()
    {
        SystemThemeReader.PersonalizeKeyPath.Should()
            .Be(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        SystemThemeReader.AppsUseLightThemeValue.Should().Be("AppsUseLightTheme");
    }

    [Fact]
    public void ReadIsLight_does_not_throw_on_windows()
    {
        // 실제 HKCU를 읽되 결과 값은 환경에 따라 다르므로 예외 없음만 검증.
        var act = () => SystemThemeReader.ReadIsLight();
        act.Should().NotThrow();
    }
}

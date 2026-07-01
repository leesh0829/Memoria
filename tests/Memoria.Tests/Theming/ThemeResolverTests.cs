// tests/Memoria.Tests/Theming/ThemeResolverTests.cs
using System;
using FluentAssertions;
using Memoria.App.Theming;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Theming;

public class ThemeResolverTests
{
    [Theory]
    [InlineData(ThemeMode.Light, true, ThemeMode.Light)]
    [InlineData(ThemeMode.Light, false, ThemeMode.Light)]
    [InlineData(ThemeMode.Dark, true, ThemeMode.Dark)]
    [InlineData(ThemeMode.Dark, false, ThemeMode.Dark)]
    [InlineData(ThemeMode.System, true, ThemeMode.Light)]
    [InlineData(ThemeMode.System, false, ThemeMode.Dark)]
    public void ResolveEffectiveMode_follows_system_only_when_mode_is_system(
        ThemeMode mode, bool systemIsLight, ThemeMode expected)
    {
        ThemeResolver.ResolveEffectiveMode(mode, systemIsLight).Should().Be(expected);
    }

    [Theory]
    [InlineData("default", "default")]
    [InlineData("BLUE", "blue")]
    [InlineData(" Green ", "green")]
    [InlineData("purple", "purple")]
    [InlineData(null, "default")]
    [InlineData("", "default")]
    [InlineData("neon", "default")]    // 알 수 없는 프리셋 → default 폴백
    [InlineData("sepia", "default")]   // 더 이상 지원하지 않는 프리셋 → default 폴백
    public void NormalizePreset_lowercases_trims_and_falls_back(string? input, string expected)
    {
        ThemeResolver.NormalizePreset(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(ThemeMode.Light, "default", true, "Themes/Default.Light.xaml")]
    [InlineData(ThemeMode.Dark, "default", true, "Themes/Default.Dark.xaml")]
    [InlineData(ThemeMode.System, "blue", false, "Themes/Blue.Dark.xaml")]
    [InlineData(ThemeMode.System, "green", true, "Themes/Green.Light.xaml")]
    [InlineData(ThemeMode.Light, "purple", true, "Themes/Purple.Light.xaml")]
    [InlineData(ThemeMode.Dark, "neon", true, "Themes/Default.Dark.xaml")] // 미지의 프리셋 폴백
    public void ResolvePaletteUri_combines_effective_mode_and_preset(
        ThemeMode mode, string preset, bool systemIsLight, string expected)
    {
        var uri = ThemeResolver.ResolvePaletteUri(mode, preset, systemIsLight);
        uri.IsAbsoluteUri.Should().BeFalse();
        uri.OriginalString.Should().Be(expected);
    }

    [Fact]
    public void Presets_list_is_the_supported_color_families()
    {
        ThemeResolver.Presets.Should().BeEquivalentTo(
            new[] { "default", "blue", "teal", "green", "yellow", "orange", "red", "pink", "purple" });
    }
}

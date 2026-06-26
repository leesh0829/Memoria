using FluentAssertions;
using Memoria.App.Windows;
using Xunit;

namespace Memoria.Tests.Windows;

public class HotkeyParserTests
{
    [Fact]
    public void Parses_default_ctrl_alt_n()
    {
        HotkeyParser.TryParse("Ctrl+Alt+N", out var hk).Should().BeTrue();
        hk.Modifiers.Should().Be(HotkeyModifiers.Control | HotkeyModifiers.Alt);
        hk.VirtualKey.Should().Be(0x4Eu); // VK_N == 'N'
    }

    [Fact]
    public void Is_case_insensitive_and_trims_spaces()
    {
        HotkeyParser.TryParse(" ctrl + ALT + n ", out var hk).Should().BeTrue();
        hk.Modifiers.Should().Be(HotkeyModifiers.Control | HotkeyModifiers.Alt);
        hk.VirtualKey.Should().Be(0x4Eu);
    }

    [Fact]
    public void Parses_function_key_with_shift()
    {
        HotkeyParser.TryParse("Ctrl+Shift+F5", out var hk).Should().BeTrue();
        hk.Modifiers.Should().Be(HotkeyModifiers.Control | HotkeyModifiers.Shift);
        hk.VirtualKey.Should().Be(0x74u); // VK_F5 == 0x70 + 4
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("N")]            // 수정자 없음
    [InlineData("Ctrl+Alt")]    // 키 없음
    [InlineData("Ctrl+A+B")]    // 키 두 개
    [InlineData("Ctrl+Alt+NN")] // 알 수 없는 키
    [InlineData("Ctrl+Alt+F25")]// 범위 밖 F키
    public void Rejects_invalid_input(string? input)
    {
        HotkeyParser.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void ModNoRepeat_constant_is_0x4000()
    {
        HotkeyParser.ModNoRepeat.Should().Be(0x4000u);
    }
}

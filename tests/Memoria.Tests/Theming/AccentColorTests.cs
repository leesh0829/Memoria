// tests/Memoria.Tests/Theming/AccentColorTests.cs
using FluentAssertions;
using Memoria.App.Theming;
using Xunit;

namespace Memoria.Tests.Theming;

public class AccentColorTests
{
    [Theory]
    [InlineData("#0078D4", true)]
    [InlineData("0078D4", true)]   // # 생략 허용
    [InlineData("#abcdef", true)]
    [InlineData("#ABC", false)]    // 3자리 단축 미지원
    [InlineData("#12345G", false)] // 비-16진
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_accepts_six_digit_hex(string? input, bool expected)
    {
        AccentColor.IsValid(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("0078d4", "#0078D4")]
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData("garbage", "#0078D4")] // 무효 → 기본값
    [InlineData(null, "#0078D4")]
    public void Normalize_returns_uppercase_hash_prefixed_or_default(string? input, string expected)
    {
        AccentColor.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Default_is_windows_blue()
    {
        AccentColor.Default.Should().Be("#0078D4");
    }
}

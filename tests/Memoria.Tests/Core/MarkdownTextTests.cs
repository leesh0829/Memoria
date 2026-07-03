using FluentAssertions;
using Memoria.Core.Text;
using Xunit;

namespace Memoria.Tests.Core;

public class MarkdownTextTests
{
    [Theory]
    [InlineData("# 제목", "제목")]
    [InlineData("###   여백 제목  ", "여백 제목")]
    [InlineData("- 항목", "항목")]
    [InlineData("* 항목", "항목")]
    [InlineData("1. 첫째", "첫째")]
    [InlineData("> 인용", "인용")]
    [InlineData("**굵게** 텍스트", "굵게 텍스트")]
    [InlineData("`code`", "code")]
    [InlineData("일반 텍스트", "일반 텍스트")]
    public void StripMarkers_RemovesLeadingBlockAndInlineMarks(string input, string expected)
        => MarkdownText.StripMarkers(input).Should().Be(expected);
}

using FluentAssertions;
using Memoria.Core.Text;
using Xunit;

namespace Memoria.Tests.Core;

public class MarkdownReadSegmenterTests
{
    [Fact]
    public void Segment_SplitsTextAndImages_InOrder()
    {
        var segs = MarkdownReadSegmenter.Segment("hello ![a](x.png) world");
        segs.Should().HaveCount(3);
        segs[0].Should().Be(new ReadSegment(false, "hello "));
        segs[1].Should().Be(new ReadSegment(true, "x.png"));
        segs[2].Should().Be(new ReadSegment(false, " world"));
    }

    [Fact]
    public void Segment_NoImages_SingleTextSegment_PreservesMarkdownSyntax()
    {
        var segs = MarkdownReadSegmenter.Segment("# 제목\n**굵게**");
        segs.Should().ContainSingle();
        segs[0].Should().Be(new ReadSegment(false, "# 제목\n**굵게**"));
    }

    [Fact]
    public void Segment_ConsecutiveImages_NoTextBetween()
    {
        var segs = MarkdownReadSegmenter.Segment("![](a.png)![](b.png)");
        segs.Should().Equal(new ReadSegment(true, "a.png"), new ReadSegment(true, "b.png"));
    }

    [Fact]
    public void Segment_IgnoresAltText_ExtractsPathTrimmed()
    {
        var segs = MarkdownReadSegmenter.Segment("![some alt]( p.png )");
        segs.Should().ContainSingle().Which.Should().Be(new ReadSegment(true, "p.png"));
    }

    [Fact]
    public void Segment_EmptyOrNull_ReturnsEmpty()
    {
        MarkdownReadSegmenter.Segment("").Should().BeEmpty();
        MarkdownReadSegmenter.Segment(null).Should().BeEmpty();
    }
}

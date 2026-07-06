using FluentAssertions;
using Memoria.Core.Sheets;
using Xunit;

namespace Memoria.Tests.Sheets;

public class SheetA1Tests
{
    [Theory]
    [InlineData("일자 작업내역", "A:C", "'일자 작업내역'!A:C")]
    [InlineData("Sheet1", "A:C", "'Sheet1'!A:C")]
    [InlineData("a'b", "A:C", "'a''b'!A:C")]
    public void Range_QuotesAndEscapesTabName(string tab, string cols, string expected)
        => SheetA1.Range(tab, cols).Should().Be(expected);
}

using FluentAssertions;
using Memoria.Core.Sheets;
using Xunit;

namespace Memoria.Tests.Sheets;

public class SheetWorkParserTests
{
    // 격자 헬퍼: 각 행은 셀 문자열 목록.
    private static IReadOnlyList<IReadOnlyList<string>> Grid(params string[][] rows)
        => rows.Select(r => (IReadOnlyList<string>)r.ToList()).ToList();

    private static readonly DateOnly Mon = new(2025, 9, 22);
    private static readonly DateOnly Fri = new(2025, 9, 26);

    [Fact]
    public void Parse_SkipsHeader_ExtractsTasksAndIssues_StripsNumbering()
    {
        var grid = Grid(
            new[] { "일자", "작업내역", "특이사항" },
            new[] { "2025.09.22 (월)", "1. SLD 점검\n2. MTP 정리", "1. 장비 오류\n2, 재확인" });

        var r = SheetWorkParser.Parse(grid, Mon, Fri);

        r.Tasks.Should().Equal("SLD 점검", "MTP 정리");
        r.Issues.Should().Equal("장비 오류", "재확인");
    }

    [Fact]
    public void Parse_FiltersRowsOutsideWeek()
    {
        var grid = Grid(
            new[] { "일자", "작업내역", "특이사항" },
            new[] { "2025.09.19 (금)", "이전주 업무", "" },   // 주 밖
            new[] { "2025.09.23 (화)", "이번주 업무", "" },
            new[] { "2025.09.29 (월)", "다음주 업무", "" });   // 주 밖

        var r = SheetWorkParser.Parse(grid, Mon, Fri);

        r.Tasks.Should().Equal("이번주 업무");
    }

    [Fact]
    public void Parse_SkipsRowsWithUnparseableOrEmptyDate()
    {
        var grid = Grid(
            new[] { "일자", "작업내역", "특이사항" },
            new[] { "", "빈 날짜", "" },
            new[] { "메모", "잘못된 날짜", "" },
            new[] { "2025.09.24 (수)", "정상", "" });

        var r = SheetWorkParser.Parse(grid, Mon, Fri);

        r.Tasks.Should().Equal("정상");
    }

    [Fact]
    public void Parse_EmptyIssueCell_YieldsNoIssues_AndRaggedRowIsSafe()
    {
        var grid = Grid(
            new[] { "일자", "작업내역", "특이사항" },
            new[] { "2025.09.24 (수)", "업무만" });   // C열 없음(래그드)

        var r = SheetWorkParser.Parse(grid, Mon, Fri);

        r.Tasks.Should().Equal("업무만");
        r.Issues.Should().BeEmpty();
    }

    [Theory]
    [InlineData("2025.09.22 (월)", true, 2025, 9, 22)]
    [InlineData("2025.9.2", true, 2025, 9, 2)]
    [InlineData("  2025.12.31 (수) ", true, 2025, 12, 31)]
    [InlineData("2025.13.01", false, 0, 0, 0)]
    [InlineData("메모", false, 0, 0, 0)]
    public void TryParseDate_ParsesLeadingYmd(string cell, bool ok, int y, int m, int d)
    {
        SheetWorkParser.TryParseDate(cell, out var date).Should().Be(ok);
        if (ok) date.Should().Be(new DateOnly(y, m, d));
    }
}

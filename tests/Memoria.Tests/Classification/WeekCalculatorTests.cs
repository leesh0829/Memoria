using FluentAssertions;
using Memoria.Core.Classification;
using Xunit;

namespace Memoria.Tests.Classification;

public class WeekCalculatorTests
{
    private readonly IWeekCalculator _calc = new WeekCalculator();

    [Fact]
    public void Friday_ReturnsSameWeekMondayToFriday()
    {
        // 2026-06-26 은 금요일
        var (monday, friday) = _calc.GetWorkWeek(new DateOnly(2026, 6, 26));
        monday.Should().Be(new DateOnly(2026, 6, 22));
        friday.Should().Be(new DateOnly(2026, 6, 26));
    }

    [Fact]
    public void Monday_ReturnsItselfAsMonday()
    {
        var (monday, friday) = _calc.GetWorkWeek(new DateOnly(2026, 6, 22));
        monday.Should().Be(new DateOnly(2026, 6, 22));
        friday.Should().Be(new DateOnly(2026, 6, 26));
    }

    [Fact]
    public void Sunday_BelongsToWeekStartedPreviousMonday()
    {
        // 2026-06-28 은 일요일 → 그 주는 06-22(월)~06-26(금)
        var (monday, friday) = _calc.GetWorkWeek(new DateOnly(2026, 6, 28));
        monday.Should().Be(new DateOnly(2026, 6, 22));
        friday.Should().Be(new DateOnly(2026, 6, 26));
    }

    [Fact]
    public void YearBoundary_WeekSpansNewYear()
    {
        // 2026-12-31 은 목요일 → 월 2026-12-28, 금 2027-01-01
        var (monday, friday) = _calc.GetWorkWeek(new DateOnly(2026, 12, 31));
        monday.Should().Be(new DateOnly(2026, 12, 28));
        friday.Should().Be(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void CustomRange_FridayToThursday_SpansPreviousFridayToThisThursday()
    {
        // 2026-08-12(수)가 속한 주 = 08-10(월)~ → 목요일 08-13, 그 직전 금요일 08-07
        var (start, end) = _calc.GetCustomRange(
            new DateOnly(2026, 8, 12), DayOfWeek.Friday, DayOfWeek.Thursday);
        start.Should().Be(new DateOnly(2026, 8, 7));
        end.Should().Be(new DateOnly(2026, 8, 13));
    }

    [Fact]
    public void CustomRange_MondayToFriday_MatchesWorkWeek()
    {
        var (start, end) = _calc.GetCustomRange(
            new DateOnly(2026, 8, 12), DayOfWeek.Monday, DayOfWeek.Friday);
        start.Should().Be(new DateOnly(2026, 8, 10));
        end.Should().Be(new DateOnly(2026, 8, 14));
    }

    [Fact]
    public void CustomRange_SameStartAndEndDay_SpansSevenDays()
    {
        var (start, end) = _calc.GetCustomRange(
            new DateOnly(2026, 8, 12), DayOfWeek.Wednesday, DayOfWeek.Wednesday);
        start.Should().Be(new DateOnly(2026, 8, 5));
        end.Should().Be(new DateOnly(2026, 8, 12));
    }

    [Fact]
    public void CustomRange_Sunday_UsesWeekStartedPreviousMonday()
    {
        // 2026-08-16(일)은 08-10(월) 주에 속한다 → 목요일 08-13 기준
        var (start, end) = _calc.GetCustomRange(
            new DateOnly(2026, 8, 16), DayOfWeek.Friday, DayOfWeek.Thursday);
        start.Should().Be(new DateOnly(2026, 8, 7));
        end.Should().Be(new DateOnly(2026, 8, 13));
    }

    [Fact]
    public void CustomRange_SundayEndDay_ResolvesToWeekEndingSunday()
    {
        // 일요일은 월요일 기준 주의 마지막 날 → 08-10(월) 주의 일요일은 08-16
        var (start, end) = _calc.GetCustomRange(
            new DateOnly(2026, 8, 12), DayOfWeek.Monday, DayOfWeek.Sunday);
        start.Should().Be(new DateOnly(2026, 8, 10));
        end.Should().Be(new DateOnly(2026, 8, 16));
    }
}

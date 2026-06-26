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
}

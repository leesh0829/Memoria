namespace Memoria.Core.Classification;

public sealed class WeekCalculator : IWeekCalculator
{
    public (DateOnly Monday, DateOnly Friday) GetWorkWeek(DateOnly anyDate)
    {
        // DayOfWeek: Sunday=0 .. Saturday=6. 월요일 기준 경과일 = ((int)dow + 6) % 7.
        int daysSinceMonday = ((int)anyDate.DayOfWeek + 6) % 7;
        DateOnly monday = anyDate.AddDays(-daysSinceMonday);
        DateOnly friday = monday.AddDays(4);
        return (monday, friday);
    }

    public (DateOnly Start, DateOnly End) GetCustomRange(DateOnly anyDate, DayOfWeek startDay, DayOfWeek endDay)
    {
        var (monday, _) = GetWorkWeek(anyDate);
        DateOnly end = monday.AddDays(((int)endDay + 6) % 7);   // 월=0 … 일=6

        int back = ((int)endDay - (int)startDay + 7) % 7;
        if (back == 0) back = 7;                                // 같은 요일이면 한 주 전
        return (end.AddDays(-back), end);
    }
}

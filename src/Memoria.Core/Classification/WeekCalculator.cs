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
}

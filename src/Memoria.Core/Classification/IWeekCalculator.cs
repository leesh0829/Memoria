namespace Memoria.Core.Classification;

public interface IWeekCalculator
{
    /// 임의 날짜가 속한 주의 (월요일, 금요일) 반환.
    (DateOnly Monday, DateOnly Friday) GetWorkWeek(DateOnly anyDate);
}

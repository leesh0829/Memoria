namespace Memoria.Core.Classification;

public interface IWeekCalculator
{
    /// 임의 날짜가 속한 주의 (월요일, 금요일) 반환.
    (DateOnly Monday, DateOnly Friday) GetWorkWeek(DateOnly anyDate);

    /// 임의 날짜가 속한 주(월요일 기준)의 endDay를 종료일로 삼고, 거기서 거슬러 올라가
    /// 처음 만나는 startDay를 시작일로 하는 구간을 반환. 두 요일이 같으면 7일 구간.
    /// (예: 금~목 → 전주 금요일 ~ 해당 주 목요일)
    (DateOnly Start, DateOnly End) GetCustomRange(DateOnly anyDate, DayOfWeek startDay, DayOfWeek endDay);
}

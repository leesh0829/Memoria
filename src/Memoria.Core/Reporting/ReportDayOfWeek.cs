namespace Memoria.Core.Reporting;

/// 설정에 DayOfWeek 이름("Friday")으로 저장된 요일을 읽는다. 값이 없거나 깨졌으면 기본값으로 폴백.
public static class ReportDayOfWeek
{
    public static DayOfWeek Parse(string? value, DayOfWeek fallback)
        => Enum.TryParse<DayOfWeek>(value, ignoreCase: true, out var day) && Enum.IsDefined(day)
            ? day
            : fallback;
}

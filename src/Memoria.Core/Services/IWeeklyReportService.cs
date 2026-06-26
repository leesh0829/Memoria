using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.Core.Services;

public sealed record WeeklyReportBuildResult(
    WeeklyReportData Data,
    int UnclassifiedTaskCount,
    DateOnly Monday,
    DateOnly Friday);

public interface IWeeklyReportService
{
    /// 주간 데이터 수집 + auto 항목 재분류 + 미분류 카운트.
    WeeklyReportBuildResult Build(DateOnly anyDateInWeek, ReportRenderOptions options);
    /// 렌더(IWeeklyReportRenderer 위임).
    string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options);
}

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
    /// 시트 등 텍스트 목록에서 빌드(자동 분류, Done=true). 체크리스트 경로와 병존.
    WeeklyReportBuildResult BuildFromTexts(
        IReadOnlyList<string> taskTexts, IReadOnlyList<string> issueTexts,
        DateOnly monday, DateOnly friday, ReportRenderOptions options);
}

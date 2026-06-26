using Memoria.Core.Models;

namespace Memoria.Core.Reporting;

public interface IWeeklyReportRenderer
{
    /// <summary>양식 A 또는 B의 최종 텍스트를 반환.</summary>
    string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options);
}

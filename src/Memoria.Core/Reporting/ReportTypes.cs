using Memoria.Core.Models;

namespace Memoria.Core.Reporting;

public sealed record ReportTask(string Text, int? ClientId, bool Done);
public sealed record ReportIssue(string Text);

public sealed record WeeklyReportData(
    IReadOnlyList<ReportTask> Tasks,
    IReadOnlyList<ReportIssue> Issues);

public sealed record ReportRenderOptions
{
    public string ReporterName { get; init; } = "이승현";
    public DateOnly WeekStart { get; init; }
    public DateOnly WeekEnd { get; init; }
    public string TaskHeaderA { get; init; } = "[업무 내용]";
    public string IssueHeaderA { get; init; } = "[이슈]";
    public string TitleWordB { get; init; } = "주간 보고";
    public string IssueHeaderB { get; init; } = "* 이슈사항:";
    public string TitleHeaderC { get; init; } = "[ 주간 실적 ]";
    public string PlanHeaderC { get; init; } = "[ 차주 계획 ]";
    public string DetailMarkerC { get; init; } = "o";
    /// 양식 C에 포함할 고객사. null = 필터 없음(전부 포함), 빈 목록 = 해당 업무 없음.
    public IReadOnlyList<int>? ClientIdsC { get; init; }
    public string Indent { get; init; } = "\t";
    public bool IncludeDoneOnly { get; init; } = false;
    public IReadOnlyList<Client> Clients { get; init; } = new List<Client>();
    public string UnclassifiedLabel { get; init; } = "미분류";
}

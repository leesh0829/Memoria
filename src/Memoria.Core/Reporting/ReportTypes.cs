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
    public string Indent { get; init; } = "\t";
    public bool IncludeDoneOnly { get; init; } = false;
    public IReadOnlyList<Client> Clients { get; init; } = new List<Client>();
    public string UnclassifiedLabel { get; init; } = "미분류";
}

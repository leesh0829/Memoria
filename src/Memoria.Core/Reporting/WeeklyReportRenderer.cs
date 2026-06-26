using Memoria.Core.Models;

namespace Memoria.Core.Reporting;

public sealed class WeeklyReportRenderer : IWeeklyReportRenderer
{
    public string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options)
        => format switch
        {
            ReportFormatKind.A => RenderA(data, options),
            ReportFormatKind.B => RenderB(data, options),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    private static IEnumerable<ReportTask> VisibleTasks(WeeklyReportData data, ReportRenderOptions options)
        => options.IncludeDoneOnly ? data.Tasks.Where(t => t.Done) : data.Tasks;

    private static string RenderA(WeeklyReportData data, ReportRenderOptions options)
    {
        var lines = new List<string> { options.TaskHeaderA };
        foreach (var t in VisibleTasks(data, options))
            lines.Add(options.Indent + "* " + t.Text);
        lines.Add("");
        lines.Add(options.IssueHeaderA);
        foreach (var i in data.Issues)
            lines.Add(options.Indent + "* " + i.Text);
        return string.Join("\n", lines);
    }

    private static string RenderB(WeeklyReportData data, ReportRenderOptions options)
        => throw new NotImplementedException("양식 B는 Task 5에서 구현");
}

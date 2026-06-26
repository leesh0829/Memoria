using System.Globalization;
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
    {
        string start = options.WeekStart.ToString("MM/dd", CultureInfo.InvariantCulture);
        string end = options.WeekEnd.ToString("MM/dd", CultureInfo.InvariantCulture);

        var lines = new List<string>
        {
            $"[ {options.ReporterName} {options.TitleWordB} ({start} ~ {end}) ]:",
            "",
        };

        var tasks = VisibleTasks(data, options).ToList();

        foreach (var client in options.Clients)
        {
            lines.Add($"[ {client.Name} ]");
            foreach (var t in tasks.Where(t => t.ClientId == client.Id))
                lines.Add(options.Indent + "* " + t.Text);
            lines.Add("");
        }

        var unclassified = tasks.Where(t => t.ClientId is null).ToList();
        if (unclassified.Count > 0)
        {
            lines.Add($"[ {options.UnclassifiedLabel} ]");
            foreach (var t in unclassified)
                lines.Add(options.Indent + "* " + t.Text);
            lines.Add("");
        }

        lines.Add(options.IssueHeaderB);
        foreach (var i in data.Issues)
            lines.Add(options.Indent + "* " + i.Text);

        return string.Join("\n", lines);
    }
}

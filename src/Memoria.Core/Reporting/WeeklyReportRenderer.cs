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
            ReportFormatKind.C => RenderC(data, options),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    /// 양식 C의 설명 머릿말 줄 들여쓰기. 붙여넣는 문서마다 탭 폭이 달라 정렬이 깨지므로
    /// 공통 report.indent(기본 탭) 대신 공백 4칸으로 고정한다.
    private const string FormatCIndent = "    ";

    private static IEnumerable<ReportTask> VisibleTasks(WeeklyReportData data, ReportRenderOptions options)
        => options.IncludeDoneOnly ? data.Tasks.Where(t => t.Done) : data.Tasks;

    private static string RenderA(WeeklyReportData data, ReportRenderOptions options)
    {
        var clientNames = options.Clients.ToDictionary(c => c.Id, c => c.Name);
        var lines = new List<string> { options.TaskHeaderA };
        foreach (var t in VisibleTasks(data, options))
            lines.Add(options.Indent + "* " + WithClientPrefix(t, clientNames));
        lines.Add("");
        lines.Add(options.IssueHeaderA);
        foreach (var i in data.Issues)
            lines.Add(options.Indent + "* " + i.Text);
        return string.Join("\n", lines);
    }

    /// 업무 텍스트 앞에 선택한 고객사명을 붙인다. 텍스트가 이미 고객사명으로 시작하면(구글시트 경로처럼
    /// 셀에 회사명이 포함된 경우) 중복을 피하려고 그대로 둔다.
    private static string WithClientPrefix(ReportTask t, IReadOnlyDictionary<int, string> clientNames)
    {
        if (t.ClientId is int id
            && clientNames.TryGetValue(id, out var name)
            && !string.IsNullOrWhiteSpace(name)
            && !t.Text.StartsWith(name, StringComparison.Ordinal))
        {
            return name + " " + t.Text;
        }
        return t.Text;
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

    /// 양식 C: 보고자 이름 아래로 업무 한 건마다 "- 업무" + 빈 설명 머릿말("o") 줄.
    /// 이슈는 다루지 않고 고객사 접두도 붙이지 않는다. 차주 계획은 머리글만 두고 사용자가 채운다.
    private static string RenderC(WeeklyReportData data, ReportRenderOptions options)
    {
        var lines = new List<string>
        {
            options.TitleHeaderC,
            "",
            "== " + options.ReporterName,
        };

        foreach (var t in VisibleTasks(data, options))
        {
            lines.Add("- " + t.Text);
            lines.Add(FormatCIndent + options.DetailMarkerC);
        }

        lines.Add("");
        lines.Add(options.PlanHeaderC);

        return string.Join("\n", lines);
    }
}

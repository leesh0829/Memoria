using System.Globalization;
using Memoria.Core.Models;

namespace Memoria.Core.Reporting;

public sealed class WeeklyReportRenderer : IWeeklyReportRenderer
{
    public string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options)
        => (format switch
        {
            ReportFormatKind.A => RenderA(data, options),
            ReportFormatKind.B => RenderB(data, options),
            ReportFormatKind.C => FormatAReport.WithoutClientHeaders(RenderA(data, options), options),
            ReportFormatKind.D => RenderD(data, options),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        }).Replace("충북테크놀로지파크", "충북테크노파크 (FEMS)", StringComparison.Ordinal);

    /// 양식 D의 설명 머릿말 줄 들여쓰기. 붙여넣는 문서마다 탭 폭이 달라 정렬이 깨지므로
    /// 공통 report.indent(기본 탭) 대신 공백 4칸으로 고정한다.
    private const string FormatDIndent = "    ";

    private static IEnumerable<ReportTask> VisibleTasks(WeeklyReportData data, ReportRenderOptions options)
        => ReportTaskDeduplicator.Merge(options.IncludeDoneOnly ? data.Tasks.Where(t => t.Done) : data.Tasks);

    private static string RenderA(WeeklyReportData data, ReportRenderOptions options)
    {
        var lines = new List<string> { options.TaskHeaderA };
        var names = options.Clients.ToDictionary(c => c.Id, c => c.Name);
        var tasks = VisibleTasks(data, options).ToList();
        var clientOrder = options.Clients.Select((client, index) => (client.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);
        var groups = tasks.GroupBy(t => t.ClientId)
            .OrderBy(g => g.Key is int id && clientOrder.TryGetValue(id, out var index) ? index : int.MaxValue);
        foreach (var group in groups)
        {
            var name = group.Key is int id && names.TryGetValue(id, out var n) ? n : options.UnclassifiedLabel;
            lines.Add("- " + ClientHeader(name));
            foreach (var task in group)
            {
                var text = StripClientPrefix(task.Text, name, options.Clients);
                var date = task.CompletedDate?.ToString("MM/dd", CultureInfo.InvariantCulture) ?? "--/--";
                lines.Add(options.Indent + "- " + text + " [완료일자: " + date + "]");
                lines.Add("\t\t\to");
            }
        }
        lines.Add("");
        lines.Add(options.IssueHeaderA);
        // 고객사 미지정/삭제된 고객사의 이슈는 고객사 묶음보다 먼저 출력한다.
        var issueGroups = data.Issues
            .GroupBy(issue => issue.ClientId is int id && names.ContainsKey(id) ? (int?)id : null)
            .OrderBy(group => group.Key is int id ? clientOrder[id] : -1);
        foreach (var group in issueGroups)
        {
            var name = group.Key is int id ? names[id] : "";
            if (group.Key is not null) lines.Add("- " + ClientHeader(name));
            foreach (var issue in group.DistinctBy(i => i.Text.Trim()))
                lines.Add(options.Indent + "- " + StripClientPrefix(issue.Text, name, options.Clients));
        }
        return string.Join("\n", lines);
    }

    internal static string ClientHeader(string name)
        => name switch { "SLD" => "SLD PLC", "코모텍" => "코모텍 PLC", "SLD 도금" => "도금 PLC", "SLD 자율형공장" => "ICC", _ => name };

    private static string StripClientPrefix(string text, string name, IReadOnlyList<Client> clients)
    {
        // 긴 이름부터 비교하여 SLD 자율형공장을 SLD로 잘못 잘라내지 않는다.
        foreach (var prefix in clients.Select(c => c.Name).Append(name)
                     .Append("충북테크놀로지파크").OrderByDescending(n => n.Length))
        {
            if (string.IsNullOrWhiteSpace(prefix)) continue;
            if (text == prefix) return text; // 고객사명만 있는 원문까지 빈 항목으로 만들지 않는다.
            if (text.StartsWith(prefix, StringComparison.Ordinal)
                && " :：/-_\t".Contains(text[prefix.Length]))
                return text[prefix.Length..].TrimStart(' ', ':', '：', '/', '-', '_', '\t');
        }
        return text;
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

    /// 양식 D(기존 C): 보고자 이름 아래로 업무 한 건마다 "- 업무" + 빈 설명 머릿말("o") 줄.
    /// 이슈는 다루지 않고 고객사 접두도 붙이지 않는다. 차주 계획은 머리글만 두고 사용자가 채운다.
    private static string RenderD(WeeklyReportData data, ReportRenderOptions options)
    {
        var lines = new List<string>
        {
            options.TitleHeaderC,
            "",
            "== " + options.ReporterName,
        };

        foreach (var t in VisibleTasks(data, options).Where(t => IncludedInC(t, options)))
        {
            lines.Add("- " + t.Text);
            lines.Add(FormatDIndent + options.DetailMarkerC);
        }

        lines.Add("");
        lines.Add(options.PlanHeaderC);

        return string.Join("\n", lines);
    }

    /// 양식 C 고객사 필터. ClientIdsC가 null이면 전부 포함, 아니면 그 고객사로 분류된 업무만
    /// 포함한다(미분류 업무는 제외).
    private static bool IncludedInC(ReportTask t, ReportRenderOptions options)
        => options.ClientIdsC is null
           || (t.ClientId is int id && options.ClientIdsC.Contains(id));
}

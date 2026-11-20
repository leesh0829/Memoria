namespace Memoria.Core.Reporting;

/// <summary>양식 C는 A의 업무 고객사 머리글만 제거한다. 신규 하위 설명·이슈·일정은 유지한다.</summary>
public static class FormatAReport
{
    public static string WithoutClientHeaders(string text, ReportRenderOptions options)
    {
        var names = options.Clients.SelectMany(c => new[] { c.Name, WeeklyReportRenderer.ClientHeader(c.Name) })
            .Append(options.UnclassifiedLabel).Append("MES").Append("ICC")
            .Append("SLD PLC").Append("코모텍 PLC").Append("도금 PLC").ToHashSet(StringComparer.Ordinal);
        var headers = names.SelectMany(name => new[] { "- " + name, "[ " + name + " ]" })
            .ToHashSet(StringComparer.Ordinal);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var result = new List<string>();
        var inIssues = false;
        var legacyGroup = false;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line == options.IssueHeaderA || line == ReportBodyFormatter.ScheduleHeader) inIssues = true;
            if (inIssues || line == options.TaskHeaderA)
            {
                result.Add(line);
                continue;
            }
            if (headers.Contains(line))
            {
                legacyGroup = true;
                continue;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^■ .+ — \d+건 \(완료 \d+건\)$"))
            {
                legacyGroup = false;
                continue;
            }
            if (line == "\t\t\to" || line == ReportIndentation.TaskDetail) continue;
            var taskLine = line;
            if (legacyGroup && options.Indent.Length > 0 && taskLine.StartsWith(options.Indent + "- ", StringComparison.Ordinal))
                taskLine = taskLine[options.Indent.Length..];
            if (legacyGroup && taskLine.TrimStart('\t').StartsWith("- ", StringComparison.Ordinal))
                taskLine = taskLine.TrimStart(' ', '\t');
            result.Add(taskLine);
        }
        return string.Join(newline, result);
    }
}

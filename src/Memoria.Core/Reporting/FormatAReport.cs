namespace Memoria.Core.Reporting;

/// <summary>양식 C는 A 완성 본문에서 고객사 구분 줄만 제외한다.</summary>
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
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line == options.IssueHeaderA) inIssues = true;
            if (inIssues || line == options.TaskHeaderA || !headers.Contains(line)) result.Add(line);
        }
        return string.Join(newline, result);
    }
}

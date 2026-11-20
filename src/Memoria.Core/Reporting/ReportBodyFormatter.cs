using System.Text.RegularExpressions;

namespace Memoria.Core.Reporting;

/// <summary>A/C가 공유하는 업무 날짜·상세·이슈·일정 서식.</summary>
internal static class ReportBodyFormatter
{
    public const string ScheduleHeader = "[일정]";

    public static string TaskTitle(string text, string period)
    {
        var annotations = Regex.Match(text, @"^(?<title>.*?)(?<tags>(?:\s+\[[^\]\r\n]+\])+)$");
        if (!annotations.Success) return text + " (" + period + ")";
        var tags = Regex.Matches(annotations.Groups["tags"].Value, @"\[([^\]]+)\]")
            .Select(match => match.Groups[1].Value.Trim());
        return annotations.Groups["title"].Value.TrimEnd() + " (" + string.Join(", ", tags.Append(period)) + ")";
    }

    public static void AppendItem(List<string> lines, string text, Func<string, string>? title = null)
    {
        var parts = ReportIndentation.NormalizeTabs(text).Replace("\r\n", "\n").Split('\n');
        var first = parts[0].Trim();
        lines.Add("- " + (title is null ? first : title(first)));
        foreach (var detail in parts.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(detail)) continue;
            var content = detail.TrimStart();
            var indent = Math.Max(2, detail.Length - content.Length);
            if (content == "o") continue; // 이전 자동 생성 빈 설명 머릿말
            if (content.StartsWith("o ", StringComparison.Ordinal))
            {
                content = content[2..];
                indent = 2;
            }
            else if (content.StartsWith("- ", StringComparison.Ordinal)) content = content[2..];
            lines.Add(new string(' ', indent) + "- " + content);
        }
    }

    public static bool IsSchedule(string text)
    {
        var title = text.Split('\n')[0].Trim();
        if (title.StartsWith(ScheduleHeader, StringComparison.Ordinal)) return true;
        // 출장 관련 정산/증빙은 개인 일정으로 옮기지 않는다.
        if (Regex.IsMatch(title, "출장비|경비|법인카드|지출|결의|매출전표|정산|청구|증빙|일정 협의|일정 조정")) return false;
        var hasDate = Regex.IsMatch(title, @"\d{1,2}\s*/\s*\d{1,2}|\d{1,2}\s*월\s*\d{1,2}\s*일");
        var hasEvent = Regex.IsMatch(title, "연차|반차|휴가|휴무|출장|외근|강의|교육|세미나|행사 참석");
        return hasEvent && (hasDate || Regex.IsMatch(title, "(?:연차|반차|휴가|휴무|출장|외근)(?: 예정)?$|(?:참석|수강)(?: 예정)?$"));
    }

    public static string ScheduleTitle(string title)
    {
        if (title.StartsWith(ScheduleHeader, StringComparison.Ordinal)) title = title[ScheduleHeader.Length..].TrimStart();
        var date = Regex.Match(title, @"^(?<date>\d{1,2}\s*/\s*\d{1,2}(?:\s*~\s*(?:\d{1,2}\s*/\s*)?\d{1,2})?)(?:\s*[:：]\s*|\s+)(?<text>.+)$");
        return date.Success
            ? Regex.Replace(date.Groups["date"].Value, @"\s+", "") + ": " + date.Groups["text"].Value
            : title;
    }

    public static void StartSection(List<string> lines, string header)
    {
        if (lines.Count > 0 && lines[^1] != "") lines.Add("");
        lines.Add(header);
        lines.Add("");
    }

    public static string Finish(List<string> lines) => string.Join("\n", lines).TrimEnd('\n');
}

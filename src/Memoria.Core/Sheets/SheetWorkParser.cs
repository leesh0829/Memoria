using System.Text.RegularExpressions;

namespace Memoria.Core.Sheets;

public sealed record ParsedWeek(IReadOnlyList<string> Tasks, IReadOnlyList<string> Issues);

/// <summary>구글 시트 '일자 작업내역' 격자 → 대상 주(월~금)의 업무/이슈 텍스트. 순수 함수.</summary>
public static class SheetWorkParser
{
    // A열: 선행 YYYY.MM.DD (뒤의 " (요일)"은 무시).
    private static readonly Regex DateRx = new(@"^\s*(\d{4})\.(\d{1,2})\.(\d{1,2})", RegexOptions.Compiled);
    // 각 줄 선행 번호 "1. " / "2, " 제거.
    private static readonly Regex NumRx = new(@"^\s*\d+\s*[.,]\s*", RegexOptions.Compiled);

    public static ParsedWeek Parse(IReadOnlyList<IReadOnlyList<string>> grid, DateOnly monday, DateOnly friday)
    {
        var tasks = new List<string>();
        var issues = new List<string>();
        for (int r = 1; r < grid.Count; r++)   // 0행(헤더) 스킵
        {
            var row = grid[r];
            if (!TryParseDate(Cell(row, 0), out var date)) continue;
            if (date < monday || date > friday) continue;
            tasks.AddRange(SplitItems(Cell(row, 1)));
            issues.AddRange(SplitItems(Cell(row, 2)));
        }
        return new ParsedWeek(tasks, issues);
    }

    public static bool TryParseDate(string cell, out DateOnly date)
    {
        date = default;
        var m = DateRx.Match(cell ?? "");
        if (!m.Success) return false;
        int y = int.Parse(m.Groups[1].Value);
        int mo = int.Parse(m.Groups[2].Value);
        int d = int.Parse(m.Groups[3].Value);
        if (mo < 1 || mo > 12 || d < 1 || d > 31) return false;
        try { date = new DateOnly(y, mo, d); return true; }
        catch { return false; }
    }

    private static IEnumerable<string> SplitItems(string cell)
    {
        if (string.IsNullOrWhiteSpace(cell)) yield break;
        foreach (var raw in cell.Split('\n'))
        {
            var line = NumRx.Replace(raw.Trim().TrimEnd('\r').Trim(), "").Trim();
            if (line.Length > 0) yield return line;
        }
    }

    private static string Cell(IReadOnlyList<string> row, int i) => i < row.Count ? (row[i] ?? "") : "";
}

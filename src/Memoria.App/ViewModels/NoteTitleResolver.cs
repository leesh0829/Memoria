using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Memoria.Core.Models;
using Memoria.Core.Text;

namespace Memoria.App.ViewModels;

/// <summary>
/// 제목 표시 규칙: 체크리스트+log_date면 날짜 제목("2026-07-06 (월)").
/// 그 외에는 title → body 첫 비어있지 않은 줄 → "(제목 없음)".
/// markdown 노트는 선행 마커 제거. 모두 표시 전용(DB 미변경).
/// </summary>
public static class NoteTitleResolver
{
    private const string Weekdays = "일월화수목금토";   // DayOfWeek.Sunday = 0

    public static string Resolve(Note note)
    {
        // 체크리스트 다이어리: 날짜를 제목으로(Title/Body보다 우선).
        if (note.Type == NoteType.Checklist && note.LogDate is DateOnly d)
            return FormatChecklistDate(d);

        if (!string.IsNullOrWhiteSpace(note.Title))
            return note.Title!.Trim();

        if (!string.IsNullOrEmpty(note.Body))
        {
            var isMarkdown = note.BodyFormat == "markdown";
            foreach (var line in note.Body.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                return isMarkdown ? MarkdownText.StripMarkers(trimmed) : trimmed;
            }
        }
        return "(제목 없음)";
    }

    /// 목록 컨텍스트에서 같은 log_date 체크리스트가 2개 이상이면 id 오름차순으로
    /// 2번째부터 " (2)", " (3)" 접미사를 붙인다(정본=MIN id는 접미사 없음). 입력 순서 보존.
    public static IReadOnlyList<string> ResolveList(IReadOnlyList<Note> notes)
    {
        // (Checklist, log_date) 그룹별 id 오름차순 랭크 계산.
        var rank = new Dictionary<int, int>();   // noteId → 1-based rank (그 날짜 내 id 순)
        foreach (var g in notes
            .Where(n => n.Type == NoteType.Checklist && n.LogDate is not null)
            .GroupBy(n => n.LogDate!.Value))
        {
            var ordered = g.OrderBy(n => n.Id).ToList();
            for (int i = 0; i < ordered.Count; i++) rank[ordered[i].Id] = i + 1;
        }

        var result = new List<string>(notes.Count);
        foreach (var n in notes)
        {
            var title = Resolve(n);
            if (rank.TryGetValue(n.Id, out var r) && r >= 2)
                title += $" ({r})";
            result.Add(title);
        }
        return result;
    }

    internal static string FormatChecklistDate(DateOnly d)
        => $"{d:yyyy-MM-dd} ({Weekdays[(int)d.DayOfWeek]})";
}

using Memoria.Core.Models;
using Memoria.Core.Text;

namespace Memoria.App.ViewModels;

/// <summary>
/// 제목 표시 규칙(§5.1): title이 비면 body 첫 비어있지 않은 줄을 표시용 제목으로.
/// markdown 노트는 선행 마커를 제거해 표시.
/// </summary>
public static class NoteTitleResolver
{
    public static string Resolve(Note note)
    {
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
}

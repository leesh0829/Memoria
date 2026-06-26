using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

/// <summary>
/// 제목 표시 규칙(§5.1): title이 비면 body 첫 비어있지 않은 줄을 표시용 제목으로.
/// </summary>
public static class NoteTitleResolver
{
    public static string Resolve(Note note)
    {
        if (!string.IsNullOrWhiteSpace(note.Title))
            return note.Title!.Trim();

        if (!string.IsNullOrEmpty(note.Body))
        {
            foreach (var line in note.Body.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0) return trimmed;
            }
        }
        return "(제목 없음)";
    }
}

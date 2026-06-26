using Memoria.Core.Models;

namespace Memoria.Core.Data;

public interface INoteRepository
{
    int Create(Note note);                        // created_at/updated_at 채움, Id 반환
    void Update(Note note);                        // 전달된 Note 그대로 저장
    void SoftDelete(int id);                       // deleted_at 설정
    void Restore(int id);                          // deleted_at = null
    void Purge(int id);                            // 영구삭제(checklist_items CASCADE)
    void PurgeExpiredTrash(int retentionDays);     // deleted_at 경과분 영구삭제
    Note? Get(int id);
    IReadOnlyList<Note> GetByGroup(int? groupId);  // 활성, groupId=null → 미분류
    IReadOnlyList<Note> GetTrash();                // deleted_at NOT NULL
    IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday);
    Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format);
}

using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Tests.Fakes;

public sealed class FakeNoteRepository : INoteRepository
{
    public readonly List<Note> Items = new();
    private int _nextId = 100;
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// Backward-compat alias so existing tests that seed via _notes.Notes still work.
    public List<Note> Notes => Items;

    /// Tracks notes passed to Create(), for assertion in WeeklyReport tests.
    public readonly List<Note> Created = new();
    /// Tracks notes passed to Update(), for assertion in WeeklyReport tests.
    public readonly List<Note> Updated = new();

    public int Create(Note note)
    {
        note.Id = _nextId++;
        var now = Clock.GetUtcNow();
        note.CreatedAt = now;
        note.UpdatedAt = now;
        Items.Add(note);
        Created.Add(note);
        return note.Id;
    }

    public void Update(Note note)
    {
        Updated.Add(note);
        var i = Items.FindIndex(n => n.Id == note.Id);
        if (i >= 0) Items[i] = note; // 전달된 Note 그대로 저장(updated_at 갱신은 호출자 정책)
        else Items.Add(note);
    }

    public void SoftDelete(int id)
    {
        var n = Items.FirstOrDefault(x => x.Id == id);
        if (n != null) n.DeletedAt = Clock.GetUtcNow();
    }

    public void Restore(int id)
    {
        var n = Items.FirstOrDefault(x => x.Id == id);
        if (n != null) n.DeletedAt = null;
    }

    public void Purge(int id) => Items.RemoveAll(n => n.Id == id);

    public void PurgeExpiredTrash(int retentionDays)
    {
        var cutoff = Clock.GetUtcNow().AddDays(-retentionDays);
        Items.RemoveAll(n => n.DeletedAt is { } d && d <= cutoff);
    }

    public Note? Get(int id) => Items.FirstOrDefault(n => n.Id == id);

    public IReadOnlyList<Note> GetByGroup(int? groupId) =>
        Items.Where(n => n.DeletedAt == null && n.GroupId == groupId).ToList();

    public IReadOnlyList<Note> GetTrash() =>
        Items.Where(n => n.DeletedAt != null).ToList();

    public IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday) =>
        Items.Where(n => n.DeletedAt == null && n.Type == NoteType.Checklist
                         && n.LogDate is { } d && d >= monday && d <= friday).ToList();

    public Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format) =>
        Items.FirstOrDefault(n => n.Type == NoteType.WeeklyReport
                                  && n.ReportWeekStart == weekStart && n.ReportFormat == format);
}

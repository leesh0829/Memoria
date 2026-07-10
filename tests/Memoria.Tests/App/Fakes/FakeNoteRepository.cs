using System;
using System.Collections.Generic;
using System.Linq;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Tests.App.Fakes;

internal sealed class FakeNoteRepository : INoteRepository
{
    public List<Note> Items { get; } = new();
    public List<int> UpdatedIds { get; } = new();

    public int Create(Note note) { note.Id = Items.Count + 1; Items.Add(note); return note.Id; }

    public void Update(Note note)
    {
        var i = Items.FindIndex(n => n.Id == note.Id);
        if (i >= 0) Items[i] = note;
        UpdatedIds.Add(note.Id);
    }

    public void SoftDelete(int id)
    {
        var n = Items.FirstOrDefault(x => x.Id == id);
        if (n != null) n.DeletedAt = DateTimeOffset.UtcNow;
    }

    public void Restore(int id)
    {
        var n = Items.FirstOrDefault(x => x.Id == id);
        if (n != null) n.DeletedAt = null;
    }
    public void Purge(int id) { }
    public void PurgeExpiredTrash(int retentionDays) { }
    public Note? Get(int id) => Items.FirstOrDefault(n => n.Id == id);

    public IReadOnlyList<Note> GetByGroup(int? groupId) =>
        Items.Where(n => n.DeletedAt == null && n.GroupId == groupId).ToList();

    public IReadOnlyList<Note> GetTrash() => Items.Where(n => n.DeletedAt != null).ToList();
    public IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday) => new List<Note>();
    public Note? FindChecklistForDate(DateOnly date) =>
        Items.Where(n => n.DeletedAt == null && n.Type == NoteType.Checklist && n.LogDate == date)
             .OrderBy(n => n.Id).FirstOrDefault();
    public Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format) => null;
}

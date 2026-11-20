using Dapper;
using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Tests.Data;

public class HandoffWorkEntryTests
{
    [Fact]
    public void UnchangedSource_PreservesWorkEntryEdits_AndRenamedClientIsReflected()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var items = new ChecklistRepository(db.Factory);
        var clients = new ClientRepository(db.Factory);
        var entries = new WorkEntryRepository(db.Factory);
        var client = clients.GetAll().First();
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new(2026, 11, 20) });
        var item = new ChecklistItem { NoteId = noteId, Kind = ItemKind.Handoff, Text = "인수인계", ClientId = client.Id };
        items.AddItem(item);
        var entry = entries.GetAll().Single();
        entry.Request = "업무내역에서 보완";
        entry.Statuses = ["진행중"];
        entries.Save(entry);
        var unchanged = entries.GetAll().Single();
        unchanged.Request.Should().Be("업무내역에서 보완");
        unchanged.Statuses.Should().Equal("진행중");
        client.Name += " 변경";
        clients.Update(client);
        entries.GetAll().Single().Clients.Should().Equal(client.Name);
        item.Text = "일지 원본 수정";
        items.UpdateItem(item);
        entries.GetAll().Single().Request.Should().Be("일지 원본 수정");
        notes.Purge(noteId);
        entries.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void ReusedChecklistId_DoesNotInheritDeletedHandoffDetails()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var items = new ChecklistRepository(db.Factory);
        var entries = new WorkEntryRepository(db.Factory);
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new(2026, 11, 20) });
        var first = new ChecklistItem { NoteId = noteId, Kind = ItemKind.Handoff, Text = "이전 후임 업무" };
        items.AddItem(first);
        lock (db.Factory.WriteSync)
            db.Factory.Write.Execute("UPDATE checklist_items SET created_at = @CreatedAt WHERE id = @Id;",
                new { first.Id, CreatedAt = first.CreatedAt.AddDays(-1) });
        var entry = entries.GetAll().Single();
        entry.Assignee = "이전 담당자";
        entries.Save(entry);
        items.DeleteItem(first.Id);
        var second = new ChecklistItem { NoteId = noteId, Kind = ItemKind.Handoff, Text = "새 후임 업무" };
        items.AddItem(second);
        second.Id.Should().Be(first.Id);
        var updated = entries.GetAll().Should().ContainSingle().Subject;
        updated.Id.Should().NotBe(entry.Id);
        updated.Assignee.Should().BeEmpty();
        updated.Request.Should().Be(second.Text);
    }

    [Fact]
    public void Handoff_SyncsOnce_UpdatesSourceFields_AndPreservesWorkEntryDetails()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var items = new ChecklistRepository(db.Factory);
        var entries = new WorkEntryRepository(db.Factory);
        var client = new ClientRepository(db.Factory).GetAll().First();
        var date = new DateOnly(2026, 11, 20);
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = date });
        var item = new ChecklistItem { NoteId = noteId, Kind = ItemKind.Handoff, Text = "후임 점검", ClientId = client.Id };
        items.AddItem(item);
        items.GetByNote(noteId).Single().Kind.Should().Be(ItemKind.Handoff);

        var entry = entries.GetAll().Should().ContainSingle().Subject;
        entry.Request.Should().Be("후임 점검");
        entry.Clients.Should().Equal(client.Name);
        entry.RequestedDate.Should().Be(date.ToDateTime(TimeOnly.MinValue));
        entry.Action = "별도 조치";
        entry.Cause = "기록한 원인";
        entry.Assignee = "후임";
        entries.Save(entry);
        entries.GetAll().Should().ContainSingle().Which.Id.Should().Be(entry.Id);

        item.Text = "후임 수정 점검";
        item.Done = true;
        item.DoneAt = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(9));
        items.UpdateItem(item);
        var updated = entries.GetAll().Should().ContainSingle().Subject;
        updated.Id.Should().Be(entry.Id);
        updated.Request.Should().Be(item.Text);
        updated.Done.Should().BeTrue();
        updated.Statuses.Should().Equal("완료");
        updated.ProcessingEnd.Should().Be(item.DoneAt.Value.LocalDateTime.Date);
        updated.Action.Should().Be("별도 조치");
        updated.Cause.Should().Be("기록한 원인");
        updated.Assignee.Should().Be("후임");

        item.Done = false;
        item.DoneAt = null;
        items.UpdateItemMetadata(item);
        var reopened = entries.GetAll().Single();
        reopened.Done.Should().BeFalse();
        reopened.Statuses.Should().Equal("대기중");
        reopened.ProcessingEnd.Should().BeNull();
    }

    [Fact]
    public void Handoff_HidesTrashedNotes_RestoresDetails_AndRemovesDeletedSources()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var items = new ChecklistRepository(db.Factory);
        var entries = new WorkEntryRepository(db.Factory);
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new(2026, 11, 20) });
        var item = new ChecklistItem { NoteId = noteId, Kind = ItemKind.Handoff, Text = "인수인계" };
        items.AddItem(item);
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "본인 업무" });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Issue, Text = "이슈" });
        var entry = entries.GetAll().Should().ContainSingle().Subject;
        entry.Assignee = "후임 담당자";
        entries.Save(entry);
        notes.SoftDelete(noteId);
        entries.GetAll().Should().BeEmpty();
        notes.Restore(noteId);
        entries.GetAll().Should().ContainSingle().Which.Assignee.Should().Be("후임 담당자");
        items.DeleteItem(item.Id);
        entries.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void BlankHandoffs_AreNotExported_AndOrdinaryWorkEntriesArePreserved()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var items = new ChecklistRepository(db.Factory);
        var entries = new WorkEntryRepository(db.Factory);
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new(2026, 11, 20) });
        var blank = new ChecklistItem { NoteId = noteId, Kind = ItemKind.Handoff, Text = "  " };
        items.AddItem(blank);
        var ordinary = new WorkEntry { Request = "직접 입력" };
        entries.Save(ordinary);
        entries.GetAll().Should().ContainSingle().Which.Id.Should().Be(ordinary.Id);
        blank.Text = "입력 완료";
        items.UpdateItems([blank]);
        entries.GetAll().Should().HaveCount(2);
        blank.Text = "";
        items.UpdateItems([blank]);
        entries.GetAll().Should().ContainSingle().Which.Id.Should().Be(ordinary.Id);
    }
}

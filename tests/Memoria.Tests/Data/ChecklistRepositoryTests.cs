using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Data;

public class ChecklistRepositoryTests
{
    private static int NewNote(SqliteConnectionFactory f) =>
        new NoteRepository(f).Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 22) });

    [Fact]
    public void AddItem_GetByNote_RoundTrips_OrderedBySortOrder()
    {
        using var db = new TestDb();
        var noteId = NewNote(db.Factory);
        var sut = new ChecklistRepository(db.Factory);

        sut.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Issue, Text = "이슈", SortOrder = 1 });
        sut.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "할일", SortOrder = 0, ClientId = 1, IsManual = true });

        var items = sut.GetByNote(noteId);
        items.Should().HaveCount(2);
        items[0].Text.Should().Be("할일");
        items[0].Kind.Should().Be(ItemKind.Task);
        items[0].ClientId.Should().Be(1);
        items[0].IsManual.Should().BeTrue();
        items[0].CreatedAt.Should().BeAfter(DateTimeOffset.UnixEpoch);
        items[1].Kind.Should().Be(ItemKind.Issue);
    }

    [Fact]
    public void UpdateItem_PersistsDoneAndClient()
    {
        using var db = new TestDb();
        var noteId = NewNote(db.Factory);
        var sut = new ChecklistRepository(db.Factory);
        var id = sut.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "t" });

        var item = sut.GetByNote(noteId).Single();
        item.Done = true;
        item.DoneAt = DateTimeOffset.UtcNow;
        item.ClientId = 2;
        sut.UpdateItem(item);

        var reloaded = sut.GetByNote(noteId).Single();
        reloaded.Done.Should().BeTrue();
        reloaded.DoneAt.Should().NotBeNull();
        reloaded.ClientId.Should().Be(2);
    }

    [Fact]
    public void DeleteItem_RemovesIt()
    {
        using var db = new TestDb();
        var noteId = NewNote(db.Factory);
        var sut = new ChecklistRepository(db.Factory);
        var id = sut.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "t" });

        sut.DeleteItem(id);
        sut.GetByNote(noteId).Should().BeEmpty();
    }

    [Fact]
    public void PurgingParentNote_CascadeDeletesItems()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var noteId = NewNote(db.Factory);
        var sut = new ChecklistRepository(db.Factory);
        sut.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "t" });

        notes.Purge(noteId);

        sut.GetByNote(noteId).Should().BeEmpty();
    }
}

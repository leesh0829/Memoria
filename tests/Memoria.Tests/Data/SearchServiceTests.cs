using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Data;

public class SearchServiceTests
{
    [Fact]
    public void Search_FindsByTitleAndBody()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var sut = new SearchService(db.Factory);
        var id = notes.Create(new Note { Type = NoteType.Plain, Title = "회의록", Body = "SLD 점검 내용" });

        var hits = sut.Search("SLD");
        hits.Should().ContainSingle(h => h.NoteId == id);
    }

    [Fact]
    public void Search_FindsByChecklistItemText()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var items = new ChecklistRepository(db.Factory);
        var sut = new SearchService(db.Factory);
        var id = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 22) });
        items.AddItem(new ChecklistItem { NoteId = id, Kind = ItemKind.Task, Text = "코모텍 미팅" });

        sut.Search("코모텍").Should().ContainSingle(h => h.NoteId == id);
    }

    [Fact]
    public void Search_ExcludesSoftDeletedNotes()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var sut = new SearchService(db.Factory);
        var id = notes.Create(new Note { Type = NoteType.Plain, Title = "삭제대상", Body = "카본센스 자료" });
        notes.SoftDelete(id);

        sut.Search("카본센스").Should().BeEmpty();
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        using var db = new TestDb();
        var sut = new SearchService(db.Factory);
        sut.Search("   ").Should().BeEmpty();
    }
}

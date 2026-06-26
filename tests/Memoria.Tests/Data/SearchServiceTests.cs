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

    [Fact]
    public void Search_PopulatesSnippetFromMatchedColumn()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var sut = new SearchService(db.Factory);
        notes.Create(new Note
        {
            Type = NoteType.Plain,
            Title = "회의록",
            Body = "오늘 자율형공장 라인에서 SLD 점검을 수행했다",
        });

        var hit = sut.Search("자율형공장").Should().ContainSingle().Subject;
        hit.Snippet.Should().NotBeNullOrEmpty();
        hit.Snippet.Should().Contain("자율형공장");
    }

    [Fact]
    public void Search_OrdersByRelevance_StrongerMatchFirst()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var sut = new SearchService(db.Factory);

        // 약한 매칭: 긴 본문에 검색어가 1회만 등장 (먼저 생성 → 낮은 rowid)
        var weakId = notes.Create(new Note
        {
            Type = NoteType.Plain,
            Title = "잡담",
            Body = "베타 그리고 서로 관련 없는 매우 길고 다양한 다른 내용 으로 가득 채워진 본문",
        });
        // 강한 매칭: 짧은 본문에 검색어가 여러 번 등장 (나중 생성 → 높은 rowid)
        var strongId = notes.Create(new Note
        {
            Type = NoteType.Plain,
            Title = "베타",
            Body = "베타 베타 베타",
        });

        var hits = sut.Search("베타");

        hits.Should().HaveCount(2);
        hits[0].NoteId.Should().Be(strongId); // FTS5 rank(관련도) 순 — 강한 매칭이 먼저
        hits[1].NoteId.Should().Be(weakId);
    }
}

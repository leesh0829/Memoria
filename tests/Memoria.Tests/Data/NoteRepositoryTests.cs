using System.Linq;
using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Data;

public class NoteRepositoryTests
{
    [Fact]
    public void Create_FillsTimestamps_AndRoundTripsEnumsAndDates()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);

        var note = new Note
        {
            Type = NoteType.WeeklyReport,
            Title = "주간",
            Body = "내용",
            ReportFormat = ReportFormatKind.B,
            ReportWeekStart = new DateOnly(2026, 6, 22),
        };
        var id = sut.Create(note);

        var loaded = sut.Get(id)!;
        loaded.Type.Should().Be(NoteType.WeeklyReport);
        loaded.ReportFormat.Should().Be(ReportFormatKind.B);
        loaded.ReportWeekStart.Should().Be(new DateOnly(2026, 6, 22));
        loaded.CreatedAt.Should().BeAfter(DateTimeOffset.UnixEpoch);
        loaded.UpdatedAt.Should().BeAfter(DateTimeOffset.UnixEpoch);
        loaded.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void GetByGroup_FiltersByGroupAndExcludesDeleted_NullMeansUnclassified()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var inGroup = sut.Create(new Note { Type = NoteType.Plain, GroupId = 1, Title = "g" });
        var unclassified = sut.Create(new Note { Type = NoteType.Plain, GroupId = null, Title = "u" });

        sut.GetByGroup(1).Should().Contain(n => n.Id == inGroup);
        sut.GetByGroup(null).Should().Contain(n => n.Id == unclassified)
            .And.NotContain(n => n.Id == inGroup);
    }

    [Fact]
    public void GetByGroup_OrdersBySortOrderAscending_WithinSamePinned()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id2 = sut.Create(new Note { Type = NoteType.Plain, GroupId = null, Title = "second", SortOrder = 2 });
        var id0 = sut.Create(new Note { Type = NoteType.Plain, GroupId = null, Title = "zeroth", SortOrder = 0 });
        var id1 = sut.Create(new Note { Type = NoteType.Plain, GroupId = null, Title = "first",  SortOrder = 1 });

        sut.GetByGroup(null).Select(n => n.Id).Should().ContainInOrder(id0, id1, id2);
    }

    [Fact]
    public void GetByGroup_TieBreaksBySortOrderZero_ByUpdatedAtDescending()
    {
        // 하위호환 보증: 기존 노트는 전부 sort_order=0 → 동률이므로 updated_at DESC로 폴백해야 한다.
        // (Create가 updated_at을 now로 덮으므로 Update로 명시 타임스탬프를 심는다.)
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var older = sut.Create(new Note { Type = NoteType.Plain, GroupId = null, Title = "older" });
        var newer = sut.Create(new Note { Type = NoteType.Plain, GroupId = null, Title = "newer" });
        var o = sut.Get(older)!; o.UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); sut.Update(o);
        var n = sut.Get(newer)!; n.UpdatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero); sut.Update(n);

        sut.GetByGroup(null).Select(x => x.Id).Should().ContainInOrder(newer, older);
    }

    [Fact]
    public void SetSortOrder_UpdatesOnlySortOrder_NotUpdatedAt()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note { Type = NoteType.Plain, GroupId = null, Title = "t" });
        var before = sut.Get(id)!.UpdatedAt;

        sut.SetSortOrder(id, 5);

        var after = sut.Get(id)!;
        after.SortOrder.Should().Be(5);
        after.UpdatedAt.Should().Be(before); // 순서변경은 updated_at을 건드리지 않는다
    }

    [Fact]
    public void SoftDelete_Restore_Purge_Work()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note { Type = NoteType.Plain, GroupId = null, Title = "t" });

        sut.SoftDelete(id);
        sut.GetByGroup(null).Should().NotContain(n => n.Id == id);
        sut.GetTrash().Should().Contain(n => n.Id == id);

        sut.Restore(id);
        sut.GetTrash().Should().NotContain(n => n.Id == id);
        sut.GetByGroup(null).Should().Contain(n => n.Id == id);

        sut.SoftDelete(id);
        sut.Purge(id);
        sut.Get(id).Should().BeNull();
    }

    [Fact]
    public void PurgeExpiredTrash_RemovesOnlyOldDeleted()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var oldId = sut.Create(new Note { Type = NoteType.Plain, Title = "old" });
        var recentId = sut.Create(new Note { Type = NoteType.Plain, Title = "recent" });

        var old = sut.Get(oldId)!;
        old.DeletedAt = DateTimeOffset.UtcNow.AddDays(-40);
        sut.Update(old);
        sut.SoftDelete(recentId);

        sut.PurgeExpiredTrash(retentionDays: 30);

        sut.Get(oldId).Should().BeNull();
        sut.Get(recentId).Should().NotBeNull();
    }

    [Fact]
    public void GetChecklistsInWeek_ReturnsOnlyChecklistsInRange()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 23), Title = "in" });
        sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 29), Title = "out" });
        sut.Create(new Note { Type = NoteType.Plain, Title = "plain" });

        var results = sut.GetChecklistsInWeek(new DateOnly(2026, 6, 22), new DateOnly(2026, 6, 26));
        results.Should().ContainSingle().Which.Title.Should().Be("in");
    }

    [Fact]
    public void FindWeeklyReport_MatchesWeekStartAndFormat()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note
        {
            Type = NoteType.WeeklyReport,
            ReportFormat = ReportFormatKind.A,
            ReportWeekStart = new DateOnly(2026, 6, 22),
        });

        sut.FindWeeklyReport(new DateOnly(2026, 6, 22), ReportFormatKind.A)!.Id.Should().Be(id);
        sut.FindWeeklyReport(new DateOnly(2026, 6, 22), ReportFormatKind.B).Should().BeNull();
    }

    [Fact]
    public void Create_DefaultsBodyFormat_ToPlain()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note { Type = NoteType.Plain, Title = "t" });
        sut.Get(id)!.BodyFormat.Should().Be("plain");
    }

    [Fact]
    public void Create_And_Get_RoundTrips_MarkdownFormat()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note { Type = NoteType.Plain, Title = "t", BodyFormat = "markdown" });
        sut.Get(id)!.BodyFormat.Should().Be("markdown");
    }

    [Fact]
    public void FindChecklistForDate_ReturnsNull_WhenNone()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        sut.FindChecklistForDate(new DateOnly(2026, 7, 6)).Should().BeNull();
    }

    [Fact]
    public void FindChecklistForDate_ReturnsMatch_ForThatDate()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) });
        sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 7) });

        sut.FindChecklistForDate(new DateOnly(2026, 7, 6))!.Id.Should().Be(id);
    }

    [Fact]
    public void FindChecklistForDate_ReturnsLowestId_WhenDuplicates()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var first = sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) });
        sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) });

        sut.FindChecklistForDate(new DateOnly(2026, 7, 6))!.Id.Should().Be(first);
    }

    [Fact]
    public void FindChecklistForDate_IgnoresSoftDeleted()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) });
        sut.SoftDelete(id);

        sut.FindChecklistForDate(new DateOnly(2026, 7, 6)).Should().BeNull();
    }
}

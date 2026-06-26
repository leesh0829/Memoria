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
}

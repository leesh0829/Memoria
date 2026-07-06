using FluentAssertions;
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Memoria.Tests.Data;
using Xunit;

namespace Memoria.Tests.Services;

public class WeeklyReportServiceTests
{
    private static (WeeklyReportService Svc, NoteRepository Notes, ChecklistRepository Items, ClientRepository Clients) Build(TestDb db)
    {
        var notes = new NoteRepository(db.Factory);
        var items = new ChecklistRepository(db.Factory);
        var clients = new ClientRepository(db.Factory);
        var svc = new WeeklyReportService(
            new WeekCalculator(), notes, items, new ClientClassifier(), clients, new WeeklyReportRenderer());
        return (svc, notes, items, clients);
    }

    [Fact]
    public void Build_CollectsWeekTasks_ReclassifiesAuto_CountsUnclassified()
    {
        using var db = new TestDb();
        var (svc, notes, items, clients) = Build(db);
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 23) });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "SLD 점검", SortOrder = 0 });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "기타 정리", SortOrder = 1 });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Issue, Text = "장비 오류", SortOrder = 2 });

        var sldId = clients.GetAll().Single(c => c.Name == "SLD").Id;
        var options = new ReportRenderOptions
        {
            WeekStart = new DateOnly(2026, 6, 22),
            WeekEnd = new DateOnly(2026, 6, 26),
            Clients = clients.GetAll(enabledOnly: true),
        };

        var result = svc.Build(new DateOnly(2026, 6, 24), options);

        result.Monday.Should().Be(new DateOnly(2026, 6, 22));
        result.Friday.Should().Be(new DateOnly(2026, 6, 26));
        result.Data.Tasks.Should().HaveCount(2);
        result.Data.Issues.Should().HaveCount(1);
        result.UnclassifiedTaskCount.Should().Be(1);
        result.Data.Tasks.Should().Contain(t => t.Text == "SLD 점검" && t.ClientId == sldId);
        result.Data.Tasks.Should().Contain(t => t.Text == "기타 정리" && t.ClientId == null);
    }

    [Fact]
    public void Build_KeepsManualClassification()
    {
        using var db = new TestDb();
        var (svc, notes, items, clients) = Build(db);
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 23) });
        // 수동으로 코모텍 지정했지만 텍스트는 SLD → 재분류로 덮어쓰지 않아야 함
        var komotekId = clients.GetAll().Single(c => c.Name == "코모텍").Id;
        items.AddItem(new ChecklistItem
        {
            NoteId = noteId, Kind = ItemKind.Task, Text = "SLD 점검",
            IsManual = true, ClientId = komotekId,
        });

        var options = new ReportRenderOptions
        {
            WeekStart = new DateOnly(2026, 6, 22),
            WeekEnd = new DateOnly(2026, 6, 26),
            Clients = clients.GetAll(enabledOnly: true),
        };
        var result = svc.Build(new DateOnly(2026, 6, 23), options);

        result.Data.Tasks.Single().ClientId.Should().Be(komotekId);
    }

    [Fact]
    public void Render_DelegatesToRenderer_FormatB()
    {
        using var db = new TestDb();
        var (svc, notes, items, clients) = Build(db);
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 23) });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "SLD 점검", SortOrder = 0 });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "기타 정리", SortOrder = 1 });

        var options = new ReportRenderOptions
        {
            WeekStart = new DateOnly(2026, 6, 22),
            WeekEnd = new DateOnly(2026, 6, 26),
            Clients = clients.GetAll(enabledOnly: true),
        };
        var result = svc.Build(new DateOnly(2026, 6, 23), options);

        var text = svc.Render(ReportFormatKind.B, result.Data, options);

        text.Should().StartWith("[ 이승현 주간 보고 (06/22 ~ 06/26) ]:");
        text.Should().Contain("[ SLD ]\n\t* SLD 점검");
        text.Should().Contain("[ 미분류 ]\n\t* 기타 정리");
    }

    [Fact]
    public void BuildFromTexts_ClassifiesTasks_WrapsIssues_CountsUnclassified()
    {
        using var db = new TestDb();
        var (svc, _, _, clients) = Build(db);
        var sldId = clients.GetAll().Single(c => c.Name == "SLD").Id;
        var options = new ReportRenderOptions
        {
            WeekStart = new DateOnly(2026, 6, 22),
            WeekEnd = new DateOnly(2026, 6, 26),
            Clients = clients.GetAll(enabledOnly: true),
        };

        var result = svc.BuildFromTexts(
            new[] { "SLD 점검", "기타 정리" },
            new[] { "장비 오류" },
            new DateOnly(2026, 6, 22), new DateOnly(2026, 6, 26), options);

        result.Monday.Should().Be(new DateOnly(2026, 6, 22));
        result.Friday.Should().Be(new DateOnly(2026, 6, 26));
        result.Data.Tasks.Should().HaveCount(2);
        result.Data.Tasks.Should().OnlyContain(t => t.Done);   // 시트엔 완료여부 없음 → 전부 완료
        result.Data.Tasks.Should().Contain(t => t.Text == "SLD 점검" && t.ClientId == sldId);
        result.Data.Issues.Should().ContainSingle(i => i.Text == "장비 오류");
        result.UnclassifiedTaskCount.Should().Be(1);
    }

    [Fact]
    public void Build_IncludeDoneOnly_AffectsUnclassifiedCount()
    {
        using var db = new TestDb();
        var (svc, notes, items, clients) = Build(db);
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 23) });
        // 미분류 task 2개: 하나만 done
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "잡무1", Done = true, SortOrder = 0 });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "잡무2", Done = false, SortOrder = 1 });

        var options = new ReportRenderOptions
        {
            WeekStart = new DateOnly(2026, 6, 22),
            WeekEnd = new DateOnly(2026, 6, 26),
            Clients = clients.GetAll(enabledOnly: true),
            IncludeDoneOnly = true,
        };

        var result = svc.Build(new DateOnly(2026, 6, 23), options);

        result.Data.Tasks.Should().HaveCount(2);   // Data는 전부 포함(렌더러가 필터)
        result.UnclassifiedTaskCount.Should().Be(1); // done인 미분류만 카운트
    }
}

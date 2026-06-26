// tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Memoria.Tests.Fakes;

namespace Memoria.Tests.ViewModels;

public class WeeklyReportViewModelTests
{
    private static (WeeklyReportViewModel vm, FakeWeeklyReportService svc, FakeNoteRepository notes,
        FakeClientRepository clients, FakeGroupRepository groups, FakeSettingsRepository settings,
        FakeClipboardService clip, FakeConfirmationDialogService dlg) CreateSut(DateTimeOffset? now = null)
    {
        var svc = new FakeWeeklyReportService();
        var notes = new FakeNoteRepository();
        var clients = new FakeClientRepository();
        var groups = new FakeGroupRepository
        {
            Groups = { new Group { Id = 2, Name = "주간보고", IsSystem = true, SortOrder = 1 } }
        };
        var settings = new FakeSettingsRepository();
        var clip = new FakeClipboardService();
        var dlg = new FakeConfirmationDialogService();
        var vm = new WeeklyReportViewModel(
            svc, new FakeWeekCalculator(), notes, clients, groups, settings, clip, dlg,
            new WeeklyReportFixedTimeProvider(now ?? new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero)));
        return (vm, svc, notes, clients, groups, settings, clip, dlg);
    }

    [Fact]
    public void Default_week_is_current_week_monday_to_friday()
    {
        // 2026-06-24 == 수요일 → 그 주 월 06/22, 금 06/26
        var (vm, _, _, _, _, _, _, _) = CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));

        vm.SelectedDate.Should().Be(new DateOnly(2026, 6, 24));
        vm.WeekStart.Should().Be(new DateOnly(2026, 6, 22));
        vm.WeekEnd.Should().Be(new DateOnly(2026, 6, 26));
        vm.WeekRangeLabel.Should().Be("06/22 ~ 06/26");
        vm.SelectedFormat.Should().Be(ReportFormatKind.A);
    }

    [Fact]
    public void Changing_selected_date_recomputes_week_range()
    {
        var (vm, _, _, _, _, _, _, _) = CreateSut();

        vm.SelectedDate = new DateOnly(2026, 1, 1); // 목요일 → 월 2025-12-29, 금 2026-01-02

        vm.WeekStart.Should().Be(new DateOnly(2025, 12, 29));
        vm.WeekEnd.Should().Be(new DateOnly(2026, 1, 2));
        vm.WeekRangeLabel.Should().Be("12/29 ~ 01/02");
    }

    [Fact]
    public void Generate_builds_options_from_settings_and_enabled_clients()
    {
        var (vm, svc, _, clients, _, settings, _, _) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));

        settings.Set(SettingsKeys.ReporterName, "홍길동");
        settings.Set(SettingsKeys.FormatATaskHeader, "[할 일]");
        settings.Set(SettingsKeys.FormatAIssueHeader, "[이슈들]");
        settings.Set(SettingsKeys.FormatBTitleWord, "위클리");
        settings.Set(SettingsKeys.FormatBIssueHeader, "* 이슈:");
        settings.Set(SettingsKeys.ReportIndent, "  ");
        settings.Set(SettingsKeys.IncludeDoneOnly, "true");
        clients.Clients.Add(new Client { Id = 1, Name = "SLD", SortOrder = 1, Enabled = true });
        clients.Clients.Add(new Client { Id = 2, Name = "MTP", SortOrder = 2, Enabled = false });

        vm.GenerateCommand.Execute(null);

        clients.LastEnabledOnly.Should().BeTrue();
        var opts = svc.LastOptions!;
        opts.ReporterName.Should().Be("홍길동");
        opts.TaskHeaderA.Should().Be("[할 일]");
        opts.IssueHeaderA.Should().Be("[이슈들]");
        opts.TitleWordB.Should().Be("위클리");
        opts.IssueHeaderB.Should().Be("* 이슈:");
        opts.Indent.Should().Be("  ");
        opts.IncludeDoneOnly.Should().BeTrue();
        opts.WeekStart.Should().Be(new DateOnly(2026, 6, 22));
        opts.WeekEnd.Should().Be(new DateOnly(2026, 6, 26));
        opts.Clients.Select(c => c.Name).Should().Equal("SLD"); // enabledOnly → MTP 제외
        svc.LastAnyDate.Should().Be(new DateOnly(2026, 6, 24));
    }

    [Fact]
    public void Generate_uses_contract_defaults_when_settings_missing()
    {
        var (vm, svc, _, _, _, _, _, _) = CreateSut();

        vm.GenerateCommand.Execute(null);

        var opts = svc.LastOptions!;
        opts.ReporterName.Should().Be("이승현");
        opts.TaskHeaderA.Should().Be("[업무 내용]");
        opts.IssueHeaderA.Should().Be("[이슈]");
        opts.TitleWordB.Should().Be("주간 보고");
        opts.IssueHeaderB.Should().Be("* 이슈사항:");
        opts.Indent.Should().Be("\t");
        opts.IncludeDoneOnly.Should().BeFalse();
        opts.UnclassifiedLabel.Should().Be("미분류");
    }

    [Fact]
    public void Generate_sets_report_text_from_renderer_for_selected_format()
    {
        var (vm, svc, _, _, _, _, _, _) = CreateSut();
        svc.RenderResult = "최종 보고서 본문";
        vm.SelectedFormat = ReportFormatKind.B;

        vm.GenerateCommand.Execute(null);

        vm.ReportText.Should().Be("최종 보고서 본문");
        svc.LastRenderFormat.Should().Be(ReportFormatKind.B);
    }

    [Fact]
    public void Warning_banner_shows_only_when_unclassified_count_positive()
    {
        var (vm, svc, _, _, _, _, _, _) = CreateSut();
        svc.BuildImpl = (d, o) => new WeeklyReportBuildResult(
            new WeeklyReportData(new List<ReportTask>(), new List<ReportIssue>()), 3, o.WeekStart, o.WeekEnd);

        vm.GenerateCommand.Execute(null);

        vm.UnclassifiedTaskCount.Should().Be(3);
        vm.HasUnclassifiedWarning.Should().BeTrue();

        svc.BuildImpl = (d, o) => new WeeklyReportBuildResult(
            new WeeklyReportData(new List<ReportTask>(), new List<ReportIssue>()), 0, o.WeekStart, o.WeekEnd);
        vm.GenerateCommand.Execute(null);

        vm.UnclassifiedTaskCount.Should().Be(0);
        vm.HasUnclassifiedWarning.Should().BeFalse();
    }

    [Fact]
    public void Generate_reuses_existing_report_body_without_rebuilding()
    {
        var (vm, svc, notes, _, _, _, _, _) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
        notes.Notes.Add(new Note
        {
            Id = 55,
            Type = NoteType.WeeklyReport,
            ReportFormat = ReportFormatKind.A,
            ReportWeekStart = new DateOnly(2026, 6, 22),
            Body = "사용자가 손으로 편집한 보고서",
        });

        vm.GenerateCommand.Execute(null);

        vm.ReportText.Should().Be("사용자가 손으로 편집한 보고서");
        notes.Created.Should().BeEmpty();
        notes.Updated.Should().BeEmpty();
    }

    [Fact]
    public void Generate_creates_new_report_in_weekly_report_system_group()
    {
        var (vm, svc, notes, _, _, _, _, _) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
        svc.RenderResult = "새로 생성된 본문";

        vm.GenerateCommand.Execute(null);

        notes.Created.Should().HaveCount(1);
        var created = notes.Created[0];
        created.Type.Should().Be(NoteType.WeeklyReport);
        created.GroupId.Should().Be(2); // FakeGroupRepository의 '주간보고' 시스템 그룹 Id
        created.ReportFormat.Should().Be(ReportFormatKind.A);
        created.ReportWeekStart.Should().Be(new DateOnly(2026, 6, 22));
        created.Body.Should().Be("새로 생성된 본문");
    }

    [Fact]
    public void Regenerate_overwrites_existing_body_after_confirm()
    {
        var (vm, svc, notes, _, _, _, _, dlg) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
        notes.Notes.Add(new Note
        {
            Id = 77,
            Type = NoteType.WeeklyReport,
            ReportFormat = ReportFormatKind.A,
            ReportWeekStart = new DateOnly(2026, 6, 22),
            Body = "예전 편집본",
        });
        svc.RenderResult = "재생성된 본문";
        dlg.Result = true;

        vm.RegenerateCommand.Execute(null);

        dlg.CallCount.Should().Be(1);
        notes.Updated.Should().HaveCount(1);
        notes.Updated[0].Id.Should().Be(77);
        notes.Updated[0].Body.Should().Be("재생성된 본문");
        vm.ReportText.Should().Be("재생성된 본문");
    }

    [Fact]
    public void Regenerate_keeps_existing_body_when_confirm_declined()
    {
        var (vm, svc, notes, _, _, _, _, dlg) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
        notes.Notes.Add(new Note
        {
            Id = 88,
            Type = NoteType.WeeklyReport,
            ReportFormat = ReportFormatKind.A,
            ReportWeekStart = new DateOnly(2026, 6, 22),
            Body = "지키고 싶은 편집본",
        });
        svc.RenderResult = "버려질 본문";
        dlg.Result = false;

        vm.RegenerateCommand.Execute(null);

        dlg.CallCount.Should().Be(1);
        notes.Updated.Should().BeEmpty();
        notes.Notes.Single().Body.Should().Be("지키고 싶은 편집본");
    }

    [Fact]
    public void Regenerate_does_not_prompt_when_no_existing_body()
    {
        var (vm, svc, notes, _, _, _, _, dlg) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
        svc.RenderResult = "첫 생성 본문";

        vm.RegenerateCommand.Execute(null);

        dlg.CallCount.Should().Be(0);
        notes.Created.Should().HaveCount(1);
        notes.Created[0].Body.Should().Be("첫 생성 본문");
    }

    [Fact]
    public void Copy_sends_current_report_text_to_clipboard()
    {
        var (vm, svc, _, _, _, _, clip, _) = CreateSut();
        svc.RenderResult = "복사 대상 본문";
        vm.GenerateCommand.Execute(null);

        vm.CopyCommand.Execute(null);

        clip.SetCount.Should().Be(1);
        clip.LastText.Should().Be("복사 대상 본문");
    }

    [Fact]
    public void Changing_format_loads_existing_report_for_that_format()
    {
        var (vm, _, notes, _, _, _, _, _) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
        notes.Notes.Add(new Note
        {
            Id = 91,
            Type = NoteType.WeeklyReport,
            ReportFormat = ReportFormatKind.B,
            ReportWeekStart = new DateOnly(2026, 6, 22),
            Body = "B 양식 기존 본문",
        });

        vm.SelectedFormat = ReportFormatKind.B;

        vm.ReportText.Should().Be("B 양식 기존 본문");
    }

    [Fact]
    public void Changing_format_clears_text_when_no_existing_report()
    {
        var (vm, svc, _, _, _, _, _, _) = CreateSut();
        svc.RenderResult = "A 본문";
        vm.GenerateCommand.Execute(null);
        vm.ReportText.Should().Be("A 본문");

        vm.SelectedFormat = ReportFormatKind.B;

        vm.ReportText.Should().BeEmpty();
    }
}

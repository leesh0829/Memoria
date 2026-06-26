using System;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelEditorHostTests
{
    // M9 신규 파라미터를 포함해 MainViewModel을 구성한다.
    // (M3~M8에서 생성자가 더 늘었다면, 기존 파라미터를 보존한 현재 시그니처에 맞춰 이 헬퍼를 갱신한다.)
    internal static (MainViewModel vm, FakeNoteRepository notes, FakeGroupRepository groups, FakeSearchService search)
        Build()
    {
        var groups = new FakeGroupRepository();
        var notes = new FakeNoteRepository();
        var search = new FakeSearchService();
        var time = new FakeTimeProvider();
        var autosave = new Memoria.App.Services.DebounceAutosaveService(time, 500);
        var recovery = new FakeRecoveryJournal();

        Func<ChecklistViewModel> checklistFactory = () =>
            new ChecklistViewModel(new FakeChecklistRepo(), new FakeClientRepo(),
                new FakeTagging(), notes, groups);
        Func<WeeklyReportViewModel> weeklyFactory = () =>
            new WeeklyReportViewModel(new FakeWeeklyReportService(), new FakeWeekCalc(), notes,
                new FakeClientRepo(), groups, new FakeSettings(), new FakeClipboard(),
                new FakeConfirm(), time);

        var vm = new MainViewModel(groups, notes, autosave, recovery, time,
            search, checklistFactory, weeklyFactory);
        return (vm, notes, groups, search);
    }

    private static NoteListItemViewModel Item(int id) =>
        new NoteListItemViewModel(id, "t", false, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Selecting_plain_note_hosts_main_view_model_as_editor()
    {
        var (vm, notes, _, _) = Build();
        notes.Create(new Note { Type = NoteType.Plain, Body = "b",
            CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });

        vm.SelectedNote = Item(1);

        vm.CurrentNoteType.Should().Be(NoteType.Plain);
        vm.CurrentEditor.Should().BeSameAs(vm);     // plain 템플릿은 MainViewModel 자신에 바인딩
        vm.IsEditorVisible.Should().BeTrue();
    }

    [Fact]
    public void Selecting_checklist_note_hosts_checklist_view_model()
    {
        var (vm, notes, _, _) = Build();
        notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 26),
            CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });

        vm.SelectedNote = Item(1);

        vm.CurrentNoteType.Should().Be(NoteType.Checklist);
        vm.CurrentEditor.Should().BeOfType<ChecklistViewModel>();
    }

    [Fact]
    public void Selecting_weekly_report_note_hosts_weekly_report_view_model()
    {
        var (vm, notes, _, _) = Build();
        notes.Create(new Note { Type = NoteType.WeeklyReport, ReportFormat = ReportFormatKind.B,
            ReportWeekStart = new DateOnly(2026, 6, 22),
            CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });

        vm.SelectedNote = Item(1);

        vm.CurrentNoteType.Should().Be(NoteType.WeeklyReport);
        vm.CurrentEditor.Should().BeOfType<WeeklyReportViewModel>();
    }

    [Fact]
    public void Clearing_selection_clears_editor()
    {
        var (vm, notes, _, _) = Build();
        notes.Create(new Note { Type = NoteType.Plain, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });
        vm.SelectedNote = Item(1);

        vm.SelectedNote = null;

        vm.CurrentEditor.Should().BeNull();
        vm.IsEditorVisible.Should().BeFalse();
    }
}

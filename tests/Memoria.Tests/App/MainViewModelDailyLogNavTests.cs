using System;
using System.Linq;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelDailyLogNavTests
{
    private static (MainViewModel vm, Memoria.Tests.App.Fakes.FakeNoteRepository notes) Setup()
    {
        var (vm, notes, groups, _) = MainViewModelEditorHostTests.Build();
        groups.Items.Add(new Group { Name = ChecklistViewModel.DailyLogGroupName, IsSystem = true, SortOrder = 100 });
        groups.Items[0].Id = 1;
        vm.LoadGroups();
        return (vm, notes);
    }

    [Fact]
    public void OpenChecklistForDate_navigates_to_existing_and_creates_nothing()
    {
        var (vm, notes) = Setup();
        notes.Create(new Note { Type = NoteType.Checklist, GroupId = 1, LogDate = new DateOnly(2026, 7, 6) });
        var countBefore = notes.Items.Count;

        vm.OpenChecklistForDate(new DateOnly(2026, 7, 6));

        notes.Items.Count.Should().Be(countBefore);          // 생성 없음
        vm.SelectedNote.Should().NotBeNull();
        vm.SelectedNote!.Id.Should().Be(notes.Items[0].Id);
        vm.CurrentEditor.Should().BeOfType<ChecklistViewModel>();
    }

    [Fact]
    public void OpenChecklistForDate_absent_shows_draft_without_creating()
    {
        var (vm, notes) = Setup();
        vm.NewChecklistCommand.Execute(null);                // 오늘 것 하나 열림(에디터 호스팅)
        var countBefore = notes.Items.Count;

        vm.OpenChecklistForDate(new DateOnly(2030, 1, 1));   // 없음 → draft

        notes.Items.Count.Should().Be(countBefore);          // 노트 생성 안 함
        vm.SelectedNote.Should().BeNull();                   // 목록에 해당 row 없음
        vm.CurrentEditor.Should().BeOfType<ChecklistViewModel>();  // 빈 draft 에디터
    }

    [Fact]
    public void Materialize_from_draft_adds_row_selects_and_keeps_same_editor()
    {
        var (vm, notes) = Setup();
        vm.NewChecklistCommand.Execute(null);
        vm.OpenChecklistForDate(new DateOnly(2030, 1, 1));   // draft
        var draftEditor = vm.CurrentEditor;                  // 이 인스턴스가 유지돼야 함

        ((ChecklistViewModel)vm.CurrentEditor!).AddTask();   // 첫 항목 → materialize

        var created = notes.Items.Single(n => n.LogDate == new DateOnly(2030, 1, 1));
        vm.SelectedNote.Should().NotBeNull();
        vm.SelectedNote!.Id.Should().Be(created.Id);
        vm.CurrentEditor.Should().BeSameAs(draftEditor);     // 재생성 없이 포커스 보존
    }
}

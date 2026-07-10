using System.Linq;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelNewChecklistTests
{
    [Fact]
    public void NewChecklist_creates_checklist_note_in_daily_log_system_group_and_selects_it()
    {
        var (vm, notes, groups, _) = MainViewModelEditorHostTests.Build();
        groups.Items.Add(new Group { Name = ChecklistViewModel.DailyLogGroupName, IsSystem = true, SortOrder = 100 });
        groups.Items[0].Id = 1;          // FakeGroupRepository.Items은 명시 Id로 검증
        vm.LoadGroups();

        vm.NewChecklistCommand.Execute(null);

        notes.Items.Should().ContainSingle();
        var created = notes.Items[0];
        created.Type.Should().Be(NoteType.Checklist);
        created.GroupId.Should().Be(1);                       // 시스템 그룹 '일일업무일지'
        created.LogDate.Should().NotBeNull();                 // 기본 log_date = 오늘

        vm.SelectedNote.Should().NotBeNull();
        vm.SelectedNote!.Id.Should().Be(created.Id);
        vm.CurrentEditor.Should().BeOfType<ChecklistViewModel>();
    }

    [Fact]
    public void NewChecklist_twice_reuses_todays_note_no_duplicate()
    {
        var (vm, notes, groups, _) = MainViewModelEditorHostTests.Build();
        groups.Items.Add(new Group { Name = ChecklistViewModel.DailyLogGroupName, IsSystem = true, SortOrder = 100 });
        groups.Items[0].Id = 1;
        vm.LoadGroups();

        vm.NewChecklistCommand.Execute(null);
        var firstId = vm.SelectedNote!.Id;
        vm.NewChecklistCommand.Execute(null);   // 오늘 것 발견 → 재생성 안 함

        notes.Items.Count(n => n.Type == NoteType.Checklist).Should().Be(1);
        vm.SelectedNote!.Id.Should().Be(firstId);
    }

    [Fact]
    public void LoadNotes_suffixes_duplicate_dated_checklists()
    {
        var (vm, notes, groups, _) = MainViewModelEditorHostTests.Build();
        groups.Items.Add(new Group { Name = ChecklistViewModel.DailyLogGroupName, IsSystem = true, SortOrder = 100 });
        groups.Items[0].Id = 1;
        vm.LoadGroups();
        // 같은 날짜 체크리스트 2개(레거시) — App/Fakes는 Create 시 id = Items.Count+1
        notes.Create(new Note { Type = NoteType.Checklist, GroupId = 1, LogDate = new DateOnly(2026, 7, 6) });
        notes.Create(new Note { Type = NoteType.Checklist, GroupId = 1, LogDate = new DateOnly(2026, 7, 6) });

        vm.SelectedNode = vm.SystemNodes.First(n => n.GroupId == 1);   // 일일업무일지 선택 → LoadNotes

        var titles = vm.Notes.Select(n => n.DisplayTitle).ToList();
        titles.Should().Contain("2026-07-06 (월)");
        titles.Should().Contain("2026-07-06 (월) (2)");
    }
}

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
}

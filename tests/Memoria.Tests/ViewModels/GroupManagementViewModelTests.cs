using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.Fakes;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class GroupManagementViewModelTests
{
    private static (GroupManagementViewModel vm, FakeGroupRepository groups, FakeNoteRepository notes) CreateSut()
    {
        var groups = new FakeGroupRepository();
        var notes = new FakeNoteRepository();
        var vm = new GroupManagementViewModel(groups, notes);
        return (vm, groups, notes);
    }

    [Fact]
    public void Load_populates_groups_in_sort_order()
    {
        var (vm, groups, _) = CreateSut();
        groups.Create(new Group { Name = "개인", SortOrder = 2, IsSystem = false });
        groups.Create(new Group { Name = "업무", SortOrder = 1, IsSystem = false });
        groups.Create(new Group { Name = "일일업무일지", SortOrder = 0, IsSystem = true });

        vm.Load();

        vm.Groups.Select(g => g.Name).Should().Equal("일일업무일지", "업무", "개인");
    }

    [Fact]
    public void AddGroup_persists_non_system_group_with_next_sort_order()
    {
        var (vm, groups, _) = CreateSut();
        groups.Create(new Group { Name = "업무", SortOrder = 0, IsSystem = false });
        vm.Load();

        vm.AddGroup("신규 프로젝트");

        var created = groups.Items.Single(g => g.Name == "신규 프로젝트");
        created.IsSystem.Should().BeFalse();
        created.SortOrder.Should().Be(1);
        created.Color.Should().NotBeNull();
        vm.Groups.Should().Contain(g => g.Name == "신규 프로젝트");
    }

    [Fact]
    public void AddGroup_into_empty_list_uses_sort_order_zero()
    {
        var (vm, groups, _) = CreateSut();
        vm.Load();

        vm.AddGroup("첫 그룹");

        groups.Items.Single().SortOrder.Should().Be(0);
    }

    [Fact]
    public void RenameGroup_updates_user_group_name()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "업무", SortOrder = 0, IsSystem = false });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.RenameGroup("업무(수정)");

        groups.Get(id)!.Name.Should().Be("업무(수정)");
    }

    [Fact]
    public void RenameGroup_is_disabled_for_system_group()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "주간보고", SortOrder = 0, IsSystem = true });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.RenameGroupCommand.CanExecute("x").Should().BeFalse();

        // 직접 호출해도 시스템 그룹은 변경되지 않는다.
        vm.RenameGroup("변경시도");
        groups.Get(id)!.Name.Should().Be("주간보고");
    }

    [Fact]
    public void RenameGroup_is_enabled_for_user_group()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "개인", SortOrder = 0, IsSystem = false });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.RenameGroupCommand.CanExecute("x").Should().BeTrue();
    }

    [Fact]
    public void SetGroupColor_persists_color()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "업무", SortOrder = 0, IsSystem = false });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.SetGroupColor("#FF5722");

        groups.Get(id)!.Color.Should().Be("#FF5722");
    }

    [Fact]
    public void SetGroupColor_is_allowed_for_system_group()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "일일업무일지", SortOrder = 0, IsSystem = true });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.SetGroupColorCommand.CanExecute("#000000").Should().BeTrue();
        vm.SetGroupColor("#000000");
        groups.Get(id)!.Color.Should().Be("#000000");
    }

    [Fact]
    public void SetGroupColor_is_disabled_without_selection()
    {
        var (vm, _, _) = CreateSut();
        vm.Load();

        vm.SetGroupColorCommand.CanExecute("#000000").Should().BeFalse();
    }
}

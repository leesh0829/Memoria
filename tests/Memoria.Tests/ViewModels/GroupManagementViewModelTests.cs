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
}

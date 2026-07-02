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

    [Fact]
    public void DeleteGroup_removes_user_group()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "업무", SortOrder = 0, IsSystem = false });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.DeleteGroup();

        groups.Get(id).Should().BeNull();
        vm.Groups.Should().NotContain(g => g.Id == id);
    }

    [Fact]
    public void DeleteGroup_is_disabled_for_system_group()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "일일업무일지", SortOrder = 0, IsSystem = true });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.DeleteGroupCommand.CanExecute(null).Should().BeFalse();

        vm.DeleteGroup();
        groups.Get(id).Should().NotBeNull();
    }

    [Fact]
    public void MoveGroup_reorders_and_reassigns_sort_order()
    {
        var (vm, groups, _) = CreateSut();
        groups.Create(new Group { Name = "A", SortOrder = 0 });
        groups.Create(new Group { Name = "B", SortOrder = 1 });
        groups.Create(new Group { Name = "C", SortOrder = 2 });
        vm.Load();

        vm.MoveGroup(0, 2); // A를 맨 뒤로

        vm.Groups.Select(g => g.Name).Should().Equal("B", "C", "A");
        groups.Items.Single(g => g.Name == "B").SortOrder.Should().Be(0);
        groups.Items.Single(g => g.Name == "C").SortOrder.Should().Be(1);
        groups.Items.Single(g => g.Name == "A").SortOrder.Should().Be(2);
    }

    [Fact]
    public void MoveGroup_ignores_out_of_range_or_noop()
    {
        var (vm, groups, _) = CreateSut();
        groups.Create(new Group { Name = "A", SortOrder = 0 });
        groups.Create(new Group { Name = "B", SortOrder = 1 });
        vm.Load();

        vm.MoveGroup(0, 0);   // no-op
        vm.MoveGroup(-1, 1);  // 범위 밖
        vm.MoveGroup(0, 5);   // 범위 밖

        vm.Groups.Select(g => g.Name).Should().Equal("A", "B");
    }

    [Fact]
    public void MoveNoteToGroup_changes_group_without_touching_updated_at()
    {
        var (vm, _, notes) = CreateSut();
        var fixedClock = new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero));
        notes.Clock = fixedClock;
        var noteId = notes.Create(new Memoria.Core.Models.Note
        {
            Type = Memoria.Core.Models.NoteType.Plain,
            Title = "메모",
            GroupId = null
        });
        var originalUpdatedAt = notes.Get(noteId)!.UpdatedAt;

        // 시간이 흐른 뒤 이동해도 updated_at은 그대로여야 한다.
        fixedClock.Advance(TimeSpan.FromHours(5));
        vm.MoveNoteToGroup(noteId, 42);

        var moved = notes.Get(noteId)!;
        moved.GroupId.Should().Be(42);
        moved.UpdatedAt.Should().Be(originalUpdatedAt);
    }

    [Fact]
    public void MoveNoteToGroup_to_unclassified_sets_group_null()
    {
        var (vm, _, notes) = CreateSut();
        var noteId = notes.Create(new Memoria.Core.Models.Note
        {
            Type = Memoria.Core.Models.NoteType.Plain,
            GroupId = 7
        });

        vm.MoveNoteToGroup(noteId, null);

        notes.Get(noteId)!.GroupId.Should().BeNull();
    }

    [Fact]
    public void AddSubGroup_CreatesUnderParent()
    {
        var repo = new FakeGroupRepository();
        var vm = new GroupManagementViewModel(repo, new FakeNoteRepository());
        var parent = repo.Create(new Group { Name = "부모" });

        vm.AddSubGroup(parent, "자식");

        repo.GetAll().Should().Contain(g => g.Name == "자식" && g.ParentId == parent);
    }
}

using System;
using System.Linq;
using FluentAssertions;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelSidebarTests
{
    private static MainViewModel NewVm(FakeGroupRepository g, FakeNoteRepository n)
    {
        var time = new FakeTimeProvider();
        return new MainViewModel(g, n,
            new DebounceAutosaveService(time, 500),
            new FakeRecoveryJournal(),
            time,
            new FakeSearchService(),
            M9EditorFakes.ChecklistFactory(n, g),
            M9EditorFakes.WeeklyFactory(n, g, time));
    }

    [Fact]
    public void LoadGroups_puts_userGroups_then_unclassified_in_sidebar_and_systemGroups_separately()
    {
        var groups = new FakeGroupRepository();
        groups.Create(new Group { Name = "업무", IsSystem = false, SortOrder = 1 });
        groups.Create(new Group { Name = "개인", IsSystem = false, SortOrder = 2 });
        groups.Create(new Group { Name = "일일업무일지", IsSystem = true, SortOrder = 10 });
        groups.Create(new Group { Name = "주간보고", IsSystem = true, SortOrder = 11 });
        var vm = NewVm(groups, new FakeNoteRepository());

        vm.LoadGroups();

        // 위 목록: 사용자 그룹(루트) + (미분류) — 시스템 그룹은 포함하지 않는다(#5).
        vm.SidebarNodes.Select(n => n.Name).Should().ContainInOrder("업무", "개인", "(미분류)");
        vm.SidebarNodes.Should().HaveCount(3);
        vm.SidebarNodes[2].Kind.Should().Be(SidebarNodeKind.Unclassified);
        vm.SidebarNodes[2].GroupId.Should().BeNull();

        // 아래 고정 목록: 시스템 그룹만.
        vm.SystemNodes.Select(n => n.Name).Should().ContainInOrder("일일업무일지", "주간보고");
        vm.SystemNodes.Should().OnlyContain(n => n.Kind == SidebarNodeKind.System);
    }

    [Fact]
    public void LoadGroups_BuildsTree_RootsAndChildren_PlusUnclassified()
    {
        var groups = new FakeGroupRepository();
        var p = groups.Create(new Group { Name = "부모", SortOrder = 0 });
        groups.Create(new Group { Name = "자식", ParentId = p, SortOrder = 0 });
        var vm = NewVm(groups, new FakeNoteRepository());

        vm.LoadGroups();

        var root = vm.SidebarNodes.First(n => n.Name == "부모");
        root.Children.Should().ContainSingle(c => c.Name == "자식");
        vm.SidebarNodes.Last().Kind.Should().Be(SidebarNodeKind.Unclassified);
        vm.SidebarNodes.Should().NotContain(n => n.Kind == SidebarNodeKind.System); // 시스템은 SystemNodes로
    }

    [Fact]
    public void LoadGroups_RestoresExpansion_ByGroupId()
    {
        var groups = new FakeGroupRepository();
        var p = groups.Create(new Group { Name = "부모", SortOrder = 0 });
        groups.Create(new Group { Name = "자식", ParentId = p, SortOrder = 0 });
        var vm = NewVm(groups, new FakeNoteRepository());
        vm.LoadGroups();
        vm.SidebarNodes.First(n => n.GroupId == p).IsExpanded = true;

        vm.LoadGroups(); // 재구성

        vm.SidebarNodes.First(n => n.GroupId == p).IsExpanded.Should().BeTrue(); // 펼침 유지
    }

    [Fact]
    public void NavigateToNote_NestedChildGroup_SelectsChildNode_NotUnclassified()
    {
        var groups = new FakeGroupRepository();
        var pId = groups.Create(new Group { Name = "부모", SortOrder = 0 });
        var cId = groups.Create(new Group { Name = "자식", ParentId = pId, SortOrder = 0 });
        var notes = new FakeNoteRepository();
        var now = DateTimeOffset.UtcNow;
        var noteId = notes.Create(new Note { GroupId = cId, Body = "", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(groups, notes);
        vm.LoadGroups();

        vm.NavigateToNote(noteId, cId);

        vm.SelectedNode!.GroupId.Should().Be(cId,
            "NavigateToNote should find the nested child group, not fall back to (미분류)");
        vm.SelectedNote.Should().NotBeNull();
        vm.SelectedNote!.Id.Should().Be(noteId);
    }

    [Fact]
    public void LoadGroups_AfterDelete_SelectsParentGroup()
    {
        var groups = new FakeGroupRepository();
        var pId = groups.Create(new Group { Name = "부모", SortOrder = 0 });
        var cId = groups.Create(new Group { Name = "자식", ParentId = pId, SortOrder = 0 });
        var vm = NewVm(groups, new FakeNoteRepository());
        vm.LoadGroups();

        // 자식 노드를 선택
        var parentNode = vm.SidebarNodes.First(n => n.GroupId == pId);
        parentNode.IsExpanded = true;
        var cNode = parentNode.Children.First(n => n.GroupId == cId);
        vm.SelectedNode = cNode;

        // 자식 삭제 후 LoadGroups에 삭제된 그룹의 부모 id 전달
        groups.Delete(cId);
        vm.LoadGroups(pId);

        // 부모 그룹이 선택돼야 함(미분류가 아님)
        vm.SelectedNode!.GroupId.Should().Be(pId);
    }
}

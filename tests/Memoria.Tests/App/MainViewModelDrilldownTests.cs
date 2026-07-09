using System.Linq;
using FluentAssertions;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelDrilldownTests
{
    private static MainViewModel NewVm(FakeGroupRepository g, FakeNoteRepository n)
    {
        var time = new FakeTimeProvider();
        return new MainViewModel(g, n,
            new DebounceAutosaveService(time, 500),
            new FakeRecoveryJournal(), time, new FakeSearchService(),
            M9EditorFakes.ChecklistFactory(n, g), M9EditorFakes.WeeklyFactory(n, g, time));
    }

    [Fact]
    public void SelectingGroupWithChildren_PopulatesFolders_AndBreadcrumb()
    {
        var g = new FakeGroupRepository();
        var pId = g.Create(new Group { Name = "부모", SortOrder = 0 });
        var cId = g.Create(new Group { Name = "자식", ParentId = pId, SortOrder = 0 });
        var notes = new FakeNoteRepository();
        var now = System.DateTimeOffset.UtcNow;
        notes.Create(new Note { GroupId = pId, Title = "부모메모", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(g, notes);
        vm.LoadGroups();

        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == pId);

        vm.Folders.Select(f => f.GroupId).Should().Equal(cId);       // 하위 그룹 = 폴더 행
        vm.Notes.Select(n => n.DisplayTitle).Should().Contain("부모메모"); // 직속 메모 유지
        vm.Breadcrumb.Select(b => b.Name).Should().Equal("부모");     // 루트→현재 경로
    }

    [Fact]
    public void SelectingLeafGroup_NoFolders()
    {
        var g = new FakeGroupRepository();
        var pId = g.Create(new Group { Name = "부모", SortOrder = 0 });
        var cId = g.Create(new Group { Name = "자식", ParentId = pId, SortOrder = 0 });
        var vm = NewVm(g, new FakeNoteRepository());
        vm.LoadGroups();
        var parent = vm.SidebarNodes.First(n => n.GroupId == pId);
        parent.IsExpanded = true;

        vm.SelectedNode = parent.Children.First(n => n.GroupId == cId);

        vm.Folders.Should().BeEmpty();
        vm.Breadcrumb.Select(b => b.Name).Should().Equal("부모", "자식");
    }

    [Fact]
    public void NavigateToFolder_MovesSelectionAndRebuilds()
    {
        var g = new FakeGroupRepository();
        var pId = g.Create(new Group { Name = "부모", SortOrder = 0 });
        var cId = g.Create(new Group { Name = "자식", ParentId = pId, SortOrder = 0 });
        var vm = NewVm(g, new FakeNoteRepository());
        vm.LoadGroups();
        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == pId);
        var folder = vm.Folders.First(f => f.GroupId == cId);

        vm.NavigateToFolder(folder.Node);

        vm.SelectedNode!.GroupId.Should().Be(cId);
        vm.Breadcrumb.Select(b => b.Name).Should().Equal("부모", "자식");
    }
}

using System.Linq;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelSidebarTests
{
    [Fact]
    public void LoadGroups_orders_userGroups_then_unclassified_then_systemGroups()
    {
        var groups = new FakeGroupRepository();
        groups.Create(new Group { Name = "업무", IsSystem = false, SortOrder = 1 });
        groups.Create(new Group { Name = "개인", IsSystem = false, SortOrder = 2 });
        groups.Create(new Group { Name = "일일업무일지", IsSystem = true, SortOrder = 10 });
        groups.Create(new Group { Name = "주간보고", IsSystem = true, SortOrder = 11 });
        var vm = new MainViewModel(groups);

        vm.LoadGroups();

        vm.SidebarNodes.Select(n => n.Name).Should()
            .ContainInOrder("업무", "개인", "(미분류)", "일일업무일지", "주간보고");
        vm.SidebarNodes[2].Kind.Should().Be(SidebarNodeKind.Unclassified);
        vm.SidebarNodes[2].GroupId.Should().BeNull();
        vm.SidebarNodes[3].Kind.Should().Be(SidebarNodeKind.System);
    }
}

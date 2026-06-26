using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Memoria.Core.Data;

namespace Memoria.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IGroupRepository _groupRepo;

    public ObservableCollection<SidebarNodeViewModel> SidebarNodes { get; } = new();

    [ObservableProperty]
    private SidebarNodeViewModel? selectedNode;

    public MainViewModel(IGroupRepository groupRepo)
    {
        _groupRepo = groupRepo;
    }

    public void LoadGroups()
    {
        SidebarNodes.Clear();
        var groups = _groupRepo.GetAll();

        foreach (var g in groups.Where(g => !g.IsSystem))
            SidebarNodes.Add(new SidebarNodeViewModel(g.Name, g.Id, SidebarNodeKind.Group));

        SidebarNodes.Add(new SidebarNodeViewModel("(미분류)", null, SidebarNodeKind.Unclassified));

        foreach (var g in groups.Where(g => g.IsSystem))
            SidebarNodes.Add(new SidebarNodeViewModel(g.Name, g.Id, SidebarNodeKind.System));
    }
}

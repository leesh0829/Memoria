using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Memoria.App.ViewModels;

public enum SidebarNodeKind { Group, Unclassified, System }

public sealed partial class SidebarNodeViewModel : ObservableObject
{
    public string Name { get; }
    public int? GroupId { get; }
    public SidebarNodeKind Kind { get; }
    public ObservableCollection<SidebarNodeViewModel> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public SidebarNodeViewModel(string name, int? groupId, SidebarNodeKind kind)
    {
        Name = name; GroupId = groupId; Kind = kind;
    }
}

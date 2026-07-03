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

    // 메모를 이 노드로 드래그 중일 때 강조(드롭 대상 하이라이트).
    [ObservableProperty] private bool _isDropTarget;

    public SidebarNodeViewModel(string name, int? groupId, SidebarNodeKind kind)
    {
        Name = name; GroupId = groupId; Kind = kind;
    }
}

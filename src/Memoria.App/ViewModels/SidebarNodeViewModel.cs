using CommunityToolkit.Mvvm.ComponentModel;

namespace Memoria.App.ViewModels;

public enum SidebarNodeKind { Group, Unclassified, System }

public sealed partial class SidebarNodeViewModel : ObservableObject
{
    public string Name { get; }
    public int? GroupId { get; }                 // null = (미분류) 가상 노드
    public SidebarNodeKind Kind { get; }

    public SidebarNodeViewModel(string name, int? groupId, SidebarNodeKind kind)
    {
        Name = name;
        GroupId = groupId;
        Kind = kind;
    }
}

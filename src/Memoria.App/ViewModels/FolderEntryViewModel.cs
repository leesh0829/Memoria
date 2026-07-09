namespace Memoria.App.ViewModels;

/// <summary>가운데 패널의 하위 그룹(폴더) 행. 클릭 시 그 그룹으로 드릴다운.</summary>
public sealed class FolderEntryViewModel
{
    public SidebarNodeViewModel Node { get; }
    public string Name => Node.Name;
    public int? GroupId => Node.GroupId;
    public FolderEntryViewModel(SidebarNodeViewModel node) => Node = node;
}

namespace Memoria.App.ViewModels;

/// <summary>브레드크럼 한 조각. 클릭 시 그 조상으로 이동.</summary>
public sealed class BreadcrumbSegmentViewModel
{
    public SidebarNodeViewModel Node { get; }
    public string Name => Node.Name;
    public BreadcrumbSegmentViewModel(SidebarNodeViewModel node) => Node = node;
}

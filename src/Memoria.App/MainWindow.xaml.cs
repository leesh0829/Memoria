using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Memoria.App.ViewModels;
using Memoria.App.Views;
using Memoria.Core;
using Memoria.Core.Data;

namespace Memoria.App;

public partial class MainWindow : Window
{
    /// 계약 §9.3 — code-behind/이후 마일스톤이 ViewModel에 접근.
    public MainViewModel ViewModel => (MainViewModel)DataContext;

    /// M5: 그룹 CRUD ViewModel (DI에서 Resolve).
    public GroupManagementViewModel GroupVm { get; }

    /// M5: 휴지통 ViewModel (DI에서 Resolve; 싱글턴 — Undo 토스트와 TrashView가 공유).
    public TrashViewModel TrashVm { get; }

    // M6: ISettingsRepository — closeToTray 판단용.
    private readonly ISettingsRepository _settings;

    /// M6: ExitApplication에서 true로 설정하면 closeToTray 여부와 무관하게 실제 종료 허용.
    public bool AllowClose { get; set; }

    // #5 사이드바가 두 ListBox(사용자/시스템)로 나뉘어, 단일 SelectedNode를 양쪽과 동기화한다.
    private bool _syncingSelection;

    // #1 드래그 임계값 판정용 좌클릭 시작점.
    private System.Windows.Point _dragStartPoint;

    public MainWindow(ISettingsRepository settings)
    {
        _settings = settings;
        InitializeComponent();
        GroupVm = AppServices.Resolve<GroupManagementViewModel>();
        TrashVm = AppServices.Resolve<TrashViewModel>();
        GroupVm.Load();

        DataContextChanged += OnDataContextChanged;
    }

    // -----------------------------------------------------------------
    // #5 사이드바 선택 동기화 (TreeView ↔ 시스템 목록, 단일 SelectedNode)
    // -----------------------------------------------------------------

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm) oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is MainViewModel newVm) newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedNode))
            SyncSidebarSelection();
    }

    // 프로그램적 SelectedNode 변경(예: 체크리스트/주간보고 생성)을 TreeView·SystemListBox에 반영.
    private void SyncSidebarSelection()
    {
        _syncingSelection = true;
        var n = ViewModel.SelectedNode;
        // 시스템 목록
        SystemListBox.SelectedItem = (n is { Kind: SidebarNodeKind.System }) ? n : null;
        // 트리: 전체 해제 후 대상 노드 IsSelected=true + 조상 펼침.
        ClearTreeNodeSelection(ViewModel.SidebarNodes);
        if (n is not null && n.Kind != SidebarNodeKind.System)
            SelectTreeNode(n);
        _syncingSelection = false;
    }

    private static void ClearTreeNodeSelection(System.Collections.Generic.IEnumerable<SidebarNodeViewModel> nodes)
    {
        foreach (var n in nodes) { n.IsSelected = false; ClearTreeNodeSelection(n.Children); }
    }

    private void SelectTreeNode(SidebarNodeViewModel target)
    {
        void Walk(System.Collections.Generic.IEnumerable<SidebarNodeViewModel> nodes,
                  System.Collections.Generic.List<SidebarNodeViewModel> path)
        {
            foreach (var n in nodes)
            {
                path.Add(n);
                if (ReferenceEquals(n, target))
                {
                    for (int i = 0; i < path.Count - 1; i++) path[i].IsExpanded = true;
                    target.IsSelected = true;
                    return;
                }
                Walk(n.Children, path);
                path.RemoveAt(path.Count - 1);
            }
        }
        Walk(ViewModel.SidebarNodes, new System.Collections.Generic.List<SidebarNodeViewModel>());
    }

    private void GroupTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingSelection) return;
        if (e.NewValue is SidebarNodeViewModel node)
        {
            _syncingSelection = true;
            SystemListBox.SelectedItem = null;
            _syncingSelection = false;
            ViewModel.SelectedNode = node;
        }
    }

    private void SystemListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (SystemListBox.SelectedItem is SidebarNodeViewModel node)
        {
            ViewModel.SelectedNode = node;
        }
    }

    // -----------------------------------------------------------------
    // M6: closeToTray — X 클릭 시 트레이로 숨기기(HWND 유지)
    // -----------------------------------------------------------------

    protected override void OnClosing(CancelEventArgs e)
    {
        bool closeToTray = bool.Parse(_settings.GetOrDefault(SettingsKeys.CloseToTray, "true"));
        if (closeToTray && !AllowClose)
        {
            e.Cancel = true;
            Hide(); // HWND 유지(파괴 금지) — 단축키·트레이 계속 동작
            return;
        }
        base.OnClosing(e);
    }

    // -----------------------------------------------------------------
    // M9: 검색 결과 선택 → 해당 노트로 이동 (code-behind는 VM 명령 위임만)
    // -----------------------------------------------------------------

    private void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && SearchResultsList.SelectedItem is SearchHit hit)
        {
            vm.OpenSearchHitCommand.Execute(hit);
            SearchResultsList.SelectedItem = null;   // 다음 검색을 위해 선택 해제
        }
    }

    // -----------------------------------------------------------------
    // 그룹 컨텍스트 메뉴 — ContextMenuOpening에서 SelectedGroup 동기화
    // -----------------------------------------------------------------

    private void SidebarItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SidebarNodeViewModel node)
        {
            GroupVm.SelectedGroup = GroupVm.Groups.FirstOrDefault(g => g.Id == node.GroupId);

            // CanExecute에 따라 이름변경/삭제 MenuItem IsEnabled 수동 반영.
            if (fe.ContextMenu is { } cm)
            {
                foreach (var item in cm.Items.OfType<MenuItem>())
                {
                    switch (item.Tag?.ToString())
                    {
                        case "Rename":
                            item.IsEnabled = GroupVm.RenameGroupCommand.CanExecute(null);
                            break;
                        case "Delete":
                            item.IsEnabled = GroupVm.DeleteGroupCommand.CanExecute(null);
                            break;
                    }
                }
            }
        }
    }

    private void OnAddGroupMenuItemClick(object sender, RoutedEventArgs e)
    {
        var name = AskInput("새 그룹 이름", "새 그룹");
        if (name is null) return;
        GroupVm.AddGroup(name);
        ViewModel.LoadGroups();
    }

    private void OnAddSubGroupMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (GroupVm.SelectedGroup is not { } g || g.IsSystem) return;
        var name = AskInput("새 하위 그룹 이름", "새 그룹");
        if (string.IsNullOrWhiteSpace(name)) return;
        GroupVm.AddSubGroup(g.Id, name.Trim());
        ViewModel.LoadGroups();
    }

    private void OnMoveToRootMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (GroupVm.SelectedGroup is not { } g || g.IsSystem || g.ParentId is null) return;
        GroupVm.MoveGroup(g.Id, null, int.MaxValue);
        ViewModel.LoadGroups();
    }

    // #1 툴바 "+ 그룹" — 보이는 그룹 생성 버튼.
    private void OnNewGroupClick(object sender, RoutedEventArgs e)
    {
        var name = AskInput("새 그룹 이름", "새 그룹");
        if (string.IsNullOrWhiteSpace(name)) return;
        GroupVm.AddGroup(name.Trim());
        ViewModel.LoadGroups();
    }

    // #2 메모 삭제 → 휴지통. 코드비하인드에서 sender의 DataContext(노트)를 직접 읽어 VM 명령 실행.
    private void OnDeleteNoteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NoteListItemViewModel note })
            ViewModel.DeleteNoteCommand.Execute(note);
    }

    private void OnRenameGroupMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (GroupVm.SelectedGroup is null) return;
        var name = AskInput("그룹 이름 변경", GroupVm.SelectedGroup.Name);
        if (name is null) return;
        GroupVm.RenameGroup(name);
        ViewModel.LoadGroups();
    }

    private void OnSetColorMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (GroupVm.SelectedGroup is null) return;
        var color = AskInput("색상 (#RRGGBB)", GroupVm.SelectedGroup.Color ?? "#9E9E9E");
        if (color is null) return;
        GroupVm.SetGroupColor(color);
        ViewModel.LoadGroups();
    }

    private void OnDeleteGroupMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (GroupVm.SelectedGroup is null || !GroupVm.DeleteGroupCommand.CanExecute(null)) return;
        var parentId = GroupVm.SelectedGroup?.ParentId;
        GroupVm.DeleteGroupCommand.Execute(null);
        ViewModel.LoadGroups(parentId);
        ViewModel.LoadNotes();
    }

    // -----------------------------------------------------------------
    // 휴지통 뷰 — 별도 창으로 열기
    // -----------------------------------------------------------------

    private void OnShowTrashClick(object sender, RoutedEventArgs e)
    {
        TrashVm.Load();
        var trashWindow = new Window
        {
            Title = "휴지통",
            Width = 600,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };
        trashWindow.SetResourceReference(BackgroundProperty, "Brush.WindowBackground");
        trashWindow.SetResourceReference(ForegroundProperty, "Brush.Foreground");
        trashWindow.Content = new TrashView { DataContext = TrashVm };
        trashWindow.ShowDialog();
        ViewModel.LoadNotes(); // 복원/영구삭제 후 메모 목록 갱신
    }

    // -----------------------------------------------------------------
    // 드래그 — 그룹 3존 재부모 (GroupTree_*) + 메모→그룹 이동 (GroupNode_DropNote)
    // -----------------------------------------------------------------

    private void GroupTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (IsOverButton(e.OriginalSource)) return;
        if (!ExceededDragThreshold(e)) return;
        if (FindDataContext<SidebarNodeViewModel>(e.OriginalSource) is not { Kind: SidebarNodeKind.Group } node) return;
        if (node.GroupId is not int groupId) return;
        DragDrop.DoDragDrop(GroupTree, new DataObject("groupId", groupId), DragDropEffects.Move);
    }

    private void GroupTree_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("groupId")) return;
        e.Effects = ResolveGroupDrop(e, out _, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupTree_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("groupId")) return;
        if (!ResolveGroupDrop(e, out var newParentId, out var index)) return;
        var groupId = (int)e.Data.GetData("groupId");
        GroupVm.MoveGroup(groupId, newParentId, index);
        ViewModel.LoadGroups();
    }

    // 포인터 아래 TreeViewItem → 3존 → (parentId, index). 무효면 false.
    private bool ResolveGroupDrop(DragEventArgs e, out int? newParentId, out int index)
    {
        newParentId = null; index = 0;
        var groupId = (int)e.Data.GetData("groupId");
        var tvi = FindVisualAncestor<System.Windows.Controls.TreeViewItem>(
            GroupTree.InputHitTest(e.GetPosition(GroupTree)) as DependencyObject);
        if (tvi?.DataContext is not SidebarNodeViewModel target) return false;
        if (target.Kind != SidebarNodeKind.Group || target.GroupId is not int targetId) return false;
        if (targetId == groupId) return false;
        if (GroupVm.RepoIsDescendantOf(targetId, groupId)) return false;

        var pos = e.GetPosition(tvi);
        var zone = GroupDropCalculator.ZoneForOffset(pos.Y, tvi.ActualHeight);
        var targetGroup = GroupVm.Groups.FirstOrDefault(g => g.Id == targetId);
        var targetParentId = targetGroup?.ParentId;
        var siblings = GroupVm.Groups
            .Where(g => g.ParentId == targetParentId)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
            .Select(g => g.Id).ToList();
        var targetIndex = siblings.IndexOf(targetId);
        (newParentId, index) = GroupDropCalculator.Resolve(zone, targetId, targetParentId, targetIndex);
        return true;
    }

    /// 메모를 그룹으로 드롭: noteId/targetGroupId 추출 후 위임.
    private void GroupNode_DropNote(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("noteId")) return;
        var noteId = (int)e.Data.GetData("noteId");
        if (((FrameworkElement)sender).DataContext is SidebarNodeViewModel node)
        {
            GroupVm.MoveNoteToGroup(noteId, node.GroupId);
            ViewModel.LoadNotes();   // #10 이동 즉시 반영(다른 그룹으로 옮기면 현재 목록에서 사라짐)
        }
    }

    /// 메모 드래그 시작 (PreviewMouseMove에서 DoDragDrop 호출).
    private void NoteList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        // #1 핵심: 🗑 등 버튼 위에서 시작한 제스처는 드래그가 아니라 '클릭'이므로 드래그를 시작하지 않는다.
        //         (이게 없으면 버튼 클릭 시 미세 이동이 드래그로 잡혀 삭제 클릭이 소실된다.)
        if (IsOverButton(e.OriginalSource)) return;
        if (!ExceededDragThreshold(e)) return;   // 임계값 이상 움직였을 때만 드래그(클릭 보호)
        if (sender is not ListBox lb) return;
        if (lb.SelectedItem is not NoteListItemViewModel note) return;

        DragDrop.DoDragDrop(lb, new DataObject("noteId", note.Id), DragDropEffects.Move);
    }

    // 드래그 임계값 판정을 위해 좌클릭 시작점을 기록(두 리스트 공용).
    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStartPoint = e.GetPosition(null);

    private bool ExceededDragThreshold(MouseEventArgs e)
    {
        var p = e.GetPosition(null);
        return System.Math.Abs(p.X - _dragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance
            || System.Math.Abs(p.Y - _dragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private static bool IsOverButton(object? source)
        => source is DependencyObject d && FindVisualAncestor<System.Windows.Controls.Primitives.ButtonBase>(d) is not null;

    private static T? FindVisualAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    // -----------------------------------------------------------------
    // 비주얼 트리 / DataContext 헬퍼
    // -----------------------------------------------------------------

    private static T? FindDataContext<T>(object? source) where T : class
    {
        var d = source as DependencyObject;
        while (d is not null)
        {
            if (d is FrameworkElement fe && fe.DataContext is T t) return t;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    // -----------------------------------------------------------------
    // 간단한 텍스트 입력 다이얼로그 헬퍼
    // -----------------------------------------------------------------

    private string? AskInput(string title, string initial = "")
    {
        string? result = null;
        var w = new Window
        {
            Title = title,
            Width = 320,
            Height = 110,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ShowInTaskbar = false
        };
        // #2 다크 테마에서 흰 배경 + 흰 글씨가 되지 않도록 창 배경/글자색을 테마색으로(동적).
        w.SetResourceReference(BackgroundProperty, "Brush.WindowBackground");
        w.SetResourceReference(ForegroundProperty, "Brush.Foreground");
        var tb = new TextBox { Text = initial, Margin = new Thickness(8, 8, 8, 4) };
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(4)
        };
        var ok = new Button { Content = "확인", IsDefault = true, Width = 64, Margin = new Thickness(4) };
        var cancel = new Button { Content = "취소", IsCancel = true, Width = 64, Margin = new Thickness(4) };
        ok.Click += (_, _) => { result = tb.Text; w.DialogResult = true; };
        cancel.Click += (_, _) => { w.DialogResult = false; };
        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);
        var stack = new StackPanel();
        stack.Children.Add(tb);
        stack.Children.Add(btnPanel);
        w.Content = stack;
        w.ShowDialog();
        return w.DialogResult == true ? result : null;
    }
}

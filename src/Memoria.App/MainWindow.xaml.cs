using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Memoria.App.ViewModels;
using Memoria.App.Views;
using Memoria.App.Windows;
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

    // N7.2 드래그 어도너 — 그룹 드래그 중에만 존재. try/catch로 실패 시 null 상태 유지.
    private DragAdorner?          _dragAdorner;
    private DropIndicatorAdorner? _dropIndicator;

    // N7.3 스프링로드 펼침 — 접힌 노드 위 700ms 정지 시 자동 펼침.
    private DispatcherTimer?         _springLoadTimer;
    private SidebarNodeViewModel?    _springLoadTarget;

    // 메모(noteId) 드래그 중 하이라이트된 드롭 대상 노드. 한 번에 하나만 강조.
    private SidebarNodeViewModel?    _noteDropTarget;

    // ② 재클릭 해제: 누를 때 이미 선택돼 있었는지 기록(뗄 때 드래그 아니면 해제).
    private bool _wasSelectedOnDown;

    public MainWindow(ISettingsRepository settings)
    {
        _settings = settings;
        InitializeComponent();
        GroupVm = AppServices.Resolve<GroupManagementViewModel>();
        TrashVm = AppServices.Resolve<TrashViewModel>();
        GroupVm.Load();

        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => RestoreColumnWidths();
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
        SaveColumnWidths();
        bool closeToTray = bool.Parse(_settings.GetOrDefault(SettingsKeys.CloseToTray, "true"));
        if (closeToTray && !AllowClose)
        {
            e.Cancel = true;
            Hide(); // HWND 유지(파괴 금지) — 단축키·트레이 계속 동작
            return;
        }
        base.OnClosing(e);
    }

    private void RestoreColumnWidths()
    {
        // 저장은 InvariantCulture로 하므로 복원 파싱도 InvariantCulture로 맞춘다(로케일 무관 왕복).
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var num = System.Globalization.NumberStyles.Float;
        if (double.TryParse(_settings.GetOrDefault(SettingsKeys.UiCol0Width, ""), num, inv, out var w0) && w0 >= 150)
            Col0.Width = new System.Windows.GridLength(w0);
        if (double.TryParse(_settings.GetOrDefault(SettingsKeys.UiCol1Width, ""), num, inv, out var w1) && w1 >= 150)
            Col1.Width = new System.Windows.GridLength(w1);
    }

    private void SaveColumnWidths()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        _settings.Set(SettingsKeys.UiCol0Width, Col0.ActualWidth.ToString(inv));
        _settings.Set(SettingsKeys.UiCol1Width, Col1.ActualWidth.ToString(inv));
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
                        case "ToRoot":
                            item.IsEnabled = GroupVm.SelectedGroup is { IsSystem: false, ParentId: not null };
                            break;
                        case "Sub":
                            item.IsEnabled = GroupVm.SelectedGroup is { IsSystem: false };
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

    private void OnFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: FolderEntryViewModel f })
        {
            ViewModel.NavigateToFolder(f.Node);
            SyncSidebarSelection();
        }
    }

    private void OnBreadcrumbClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: BreadcrumbSegmentViewModel b })
        {
            ViewModel.NavigateToFolder(b.Node);
            SyncSidebarSelection();
        }
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

        // N7.2: Create drag ghost adorner before DoDragDrop (graceful on failure).
        var sourceTvi = FindVisualAncestor<System.Windows.Controls.TreeViewItem>(e.OriginalSource as DependencyObject);
        if (sourceTvi is not null)
            _dragAdorner = DragAdorner.TryCreate(GroupTree, sourceTvi);

        try
        {
            DragDrop.DoDragDrop(GroupTree, new DataObject("groupId", groupId), DragDropEffects.Move);
        }
        finally
        {
            // Cleanup adorners after drag ends (drop, cancel, or exception).
            _dragAdorner?.Remove();
            _dragAdorner = null;
            _dropIndicator?.Remove();
            _dropIndicator = null;
            // N7.3: Cancel any pending spring-load expand.
            _springLoadTimer?.Stop();
            _springLoadTimer  = null;
            _springLoadTarget = null;
        }
    }

    private void GroupTree_DragOver(object sender, DragEventArgs e)
    {
        // 메모(noteId) 드래그: 그룹 재부모 피드백(고스트/인디케이터)이 아니라
        // '메모가 들어갈 그룹' 강조만 갱신한다.
        if (e.Data.GetDataPresent("noteId"))
        {
            UpdateNoteDropTarget(e);
            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent("groupId")) return;

        var valid = ResolveGroupDrop(e, out _, out _);
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;

        // N7.2: Update ghost adorner position.
        try { _dragAdorner?.Update(e.GetPosition(GroupTree)); }
        catch { /* degrade gracefully */ }

        // N7.2: Update drop indicator adorner.
        try
        {
            if (valid)
            {
                var tvi = FindVisualAncestor<System.Windows.Controls.TreeViewItem>(
                    GroupTree.InputHitTest(e.GetPosition(GroupTree)) as DependencyObject);
                if (tvi is not null)
                {
                    var pos  = e.GetPosition(tvi);
                    var zone = GroupDropCalculator.ZoneForOffset(pos.Y, tvi.ActualHeight);
                    _dropIndicator ??= DropIndicatorAdorner.TryCreate(GroupTree);
                    _dropIndicator?.Update(tvi, zone);
                }
                else
                {
                    _dropIndicator?.Clear();
                }
            }
            else
            {
                _dropIndicator?.Clear();
            }
        }
        catch { /* degrade gracefully */ }

        // N7.3: Spring-loaded expand — hover 700ms over a collapsed group node → expand.
        try
        {
            var tvi = FindVisualAncestor<System.Windows.Controls.TreeViewItem>(
                GroupTree.InputHitTest(e.GetPosition(GroupTree)) as DependencyObject);
            var hoverNode = tvi?.DataContext as SidebarNodeViewModel;

            // Only spring-load if: group node, has children, and currently collapsed.
            if (hoverNode is { Kind: SidebarNodeKind.Group, IsExpanded: false } && hoverNode.Children.Count > 0)
            {
                if (!ReferenceEquals(hoverNode, _springLoadTarget))
                {
                    // Moved to a new collapsed node — restart timer.
                    _springLoadTimer?.Stop();
                    _springLoadTarget = hoverNode;
                    var capturedNode = hoverNode;
                    var capturedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                    _springLoadTimer = capturedTimer;
                    capturedTimer.Tick += (_, _) =>
                    {
                        capturedTimer.Stop();
                        if (ReferenceEquals(_springLoadTarget, capturedNode) && !capturedNode.IsExpanded)
                            capturedNode.IsExpanded = true;
                    };
                    capturedTimer.Start();
                }
                // else: same target, timer still running — do nothing.
            }
            else
            {
                // Not a valid spring-load target: cancel any pending expand.
                if (!ReferenceEquals(hoverNode, _springLoadTarget))
                {
                    _springLoadTimer?.Stop();
                    _springLoadTarget = null;
                }
            }
        }
        catch { /* degrade gracefully — spring-load is polish only */ }

        e.Handled = true;
    }

    private void GroupTree_Drop(object sender, DragEventArgs e)
    {
        // N7.2: Remove adorners before processing drop (cleanup even if drop fails).
        _dragAdorner?.Remove();   _dragAdorner   = null;
        _dropIndicator?.Remove(); _dropIndicator = null;

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
        ClearNoteDropTarget();   // 드롭 순간 강조 해제.
        if (!e.Data.GetDataPresent("noteId")) return;
        var noteId = (int)e.Data.GetData("noteId");
        if (((FrameworkElement)sender).DataContext is SidebarNodeViewModel node)
        {
            GroupVm.MoveNoteToGroup(noteId, node.GroupId);
            ViewModel.LoadNotes();   // #10 이동 즉시 반영(다른 그룹으로 옮기면 현재 목록에서 사라짐)
        }
    }

    // 트리 밖으로 벗어나면 강조 해제(자식 요소로의 이동은 무시).
    private void GroupTree_DragLeave(object sender, DragEventArgs e)
    {
        var pos = e.GetPosition(GroupTree);
        if (pos.X < 0 || pos.Y < 0 || pos.X > GroupTree.ActualWidth || pos.Y > GroupTree.ActualHeight)
            ClearNoteDropTarget();
    }

    // 포인터 아래의 그룹/(미분류) 노드를 드롭 대상으로 강조. 한 번에 하나만.
    private void UpdateNoteDropTarget(DragEventArgs e)
    {
        var tvi = FindVisualAncestor<System.Windows.Controls.TreeViewItem>(
            GroupTree.InputHitTest(e.GetPosition(GroupTree)) as DependencyObject);
        var node = tvi?.DataContext as SidebarNodeViewModel;

        // 메모는 사용자 그룹과 (미분류)에만 드롭 가능(시스템 그룹 제외).
        var valid = node is { Kind: SidebarNodeKind.Group or SidebarNodeKind.Unclassified };
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;

        var target = valid ? node : null;
        if (ReferenceEquals(target, _noteDropTarget)) return;   // 변화 없음.
        if (_noteDropTarget is not null) _noteDropTarget.IsDropTarget = false;
        _noteDropTarget = target;
        if (_noteDropTarget is not null) _noteDropTarget.IsDropTarget = true;
    }

    private void ClearNoteDropTarget()
    {
        if (_noteDropTarget is not null) _noteDropTarget.IsDropTarget = false;
        _noteDropTarget = null;
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

        try
        {
            DragDrop.DoDragDrop(lb, new DataObject("noteId", note.Id), DragDropEffects.Move);
        }
        finally
        {
            ClearNoteDropTarget();   // 드롭/취소/예외 무관하게 강조 해제.
        }
    }

    // 드래그 임계값 판정을 위해 좌클릭 시작점을 기록(세 표면 공용).
    // ② 재클릭 해제: 눌린 항목이 이미 선택 상태인지도 함께 기록한다.
    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        // 눌린 항목이 이미 선택 상태인지 판정(메모/그룹 공통).
        _wasSelectedOnDown = false;
        if (FindDataContext<NoteListItemViewModel>(e.OriginalSource) is { } note)
            _wasSelectedOnDown = ReferenceEquals(ViewModel.SelectedNote, note);
        else if (FindDataContext<SidebarNodeViewModel>(e.OriginalSource) is { } node)
            _wasSelectedOnDown = node.IsSelected || ReferenceEquals(ViewModel.SelectedNode, node);
    }

    // ② 재클릭 해제 — 메모 목록
    private void NoteList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_wasSelectedOnDown || ExceededDragThreshold(e)) return;   // 드래그면 해제 안 함
        if (FindDataContext<NoteListItemViewModel>(e.OriginalSource) is { } note
            && ReferenceEquals(ViewModel.SelectedNote, note))
        {
            ViewModel.SelectedNote = null;   // 에디터 닫힘(기존 경로)
            e.Handled = true;
        }
    }

    // ② 재클릭 해제 — 그룹 트리
    private void GroupTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_syncingSelection) return;   // 프로그램적 동기화 중 재클릭 해제 억제(SelectedItemChanged와 일관).
        if (!_wasSelectedOnDown || ExceededDragThreshold(e)) return;
        if (FindDataContext<SidebarNodeViewModel>(e.OriginalSource) is { } node
            && (node.IsSelected || ReferenceEquals(ViewModel.SelectedNode, node)))
        {
            _syncingSelection = true;
            ClearTreeNodeSelection(ViewModel.SidebarNodes);   // 트리 하이라이트 해제
            _syncingSelection = false;
            ViewModel.SelectedNode = null;                    // 가운데 비움(LoadNotes Clear)
            e.Handled = true;
        }
    }

    // ② 재클릭 해제 — 시스템 목록
    private void SystemList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_syncingSelection) return;   // 프로그램적 동기화 중 재클릭 해제 억제(일관).
        if (!_wasSelectedOnDown || ExceededDragThreshold(e)) return;
        if (FindDataContext<SidebarNodeViewModel>(e.OriginalSource) is { } node
            && ReferenceEquals(ViewModel.SelectedNode, node))
        {
            _syncingSelection = true;
            SystemListBox.SelectedItem = null;
            _syncingSelection = false;
            ViewModel.SelectedNode = null;
            e.Handled = true;
        }
    }

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
    // RM6: 마크다운 툴바 — 문법 삽입
    // -----------------------------------------------------------------

    private void OnMdToolbarClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string tag }) return;
        if (FindBodyEditor() is not System.Windows.Controls.TextBox tb) return;
        WrapOrInsert(tb, tag);
    }

    private void OnInsertImageClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentNoteId is not int noteId) return;
        if (FindBodyEditor() is not TextBox tb) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "이미지|*.png;*.jpg;*.jpeg;*.gif;*.bmp|모든 파일|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var att = AppServices.Resolve<Memoria.Core.Attachments.IAttachmentService>();
            var rel = att.SaveFile(noteId, dlg.FileName);
            InsertAtCaret(tb, $"![]({rel})");
        }
        catch { /* 실패 시 무변경 */ }
    }

    private void BodyEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+V 이고 클립보드에 이미지가 있으면 가로채 첨부로 저장 후 마크다운 참조 삽입.
        bool ctrlV = e.Key == Key.V
                     && (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (!ctrlV) return;
        if (sender is not TextBox tb) return;
        if (ViewModel.CurrentNoteId is not int noteId) return;
        if (!Clipboard.ContainsImage()) return;   // 텍스트면 기본 동작 유지

        try
        {
            var src = Clipboard.GetImage();
            if (src is null) return;
            var bytes = EncodePng(src);
            var att = AppServices.Resolve<Memoria.Core.Attachments.IAttachmentService>();
            var rel = att.SaveImage(noteId, bytes, "png");
            InsertAtCaret(tb, $"![]({rel})");
            e.Handled = true;   // 기본 이미지 붙여넣기 억제
        }
        catch { /* 저장 실패 시 본문 무변경 */ }
    }

    private static byte[] EncodePng(System.Windows.Media.Imaging.BitmapSource src)
    {
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static void InsertAtCaret(TextBox tb, string text)
    {
        int at = tb.SelectionStart, len = tb.SelectionLength;
        tb.Text = tb.Text.Remove(at, len).Insert(at, text);
        tb.CaretIndex = at + text.Length;
        tb.Focus();
    }

    // 현재 Plain 에디터의 본문 TextBox를 찾는다(DataTemplate 내부라 이름 직접참조 불가할 수 있음).
    private System.Windows.Controls.TextBox? FindBodyEditor()
        => FindDescendant<System.Windows.Controls.TextBox>(this, "BodyEditor");

    private static void WrapOrInsert(System.Windows.Controls.TextBox tb, string tag)
    {
        int start = tb.SelectionStart, len = tb.SelectionLength;
        string sel = tb.SelectedText ?? "";
        string repl; int caret;
        switch (tag)
        {
            case "bold":    repl = $"**{sel}**";    caret = start + 2 + sel.Length; break;
            case "italic":  repl = $"*{sel}*";      caret = start + 1 + sel.Length; break;
            case "heading": repl = $"# {sel}";      caret = start + 2 + sel.Length; break;
            case "ul":      repl = $"- {sel}";      caret = start + 2 + sel.Length; break;
            case "ol":      repl = $"1. {sel}";     caret = start + 3 + sel.Length; break;
            case "link":    repl = $"[{sel}](url)"; caret = start + repl.Length; break;
            default: return;
        }
        tb.Text = tb.Text.Remove(start, len).Insert(start, repl);
        tb.CaretIndex = caret;
        tb.Focus();
    }

    private static T? FindDescendant<T>(System.Windows.DependencyObject root, string name)
        where T : System.Windows.FrameworkElement
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T fe && fe.Name == name) return fe;
            if (FindDescendant<T>(child, name) is { } found) return found;
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

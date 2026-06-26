using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Memoria.App.ViewModels;
using Memoria.App.Views;

namespace Memoria.App;

public partial class MainWindow : Window
{
    /// 계약 §9.3 — code-behind/이후 마일스톤이 ViewModel에 접근.
    public MainViewModel ViewModel => (MainViewModel)DataContext;

    /// M5: 그룹 CRUD ViewModel (DI에서 Resolve).
    public GroupManagementViewModel GroupVm { get; }

    /// M5: 휴지통 ViewModel (DI에서 Resolve; 싱글턴 — Undo 토스트와 TrashView가 공유).
    public TrashViewModel TrashVm { get; }

    public MainWindow()
    {
        InitializeComponent();
        GroupVm = AppServices.Resolve<GroupManagementViewModel>();
        TrashVm = AppServices.Resolve<TrashViewModel>();
        GroupVm.Load();

        // M5: 삭제/복원 후 메모 목록 자동 갱신 (IsUndoAvailable 변경 감지).
        TrashVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TrashViewModel.IsUndoAvailable))
                Dispatcher.Invoke(() => ViewModel.LoadNotes());
        };
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
        GroupVm.DeleteGroupCommand.Execute(null);
        ViewModel.LoadGroups();
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
        trashWindow.Content = new TrashView { DataContext = TrashVm };
        trashWindow.ShowDialog();
        ViewModel.LoadNotes(); // 복원/영구삭제 후 메모 목록 갱신
    }

    // -----------------------------------------------------------------
    // 드래그 — 그룹 순서변경 (GroupList_Drop) + 메모→그룹 이동 (GroupNode_DropNote)
    // -----------------------------------------------------------------

    /// 그룹 순서변경 드래그 시작 (PreviewMouseMove에서 DoDragDrop 호출).
    private void GroupList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not ListBox lb) return;
        if (lb.SelectedItem is not SidebarNodeViewModel node) return;
        if (node.Kind == SidebarNodeKind.Unclassified) return; // 미분류 노드는 드래그 불가

        var index = lb.Items.IndexOf(node);
        if (index < 0) return;

        DragDrop.DoDragDrop(lb, new DataObject("groupFromIndex", index), DragDropEffects.Move);
    }

    /// 그룹 순서변경: 드롭 시 인덱스 계산 후 위임.
    private void GroupList_Drop(object sender, DragEventArgs e)
    {
        var (from, to) = ResolveDragIndices(sender, e);
        if (from < 0 || to < 0 || from == to) return;
        GroupVm.MoveGroup(from, to);
        ViewModel.LoadGroups();
    }

    /// 메모를 그룹으로 드롭: noteId/targetGroupId 추출 후 위임.
    private void GroupNode_DropNote(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("noteId")) return;
        var noteId = (int)e.Data.GetData("noteId");
        if (((FrameworkElement)sender).DataContext is SidebarNodeViewModel node)
            GroupVm.MoveNoteToGroup(noteId, node.GroupId);
    }

    /// 메모 드래그 시작 (PreviewMouseMove에서 DoDragDrop 호출).
    private void NoteList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not ListBox lb) return;
        if (lb.SelectedItem is not NoteListItemViewModel note) return;

        DragDrop.DoDragDrop(lb, new DataObject("noteId", note.Id), DragDropEffects.Move);
    }

    // -----------------------------------------------------------------
    // 인덱스 계산 헬퍼
    // -----------------------------------------------------------------

    private (int from, int to) ResolveDragIndices(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("groupFromIndex")) return (-1, -1);
        var fromIndex = (int)e.Data.GetData("groupFromIndex");
        if (sender is not ListBox list) return (fromIndex, fromIndex);

        var pos = e.GetPosition(list);
        var toIndex = list.Items.Count - 1;
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item) continue;
            var pt = item.TransformToAncestor(list).Transform(new Point(0, 0));
            if (pos.Y < pt.Y + item.ActualHeight / 2) { toIndex = i; break; }
        }
        return (fromIndex, toIndex);
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

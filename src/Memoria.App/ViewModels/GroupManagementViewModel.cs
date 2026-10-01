using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.App.Services;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public partial class GroupManagementViewModel : ObservableObject
{
    private readonly IGroupRepository _groups;
    private readonly INoteRepository _notes;
    private readonly Dictionary<int, Group> _groupById = new();

    public const string DefaultGroupColor = "#9E9E9E";

    public GroupManagementViewModel(IGroupRepository groups, INoteRepository notes)
    {
        _groups = groups;
        _notes = notes;
    }

    public ObservableCollection<Group> Groups { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameGroupCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetGroupColorCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteGroupCommand))]
    private Group? _selectedGroup;

    public void Load()
    {
        ApplySnapshot(_groups.GetAll());
    }

    public async Task LoadAsync()
    {
        var groups = await Task.Run(() => _groups.GetAll());
        ApplySnapshot(groups);
    }

    private void ApplySnapshot(IReadOnlyList<Group> groups)
    {
        Groups.Clear();
        _groupById.Clear();
        foreach (var g in groups)
        {
            Groups.Add(g);
            _groupById[g.Id] = g;
        }
    }

    private bool CanModifySelected() => SelectedGroup is { IsSystem: false };

    private bool HasSelection() => SelectedGroup is not null;

    [RelayCommand(CanExecute = nameof(CanModifySelected))]
    public void RenameGroup(string newName)
    {
        if (SelectedGroup is null || SelectedGroup.IsSystem) return;
        SelectedGroup.Name = newName;
        _groups.Update(SelectedGroup);
        Load();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void SetGroupColor(string color)
    {
        if (SelectedGroup is null) return;
        SelectedGroup.Color = color;
        _groups.Update(SelectedGroup);
        Load();
    }

    [RelayCommand(CanExecute = nameof(CanModifySelected))]
    public void DeleteGroup()
    {
        if (SelectedGroup is null || SelectedGroup.IsSystem) return;
        _groups.Delete(SelectedGroup.Id); // notes.group_id ON DELETE SET NULL → 메모는 (미분류)로
        Load();
    }

    [RelayCommand]
    public void AddGroup(string name)
    {
        var nextOrder = Groups.Count == 0 ? 0 : Groups.Max(g => g.SortOrder) + 1;
        var group = new Group
        {
            Name = name,
            IsSystem = false,
            SortOrder = nextOrder,
            Color = DefaultGroupColor,
            CreatedAt = DateTimeOffset.UtcNow
        };
        group.Id = _groups.Create(group);
        Load();
    }

    public void AddSubGroup(int parentId, string name)
    {
        var siblings = _groups.GetAll().Where(g => g.ParentId == parentId).ToList();
        var nextOrder = siblings.Count == 0 ? 0 : siblings.Max(g => g.SortOrder) + 1;
        var group = new Group
        {
            Name = name, ParentId = parentId, IsSystem = false,
            SortOrder = nextOrder, Color = DefaultGroupColor, CreatedAt = DateTimeOffset.UtcNow,
        };
        group.Id = _groups.Create(group);
        Load();
    }

    public async Task AddGroupAsync(string name)
    {
        AppLog.Action("UI.GroupAdd", $"parent_id=null existing_count={Groups.Count}");
        using var perf = AppLog.Measure("Group.Add");
        await Task.Run(() =>
        {
            var groups = _groups.GetAll();
            var nextOrder = groups.Count == 0 ? 0 : groups.Max(g => g.SortOrder) + 1;
            _groups.Create(new Group
            {
                Name = name,
                IsSystem = false,
                SortOrder = nextOrder,
                Color = DefaultGroupColor,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        });
        await LoadAsync();
    }

    public async Task AddSubGroupAsync(int parentId, string name)
    {
        AppLog.Action("UI.GroupAdd", $"parent_id={parentId} existing_count={Groups.Count}");
        using var perf = AppLog.Measure("Group.AddSubgroup");
        await Task.Run(() =>
        {
            var siblings = _groups.GetAll().Where(g => g.ParentId == parentId).ToList();
            var nextOrder = siblings.Count == 0 ? 0 : siblings.Max(g => g.SortOrder) + 1;
            _groups.Create(new Group
            {
                Name = name,
                ParentId = parentId,
                IsSystem = false,
                SortOrder = nextOrder,
                Color = DefaultGroupColor,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        });
        await LoadAsync();
    }

    public async Task RenameSelectedGroupAsync(string name)
    {
        var selected = SelectedGroup;
        if (selected is null || selected.IsSystem) return;
        AppLog.Action("UI.GroupRename", $"group_id={selected.Id}");
        using var perf = AppLog.Measure("Group.Rename");
        selected.Name = name;
        await Task.Run(() => _groups.Update(selected));
        await LoadAsync();
    }

    public async Task SetSelectedGroupColorAsync(string color)
    {
        var selected = SelectedGroup;
        if (selected is null) return;
        AppLog.Action("UI.GroupColor", $"group_id={selected.Id}");
        using var perf = AppLog.Measure("Group.SetColor");
        selected.Color = color;
        await Task.Run(() => _groups.Update(selected));
        await LoadAsync();
    }

    public async Task DeleteSelectedGroupAsync()
    {
        var selected = SelectedGroup;
        if (selected is null || selected.IsSystem) return;
        AppLog.Action("UI.GroupDelete", $"group_id={selected.Id} parent_id={selected.ParentId?.ToString() ?? "null"}");
        using var perf = AppLog.Measure("Group.Delete");
        await Task.Run(() => _groups.Delete(selected.Id));
        await LoadAsync();
    }

    public bool RepoIsDescendantOf(int nodeId, int ancestorId)
    {
        // DragOver는 초당 수십 번 호출된다. 여기서 매번 DB 전체 parent map을 읽으면
        // 드래그만 해도 연결/파일 I/O가 폭주하므로 이미 로드된 그룹 스냅샷으로 판정한다.
        var visited = new HashSet<int>();
        var current = nodeId;
        while (_groupById.TryGetValue(current, out var group) && group.ParentId is int parentId)
        {
            if (!visited.Add(current)) break;
            if (parentId == ancestorId) return true;
            current = parentId;
        }
        return false;
    }

    public void MoveNoteToGroup(int noteId, int? targetGroupId)
    {
        var note = _notes.Get(noteId);
        if (note is null) return;
        if (note.GroupId == targetGroupId) return;

        // §7.7: 그룹 이동은 메타 조작 → UpdatedAt을 갱신하지 않고 그대로 저장한다.
        _notes.SetGroup(note.Id, targetGroupId);
    }

    public void MoveGroup(int groupId, int? newParentId, int siblingIndex)
    {
        var self = _groups.Get(groupId);
        if (self is null || self.IsSystem) return;
        if (newParentId is int pid)
        {
            if (pid == groupId || _groups.IsDescendantOf(pid, groupId)) return;
            var parent = _groups.Get(pid);
            if (parent is null || parent.IsSystem) return;
        }
        var siblings = _groups.GetAll()
            .Where(g => g.ParentId == newParentId && g.Id != groupId)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
            .Select(g => g.Id).ToList();
        var index = Math.Clamp(siblingIndex, 0, siblings.Count);
        siblings.Insert(index, groupId);
        _groups.ReorderSiblings(newParentId, siblings);
        Load();
    }

    public async Task MoveGroupAsync(int groupId, int? newParentId, int siblingIndex)
    {
        AppLog.Action("UI.GroupMove", $"group_id={groupId} parent_id={newParentId?.ToString() ?? "null"} sibling_index={siblingIndex}");
        using var perf = AppLog.Measure("Group.Move");
        await Task.Run(() =>
        {
            var self = _groups.Get(groupId);
            if (self is null || self.IsSystem) return;
            if (newParentId is int pid)
            {
                if (pid == groupId || _groups.IsDescendantOf(pid, groupId)) return;
                var parent = _groups.Get(pid);
                if (parent is null || parent.IsSystem) return;
            }
            var siblings = _groups.GetAll()
                .Where(g => g.ParentId == newParentId && g.Id != groupId)
                .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
                .Select(g => g.Id).ToList();
            var index = Math.Clamp(siblingIndex, 0, siblings.Count);
            siblings.Insert(index, groupId);
            _groups.ReorderSiblings(newParentId, siblings);
        });
        await LoadAsync();
    }
}

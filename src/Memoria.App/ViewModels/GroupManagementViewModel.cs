using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public partial class GroupManagementViewModel : ObservableObject
{
    private readonly IGroupRepository _groups;
    private readonly INoteRepository _notes;

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
        Groups.Clear();
        foreach (var g in _groups.GetAll())
            Groups.Add(g);
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

    public void MoveGroup(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Groups.Count) return;
        if (toIndex < 0 || toIndex >= Groups.Count) return;
        if (fromIndex == toIndex) return;

        var item = Groups[fromIndex];
        Groups.RemoveAt(fromIndex);
        Groups.Insert(toIndex, item);

        for (var i = 0; i < Groups.Count; i++)
        {
            if (Groups[i].SortOrder != i)
            {
                Groups[i].SortOrder = i;
                _groups.Update(Groups[i]); // sort_order는 메타 조작
            }
        }
    }
}

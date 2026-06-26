using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IGroupRepository _groupRepo;
    private readonly INoteRepository _noteRepo;
    private readonly TimeProvider _time;

    public ObservableCollection<SidebarNodeViewModel> SidebarNodes { get; } = new();
    public ObservableCollection<NoteListItemViewModel> Notes { get; } = new();

    [ObservableProperty]
    private SidebarNodeViewModel? selectedNode;

    [ObservableProperty]
    private NoteListItemViewModel? selectedNote;

    public MainViewModel(IGroupRepository groupRepo, INoteRepository noteRepo, TimeProvider time)
    {
        _groupRepo = groupRepo;
        _noteRepo = noteRepo;
        _time = time;
    }

    public void LoadGroups()
    {
        SidebarNodes.Clear();
        var groups = _groupRepo.GetAll();

        foreach (var g in groups.Where(g => !g.IsSystem))
            SidebarNodes.Add(new SidebarNodeViewModel(g.Name, g.Id, SidebarNodeKind.Group));

        SidebarNodes.Add(new SidebarNodeViewModel("(미분류)", null, SidebarNodeKind.Unclassified));

        foreach (var g in groups.Where(g => g.IsSystem))
            SidebarNodes.Add(new SidebarNodeViewModel(g.Name, g.Id, SidebarNodeKind.System));
    }

    partial void OnSelectedNodeChanged(SidebarNodeViewModel? value) => LoadNotes();

    public void LoadNotes()
    {
        Notes.Clear();
        if (SelectedNode is null) return;

        var notes = _noteRepo.GetByGroup(SelectedNode.GroupId)
            .OrderByDescending(n => n.Pinned)
            .ThenByDescending(n => n.UpdatedAt);

        foreach (var n in notes)
            Notes.Add(new NoteListItemViewModel(n.Id, NoteTitleResolver.Resolve(n), n.Pinned, n.UpdatedAt));
    }

    [RelayCommand]
    private void NewPlainNote()
    {
        var now = _time.GetUtcNow();
        var note = new Note
        {
            Type = NoteType.Plain,
            GroupId = SelectedNode?.GroupId,
            Title = null,
            Body = "",
            CreatedAt = now,
            UpdatedAt = now,
        };
        _noteRepo.Create(note);
        LoadNotes();
    }

    [RelayCommand]
    private void NewChecklist() { /* M9에서 채움 */ }

    [RelayCommand]
    private void OpenWeeklyReport() { /* M9에서 채움 */ }

    [RelayCommand]
    private void OpenSettings() { /* M7에서 채움 */ }

    [RelayCommand]
    private void Search() { /* M9에서 채움 */ }

    [RelayCommand]
    private void OpenSearchHit(object? hit) { /* M9에서 채움 */ }
}

// src/Memoria.App/ViewModels/ChecklistViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Services;

namespace Memoria.App.ViewModels;

public partial class ChecklistViewModel : ObservableObject
{
    public const string DailyLogGroupName = "일일업무일지";

    private readonly IChecklistRepository _checklist;
    private readonly IClientRepository _clients;
    private readonly ITaggingService _tagging;
    private readonly INoteRepository _notes;
    private readonly IGroupRepository _groups;

    private Note? _note;

    public ObservableCollection<ChecklistItemViewModel> Items { get; } = new();
    public ObservableCollection<Client> AvailableClients { get; } = new();

    [ObservableProperty]
    private DateOnly _logDate;

    public ChecklistViewModel(
        IChecklistRepository checklist,
        IClientRepository clients,
        ITaggingService tagging,
        INoteRepository notes,
        IGroupRepository groups)
    {
        _checklist = checklist;
        _clients = clients;
        _tagging = tagging;
        _notes = notes;
        _groups = groups;
    }

    public void Load(Note note)
    {
        _note = note;
        LogDate = note.LogDate ?? DateOnly.FromDateTime(DateTime.Today);

        AvailableClients.Clear();
        foreach (var client in _clients.GetAll(enabledOnly: true))
            AvailableClients.Add(client);

        Items.Clear();
        foreach (var item in _checklist.GetByNote(note.Id))
            Items.Add(new ChecklistItemViewModel(item));
    }

    [RelayCommand]
    public void AddTask() => AddItem(ItemKind.Task);

    [RelayCommand]
    public void AddIssue() => AddItem(ItemKind.Issue);

    private ChecklistItemViewModel AddItem(ItemKind kind)
    {
        var now = DateTimeOffset.UtcNow;
        var model = new ChecklistItem
        {
            NoteId = _note!.Id,
            Kind = kind,
            Text = "",
            Done = false,
            DoneAt = null,
            ClientId = null,
            IsManual = false,
            SortOrder = NextSortOrder(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        model.Id = _checklist.AddItem(model);

        var vm = new ChecklistItemViewModel(model);
        Items.Add(vm);
        TouchNote();
        return vm;
    }

    [RelayCommand]
    public void RemoveItem(ChecklistItemViewModel item)
    {
        _checklist.DeleteItem(item.Id);
        Items.Remove(item);
        TouchNote();
    }

    private int NextSortOrder() => Items.Count == 0 ? 0 : Items.Max(i => i.SortOrder) + 1;

    /// 콘텐츠 변경 시 부모 Note의 UpdatedAt 갱신(메타 조작 제외).
    private void TouchNote()
    {
        if (_note is null) return;
        _note.UpdatedAt = DateTimeOffset.UtcNow;
        _notes.Update(_note);
    }
}

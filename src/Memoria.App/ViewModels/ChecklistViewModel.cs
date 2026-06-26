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
}

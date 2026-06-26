using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.Core;
using Memoria.Core.Data;

namespace Memoria.App.ViewModels;

public partial class TrashViewModel : ObservableObject
{
    private readonly INoteRepository _notes;
    private readonly ISettingsRepository _settings;
    private readonly TimeProvider _clock;

    public TrashViewModel(INoteRepository notes, ISettingsRepository settings, TimeProvider? clock = null)
    {
        _notes = notes;
        _settings = settings;
        _clock = clock ?? TimeProvider.System;
    }

    public ObservableCollection<TrashItemViewModel> Items { get; } = new();

    public int RetentionDays =>
        int.TryParse(_settings.GetOrDefault(SettingsKeys.TrashRetentionDays, "30"), out var d) ? d : 30;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _isUndoAvailable;

    [ObservableProperty]
    private string? _undoMessage;

    private int _lastDeletedNoteId;

    [RelayCommand]
    public void DeleteNote(int noteId)
    {
        _notes.SoftDelete(noteId);
        _lastDeletedNoteId = noteId;
        IsUndoAvailable = true;
        UndoMessage = "메모를 휴지통으로 옮겼습니다.";
    }

    private bool CanUndo() => IsUndoAvailable;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo()
    {
        if (!IsUndoAvailable) return;
        _notes.Restore(_lastDeletedNoteId);
        IsUndoAvailable = false;
        UndoMessage = null;
    }

    [RelayCommand]
    public void Restore(int noteId)
    {
        _notes.Restore(noteId);
        Load();
    }

    [RelayCommand]
    public void Purge(int noteId)
    {
        _notes.Purge(noteId); // checklist_items CASCADE
        Load();
    }

    public void Load()
    {
        var now = _clock.GetUtcNow();
        var days = RetentionDays;
        Items.Clear();
        foreach (var n in _notes.GetTrash())
            Items.Add(new TrashItemViewModel(n, days, now));
    }
}

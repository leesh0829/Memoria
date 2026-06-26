using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public void Load()
    {
        var now = _clock.GetUtcNow();
        var days = RetentionDays;
        Items.Clear();
        foreach (var n in _notes.GetTrash())
            Items.Add(new TrashItemViewModel(n, days, now));
    }
}

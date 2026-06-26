using System;

namespace Memoria.App.ViewModels;

public sealed class NoteListItemViewModel
{
    public int Id { get; }
    public string DisplayTitle { get; }
    public bool Pinned { get; }
    public DateTimeOffset UpdatedAt { get; }

    public NoteListItemViewModel(int id, string displayTitle, bool pinned, DateTimeOffset updatedAt)
    {
        Id = id;
        DisplayTitle = displayTitle;
        Pinned = pinned;
        UpdatedAt = updatedAt;
    }
}

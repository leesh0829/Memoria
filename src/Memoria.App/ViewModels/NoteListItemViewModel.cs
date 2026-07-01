using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Memoria.App.ViewModels;

public sealed partial class NoteListItemViewModel : ObservableObject
{
    public int Id { get; }
    public bool Pinned { get; }

    // 목록에 표시되는 제목/수정시각은 편집 중 즉시 갱신되어야 하므로 관찰형으로 둔다(#2).
    [ObservableProperty]
    private string _displayTitle;

    [ObservableProperty]
    private DateTimeOffset _updatedAt;

    public NoteListItemViewModel(int id, string displayTitle, bool pinned, DateTimeOffset updatedAt)
    {
        Id = id;
        _displayTitle = displayTitle;
        Pinned = pinned;
        _updatedAt = updatedAt;
    }
}

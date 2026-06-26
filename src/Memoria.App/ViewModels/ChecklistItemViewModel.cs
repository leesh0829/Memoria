// src/Memoria.App/ViewModels/ChecklistItemViewModel.cs
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public partial class ChecklistItemViewModel : ObservableObject
{
    public int Id { get; set; }
    public int NoteId { get; }
    public ItemKind Kind { get; }

    public bool IsTask => Kind == ItemKind.Task;
    public bool ShowCheckbox => IsTask;

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStruck))]
    private bool _done;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnclassified))]
    private int? _clientId;

    public DateTimeOffset? DoneAt { get; set; }
    public bool IsManual { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsStruck => IsTask && Done;
    public bool IsUnclassified => IsTask && ClientId is null;

    /// 텍스트가 사용자 입력으로 바뀌었음을 표시(디바운스 FlushSaves 대상).
    public bool IsDirty { get; set; }

    public ChecklistItemViewModel(ChecklistItem model)
    {
        Id = model.Id;
        NoteId = model.NoteId;
        Kind = model.Kind;
        _text = model.Text;          // 필드 직접 대입 → OnTextChanged 미발생(생성 시 dirty 아님)
        _done = model.Done;
        _clientId = model.ClientId;
        DoneAt = model.DoneAt;
        IsManual = model.IsManual;
        SortOrder = model.SortOrder;
        CreatedAt = model.CreatedAt;
        UpdatedAt = model.UpdatedAt;
    }

    partial void OnTextChanged(string value) => IsDirty = true;

    public ChecklistItem ToModel() => new()
    {
        Id = Id,
        NoteId = NoteId,
        Kind = Kind,
        Text = Text,
        Done = Done,
        DoneAt = DoneAt,
        ClientId = ClientId,
        IsManual = IsManual,
        SortOrder = SortOrder,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
    };
}

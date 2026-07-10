// src/Memoria.App/ViewModels/ChecklistViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
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
    private bool _loading;

    public ObservableCollection<ChecklistItemViewModel> Items { get; } = new();
    public ObservableCollection<Client> AvailableClients { get; } = new();

    // #4 같은 Items를 종류별로 분리한 두 뷰(할 일 / 이슈). 추가/삭제 시 자동 반영.
    public ICollectionView TasksView { get; }
    public ICollectionView IssuesView { get; }

    [ObservableProperty]
    private DateOnly _logDate;

    /// 날짜 변경 요청(MainViewModel이 해당 날짜 체크리스트로 이동). VM은 _note를 직접 바꾸지 않는다.
    public event Action<DateOnly>? NavigateToDateRequested;
    /// draft(빈) 상태에서 첫 항목 추가로 실 노트가 생성됐을 때 그 id.
    public event Action<int>? NoteMaterialized;

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

        TasksView = new ListCollectionView(Items)
            { Filter = o => o is ChecklistItemViewModel { IsTask: true } };
        IssuesView = new ListCollectionView(Items)
            { Filter = o => o is ChecklistItemViewModel { IsTask: false } };
    }

    public void Load(Note note)
    {
        _loading = true;
        try
        {
            _note = note;
            LogDate = note.LogDate ?? DateOnly.FromDateTime(DateTime.Today);  // _loading 가드로 OnLogDateChanged 재영속화 방지

            AvailableClients.Clear();
            foreach (var client in _clients.GetAll(enabledOnly: true))
                AvailableClients.Add(client);

            Items.Clear();
            foreach (var item in _checklist.GetByNote(note.Id))
                Items.Add(new ChecklistItemViewModel(item));
        }
        finally
        {
            _loading = false;
        }
    }

    /// 아직 노트가 없는 날짜의 빈 에디터. 첫 AddItem에서 실 노트를 생성(§lazy-create).
    public void LoadDraft(DateOnly date)
    {
        _loading = true;
        try
        {
            _note = null;
            LogDate = date;                       // _loading 가드로 이벤트 억제
            AvailableClients.Clear();
            foreach (var client in _clients.GetAll(enabledOnly: true))
                AvailableClients.Add(client);
            Items.Clear();
        }
        finally { _loading = false; }
    }

    [RelayCommand]
    public void AddTask() => AddItem(ItemKind.Task);

    [RelayCommand]
    public void AddIssue() => AddItem(ItemKind.Issue);

    private ChecklistItemViewModel AddItem(ItemKind kind)
    {
        if (_note is null)   // draft → 실 노트 생성 후 진행
        {
            _note = CreateChecklistNote(_notes, _groups, LogDate);
            NoteMaterialized?.Invoke(_note.Id);
        }
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
    public void ToggleDone(ChecklistItemViewModel item)
    {
        if (!item.IsTask) return;

        item.Done = !item.Done;
        item.DoneAt = item.Done ? DateTimeOffset.UtcNow : null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        _checklist.UpdateItem(item.ToModel());
        TouchNote();
    }

    [RelayCommand]
    public void RemoveItem(ChecklistItemViewModel item)
    {
        _checklist.DeleteItem(item.Id);
        Items.Remove(item);
        TouchNote();
    }

    /// 디바운스 저장 시점에 호출. dirty(텍스트 변경) 항목만 자동태깅 적용 후 영속화한다.
    /// ApplyAutoTag는 Task & !IsManual 일 때만 ClientId를 재계산한다(Issue/수동보호 항목은 보존).
    public void FlushSaves()
    {
        var dirty = Items.Where(i => i.IsDirty).ToList();
        if (dirty.Count == 0) return;

        foreach (var item in dirty)
        {
            var tagged = _tagging.ApplyAutoTag(item.ToModel());
            item.ClientId = tagged.ClientId;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            _checklist.UpdateItem(item.ToModel());
            item.IsDirty = false;
        }
        TouchNote();
    }

    /// 드롭다운으로 사용자가 고객사를 교정했을 때 호출(item.ClientId는 바인딩으로 이미 갱신됨).
    /// 수동 교정으로 표시하여 이후 자동 재분류로부터 보호한다.
    [RelayCommand]
    public void CommitClient(ChecklistItemViewModel item)
    {
        if (!item.IsTask) return;

        item.IsManual = true;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        _checklist.UpdateItem(item.ToModel());
        TouchNote();
    }

    partial void OnLogDateChanged(DateOnly value)
    {
        if (_loading) return;                    // Load/LoadDraft 초기값 설정은 무시(기존 가드)
        NavigateToDateRequested?.Invoke(value);  // 재-date 쓰기 제거 → 이동 요청만
    }

    /// 새 checklist 메모를 시스템 그룹 '일일업무일지'(M1 시드)에 배치하여 생성한다.
    /// 시스템 그룹이 없으면 GroupId=null(미분류)로 둔다.
    public static Note CreateChecklistNote(INoteRepository notes, IGroupRepository groups, DateOnly logDate)
    {
        var group = groups.GetAll()
            .FirstOrDefault(g => g.IsSystem && g.Name == DailyLogGroupName);

        var now = DateTimeOffset.UtcNow;
        var note = new Note
        {
            GroupId = group?.Id,
            Type = NoteType.Checklist,
            Title = null,
            Body = null,
            LogDate = logDate,
            Pinned = false,
            SortOrder = 0,
            DeletedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        note.Id = notes.Create(note);
        return note;
    }

    /// 항목 순서 변경(드래그 등). sort_order 재부여는 메타 조작이므로 Note.UpdatedAt를 갱신하지 않는다.
    public void MoveItem(ChecklistItemViewModel item, int newIndex)
    {
        var oldIndex = Items.IndexOf(item);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Items.Count) return;

        Items.Move(oldIndex, newIndex);
        Renumber();
    }

    private void Renumber()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].SortOrder != i)
            {
                Items[i].SortOrder = i;
                _checklist.UpdateItem(Items[i].ToModel());   // UpdatedAt 보존(메타)
            }
        }
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

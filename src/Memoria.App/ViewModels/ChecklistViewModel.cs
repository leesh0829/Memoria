// src/Memoria.App/ViewModels/ChecklistViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.App.Services;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
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
    private readonly IClipboardService _clipboard;

    private Note? _note;
    private bool _loading;
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    public ObservableCollection<ChecklistItemViewModel> Items { get; } = new();
    public ObservableCollection<Client> AvailableClients { get; } = new();

    // #4 같은 Items를 종류별로 분리한 두 뷰(할 일 / 이슈). 추가/삭제 시 자동 반영.
    public ICollectionView TasksView { get; }
    public ICollectionView IssuesView { get; }

    [ObservableProperty]
    private DateOnly _logDate;

    // Read(양식 출력) 탭: Write=false / Read=true. Read 진입 시 최신 상태로 출력 렌더.
    [ObservableProperty]
    private bool _isReadMode;

    [ObservableProperty]
    private string _taskOutput = "";

    [ObservableProperty]
    private string _issueOutput = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _loadingMessage = "저장하는 중…";

    [ObservableProperty]
    private string _operationError = "";

    /// 날짜 변경 요청(MainViewModel이 해당 날짜 체크리스트로 이동). VM은 _note를 직접 바꾸지 않는다.
    public event Action<DateOnly>? NavigateToDateRequested;
    /// draft(빈) 상태에서 첫 항목 추가로 실 노트가 생성됐을 때 그 id.
    public event Action<int>? NoteMaterialized;

    public ChecklistViewModel(
        IChecklistRepository checklist,
        IClientRepository clients,
        ITaggingService tagging,
        INoteRepository notes,
        IGroupRepository groups,
        IClipboardService clipboard)
    {
        _checklist = checklist;
        _clients = clients;
        _tagging = tagging;
        _notes = notes;
        _groups = groups;
        _clipboard = clipboard;

        TasksView = new ListCollectionView(Items)
            { Filter = o => o is ChecklistItemViewModel { IsTask: true } };
        IssuesView = new ListCollectionView(Items)
            { Filter = o => o is ChecklistItemViewModel { IsTask: false } };
    }

    public void Load(Note note)
        => ApplyLoad(note, ReadLoadSnapshot(note.Id));

    /// 저장소 조회만 백그라운드에서 실행하고 WPF 컬렉션 반영은 호출한 UI 컨텍스트에서 수행한다.
    /// ObservableCollection/ListCollectionView를 Task.Run 내부에서 변경하면 뷰가 비거나
    /// 스레드 소유권 예외가 발생하므로 두 단계를 명시적으로 분리한다.
    public async Task LoadAsync(Note note)
    {
        var snapshot = await Task.Run(() => ReadLoadSnapshot(note.Id));
        ApplyLoad(note, snapshot);
    }

    private ChecklistLoadSnapshot ReadLoadSnapshot(int noteId)
        => new(
            _clients.GetAll(enabledOnly: true),
            _checklist.GetByNote(noteId));

    private void ApplyLoad(Note note, ChecklistLoadSnapshot snapshot)
    {
        _loading = true;
        try
        {
            _note = note;
            LogDate = note.LogDate ?? DateOnly.FromDateTime(DateTime.Today);  // _loading 가드로 OnLogDateChanged 재영속화 방지

            AvailableClients.Clear();
            foreach (var client in snapshot.Clients)
                AvailableClients.Add(client);

            Items.Clear();
            foreach (var item in snapshot.Items)
                Items.Add(new ChecklistItemViewModel(item));
        }
        finally
        {
            _loading = false;
        }
    }

    private sealed record ChecklistLoadSnapshot(
        IReadOnlyList<Client> Clients,
        IReadOnlyList<ChecklistItem> Items);

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

    public void AddTask() => AddItem(ItemKind.Task);

    public void AddIssue() => AddItem(ItemKind.Issue);

    [RelayCommand]
    private Task AddTaskAsync() => AddItemAsync(ItemKind.Task);

    [RelayCommand]
    private Task AddIssueAsync() => AddItemAsync(ItemKind.Issue);

    // ---- Read(양식 출력) 탭 ----

    [RelayCommand]
    private void SetWriteMode() => IsReadMode = false;

    [RelayCommand]
    private void SetReadMode() => IsReadMode = true;

    [RelayCommand]
    private void CopyTaskOutput() => _clipboard.SetText(TaskOutput ?? "");

    [RelayCommand]
    private void CopyIssueOutput() => _clipboard.SetText(IssueOutput ?? "");

    partial void OnIsReadModeChanged(bool value)
    {
        if (value) BuildOutputs();
    }

    /// Read 탭 진입 시 최신 상태로 렌더. 보류 저장(dirty)을 먼저 확정해 최신 텍스트+자동태깅을 반영한다.
    /// Items(정렬된 원본)를 Kind로 필터해 렌더러에 전달(필터 뷰 아님). 고객사명은 전체(비활성 포함)에서 해석.
    private void BuildOutputs()
    {
        FlushSaves();
        var clientNames = _clients.GetAll().ToDictionary(c => c.Id, c => c.Name);
        var tasks = Items.Where(i => i.IsTask).Select(i => (i.Text, i.ClientId)).ToList();
        var issues = Items.Where(i => !i.IsTask).Select(i => i.Text).ToList();
        TaskOutput = DailyLogRenderer.RenderTasks(tasks, clientNames);
        IssueOutput = DailyLogRenderer.RenderIssues(issues);
    }

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

    private async Task AddItemAsync(ItemKind kind)
    {
        var currentNote = _note;
        var logDate = LogDate;
        var sortOrder = NextSortOrder();
        ChecklistAddResult? result = null;

        var added = await RunBusyAsync("Checklist.AddItem", "항목을 추가하는 중…", async () =>
        {
            result = await RunPersistenceAsync(() =>
            {
                var note = currentNote ?? CreateChecklistNote(_notes, _groups, logDate);
                var now = DateTimeOffset.UtcNow;
                var model = new ChecklistItem
                {
                    NoteId = note.Id,
                    Kind = kind,
                    Text = "",
                    Done = false,
                    DoneAt = null,
                    ClientId = null,
                    IsManual = false,
                    SortOrder = sortOrder,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                model.Id = _checklist.AddItem(model);
                _notes.Touch(note.Id, now);
                note.UpdatedAt = now;
                return new ChecklistAddResult(note, model);
            });
        });

        if (!added || result is null) return;
        var materialized = _note is null;
        _note = result.Note;
        Items.Add(new ChecklistItemViewModel(result.Item));
        if (materialized) NoteMaterialized?.Invoke(result.Note.Id);
    }

    public void ToggleDone(ChecklistItemViewModel item)
    {
        if (!item.IsTask) return;

        item.Done = !item.Done;
        item.DoneAt = item.Done ? DateTimeOffset.UtcNow : null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        _checklist.UpdateItemMetadata(item.ToModel());
        TouchNote();
    }

    public void RemoveItem(ChecklistItemViewModel item)
    {
        _checklist.DeleteItem(item.Id);
        Items.Remove(item);
        TouchNote();
    }

    [RelayCommand]
    private async Task ToggleDoneAsync(ChecklistItemViewModel? item)
    {
        if (item is null || !item.IsTask) return;

        var previousDone = item.Done;
        var previousDoneAt = item.DoneAt;
        var note = _note;
        var now = DateTimeOffset.UtcNow;
        item.Done = !item.Done;
        item.DoneAt = item.Done ? now : null;
        item.UpdatedAt = now;
        var model = item.ToModel();

        var saved = await RunBusyAsync("Checklist.ToggleDone", "완료 상태를 저장하는 중…", async () =>
        {
            await RunPersistenceAsync(() =>
            {
                _checklist.UpdateItemMetadata(model);
                if (note is not null) _notes.Touch(note.Id, now);
            });
        });

        if (saved)
        {
            if (note is not null) note.UpdatedAt = now;
        }
        else
        {
            item.Done = previousDone;
            item.DoneAt = previousDoneAt;
        }
    }

    [RelayCommand]
    private async Task RemoveItemAsync(ChecklistItemViewModel? item)
    {
        if (item is null) return;
        var note = _note;
        var now = DateTimeOffset.UtcNow;

        var removed = await RunBusyAsync("Checklist.RemoveItem", "항목을 삭제하는 중…", async () =>
        {
            await RunPersistenceAsync(() =>
            {
                _checklist.DeleteItem(item.Id);
                if (note is not null) _notes.Touch(note.Id, now);
            });
        });

        if (!removed) return;
        Items.Remove(item);
        if (note is not null) note.UpdatedAt = now;
    }

    /// 디바운스 저장 시점에 호출. dirty(텍스트 변경) 항목만 자동태깅 적용 후 영속화한다.
    /// ApplyAutoTag는 Task & !IsManual 일 때만 ClientId를 재계산한다(Issue/수동보호 항목은 보존).
    public void FlushSaves()
    {
        var dirty = Items.Where(i => i.IsDirty).ToList();
        if (dirty.Count == 0) return;

        var updates = new List<ChecklistItem>(dirty.Count);
        foreach (var item in dirty)
        {
            var tagged = _tagging.ApplyAutoTag(item.ToModel());
            item.ClientId = tagged.ClientId;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            updates.Add(item.ToModel());
            item.IsDirty = false;
        }
        _checklist.UpdateItems(updates);
        TouchNote();
    }

    /// UI 컬렉션에서는 저장할 스냅샷만 캡처하고, 분류/SQLite 작업은 백그라운드에서
    /// 직렬 실행한다. 저장 중 추가 입력이 생기면 최신 dirty 상태를 유지해 다음
    /// 디바운스에서 다시 저장한다.
    public async Task FlushSavesAsync()
    {
        await _flushGate.WaitAsync();
        try
        {
            var pending = Items
                .Where(i => i.IsDirty)
                .Select(i => new PendingChecklistSave(i, i.ToModel()))
                .ToList();
            if (pending.Count == 0) return;

            var note = _note;
            var now = DateTimeOffset.UtcNow;
            try
            {
                await RunPersistenceAsync(() =>
                {
                    foreach (var entry in pending)
                    {
                        var tagged = _tagging.ApplyAutoTag(entry.Model);
                        tagged.UpdatedAt = now;
                    }
                    _checklist.UpdateItems(pending.Select(p => p.Model).ToList());
                    if (note is not null) _notes.Touch(note.Id, now);
                });

                foreach (var entry in pending)
                {
                    if (!string.Equals(entry.ViewModel.Text, entry.Model.Text, StringComparison.Ordinal)) continue;
                    entry.ViewModel.ClientId = entry.Model.ClientId;
                    entry.ViewModel.UpdatedAt = entry.Model.UpdatedAt;
                    entry.ViewModel.IsDirty = false;
                }
                if (note is not null) note.UpdatedAt = now;
                OperationError = "";
            }
            catch (Exception ex)
            {
                OperationError = "저장하지 못했습니다. 입력 내용은 유지되며 다시 저장을 시도합니다.";
                AppLog.Error("Checklist.FlushSaves", ex);
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    /// 드롭다운으로 사용자가 고객사를 교정했을 때 호출(item.ClientId는 바인딩으로 이미 갱신됨).
    /// 수동 교정으로 표시하여 이후 자동 재분류로부터 보호한다.
    public void CommitClient(ChecklistItemViewModel item)
    {
        if (!item.IsTask) return;

        item.IsManual = true;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        _checklist.UpdateItemMetadata(item.ToModel());
        TouchNote();
    }

    [RelayCommand]
    private async Task CommitClientAsync(ChecklistItemViewModel? item)
    {
        if (item is null || !item.IsTask) return;
        var note = _note;
        var now = DateTimeOffset.UtcNow;
        item.IsManual = true;
        item.UpdatedAt = now;
        var model = item.ToModel();

        var saved = await RunBusyAsync("Checklist.CommitClient", "고객사 설정을 저장하는 중…", async () =>
        {
            await RunPersistenceAsync(() =>
            {
                _checklist.UpdateItemMetadata(model);
                if (note is not null) _notes.Touch(note.Id, now);
            });
        });
        if (saved && note is not null) note.UpdatedAt = now;
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
        var updates = new List<ChecklistItem>();
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].SortOrder != i)
            {
                Items[i].SortOrder = i;
                updates.Add(Items[i].ToModel());   // UpdatedAt 보존(메타)
            }
        }
        _checklist.SetSortOrders(updates);
    }

    private int NextSortOrder() => Items.Count == 0 ? 0 : Items.Max(i => i.SortOrder) + 1;

    /// 콘텐츠 변경 시 부모 Note의 UpdatedAt 갱신(메타 조작 제외).
    private void TouchNote()
    {
        if (_note is null) return;
        _note.UpdatedAt = DateTimeOffset.UtcNow;
        _notes.Touch(_note.Id, _note.UpdatedAt);
    }

    private async Task<bool> RunBusyAsync(string operation, string message, Func<Task> action)
    {
        if (IsBusy) return false;
        IsBusy = true;
        LoadingMessage = message;
        OperationError = "";
        try
        {
            using var perf = AppLog.Measure(operation);
            await action();
            return true;
        }
        catch (Exception ex)
        {
            OperationError = "작업을 완료하지 못했습니다. 잠시 후 다시 시도해 주세요.";
            AppLog.Error(operation, ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunPersistenceAsync(Action action)
    {
        await _persistenceGate.WaitAsync();
        try
        {
            if (CanOffloadPersistence()) await Task.Run(action);
            else action();
        }
        finally { _persistenceGate.Release(); }
    }

    private async Task<T> RunPersistenceAsync<T>(Func<T> action)
    {
        await _persistenceGate.WaitAsync();
        try
        {
            return CanOffloadPersistence() ? await Task.Run(action) : action();
        }
        finally { _persistenceGate.Release(); }
    }

    // 실제 앱의 UI Dispatcher에서 호출될 때만 I/O를 작업 스레드로 보낸다.
    // WPF Dispatcher가 없는 단위 테스트에서는 같은 스레드에서 실행해
    // ListCollectionView의 스레드 소유권을 보존한다.
    private static bool CanOffloadPersistence()
        => Application.Current?.Dispatcher is { } dispatcher && dispatcher.CheckAccess();

    private sealed record ChecklistAddResult(Note Note, ChecklistItem Item);
    private sealed record PendingChecklistSave(ChecklistItemViewModel ViewModel, ChecklistItem Model);
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.App.Services;
using Memoria.App.Views;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IGroupRepository _groupRepo;
    private readonly INoteRepository _noteRepo;
    private readonly IAutosaveService _autosave;
    private readonly IRecoveryJournal _recovery;
    private readonly TimeProvider _time;
    private readonly ISearchService _search;
    private readonly Func<ChecklistViewModel> _checklistEditorFactory;
    private readonly Func<WeeklyReportViewModel> _weeklyReportEditorFactory;

    private readonly Func<WorkEntriesViewModel>? _workEntriesFactory;

    private Note? _current;                 // plain 에디터의 현재 노트(헤더/본문 로직용)
    private int? _currentEditorNoteId;      // #6 우측에 호스팅 중인 노트 id(plain/checklist/weekly 공통)
    private bool _suppressDirty;
    private int _busyCount;
    private int _notesLoadGeneration;
    private int _editorLoadGeneration;
    private int _searchGeneration;
    private bool _suppressSelectedNodeLoad;

    public ObservableCollection<SidebarNodeViewModel> SidebarNodes { get; } = new();   // 사용자 그룹 + (미분류)
    public ObservableCollection<SidebarNodeViewModel> SystemNodes { get; } = new();    // #5 시스템 그룹(하단 고정)
    public ObservableCollection<NoteListItemViewModel> Notes { get; } = new();
    public ObservableCollection<SearchHit> SearchResults { get; } = new();
    public ObservableCollection<FolderEntryViewModel> Folders { get; } = new();
    public ObservableCollection<BreadcrumbSegmentViewModel> Breadcrumb { get; } = new();

    [ObservableProperty]
    private SidebarNodeViewModel? selectedNode;

    [ObservableProperty]
    private NoteListItemViewModel? selectedNote;

    [ObservableProperty]
    private NoteType currentNoteType;              // 현재 편집 중 NoteType(M9 뷰 호스팅용)

    [ObservableProperty]
    private object? currentEditor;                 // M9: 현재 호스팅 중인 에디터 VM

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty] private string editorTitle = "";
    [ObservableProperty] private string editorBody = "";
    [ObservableProperty] private string headerText = "";
    [ObservableProperty] private bool isEditorVisible;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string loadingMessage = "불러오는 중…";

    // RM6: 마크다운 에디터 상태
    [ObservableProperty] private MarkdownViewMode viewMode = MarkdownViewMode.Read;
    [ObservableProperty] private string bodyFormat = "plain";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetReadModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetRenderedModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertToMarkdownCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertToMarkdownWithLoadingCommand))]
    private bool markdownEnabled = true;

    public bool IsMarkdown  => MarkdownEnabled && BodyFormat == "markdown";
    public bool ShowMarkdownConversion => MarkdownEnabled && BodyFormat != "markdown";
    public bool ShowRead     => IsMarkdown && ViewMode == MarkdownViewMode.Read;
    public bool ShowRendered => IsMarkdown && ViewMode == MarkdownViewMode.Rendered;
    public bool ShowEdit     => !IsMarkdown || ViewMode == MarkdownViewMode.Edit;   // plain은 항상 편집
    public bool ShowToolbar  => IsMarkdown && ViewMode == MarkdownViewMode.Edit;

    // RM7: 코드비하인드에서 현재 열린 노트 id를 읽어 첨부 저장에 사용.
    public int? CurrentNoteId => _current?.Id;

    partial void OnViewModeChanged(MarkdownViewMode value)
    {
        OnPropertyChanged(nameof(ShowRead));
        OnPropertyChanged(nameof(ShowRendered));
        OnPropertyChanged(nameof(ShowEdit));
        OnPropertyChanged(nameof(ShowToolbar));
    }

    partial void OnBodyFormatChanged(string value)
    {
        OnPropertyChanged(nameof(IsMarkdown));
        OnPropertyChanged(nameof(ShowMarkdownConversion));
        OnPropertyChanged(nameof(ShowRead));
        OnPropertyChanged(nameof(ShowRendered));
        OnPropertyChanged(nameof(ShowEdit));
        OnPropertyChanged(nameof(ShowToolbar));
    }

    partial void OnMarkdownEnabledChanged(bool value)
    {
        // 표시만 변경하고 기존 메모의 BodyFormat과 본문은 보존한다.
        OnBodyFormatChanged(BodyFormat);
        if (!value) ViewMode = MarkdownViewMode.Edit;
    }

    private bool CanUseMarkdown() => MarkdownEnabled;

    [RelayCommand(CanExecute = nameof(CanUseMarkdown))]
    private void SetReadMode()
    {
        if (MarkdownEnabled) ViewMode = MarkdownViewMode.Read;
    }
    [RelayCommand] private void SetEditMode()     => ViewMode = MarkdownViewMode.Edit;
    [RelayCommand(CanExecute = nameof(CanUseMarkdown))]
    private void SetRenderedMode()
    {
        if (MarkdownEnabled) ViewMode = MarkdownViewMode.Rendered;
    }

    [RelayCommand(CanExecute = nameof(CanUseMarkdown))]
    private void ConvertToMarkdown()
    {
        if (!MarkdownEnabled || _current is null || _current.BodyFormat == "markdown") return;

        // 보류 중인 자동저장을 먼저 확정(라이브 편집 보존). _current는 '열 때' 값이라 그대로 쓰면 유실된다.
        _autosave.FlushAll();
        var note = _noteRepo.Get(_current.Id);
        if (note is null) return;
        note.BodyFormat = "markdown";
        note.UpdatedAt = _time.GetUtcNow();
        _noteRepo.Update(note);
        _current = note;

        ViewMode = MarkdownViewMode.Edit;   // BodyFormat 설정보다 먼저 → 전환 순간 미리보기 렌더 방지
        BodyFormat = "markdown";
        UpdateListItemTitle(_current.Id, ResolveLiveTitle());
    }

    [RelayCommand(CanExecute = nameof(CanUseMarkdown))]
    private async Task ConvertToMarkdownWithLoadingAsync()
    {
        if (!MarkdownEnabled || _current is null || _current.BodyFormat == "markdown") return;
        var noteId = _current.Id;
        BeginBusy("마크다운으로 전환하는 중…");
        try
        {
            var note = await Task.Run(() =>
            {
                using var perf = AppLog.Measure("ConvertToMarkdown.Persist", 100);
                _autosave.FlushAll();
                var latest = _noteRepo.Get(noteId);
                if (latest is null) return null;
                latest.BodyFormat = "markdown";
                latest.UpdatedAt = _time.GetUtcNow();
                _noteRepo.Update(latest);
                return latest;
            });
            if (note is null || _current?.Id != noteId) return;

            _current = note;
            ViewMode = MarkdownViewMode.Edit;
            BodyFormat = "markdown";
            UpdateListItemTitle(note.Id, ResolveLiveTitle());
        }
        catch (Exception ex)
        {
            AppLog.Error("ConvertToMarkdown", ex);
        }
        finally
        {
            EndBusy();
        }
    }

    // #3 자동저장 상태 표시("저장 중…" → "저장됨 HH:mm:ss"). 저장 없이 자동 영속됨을 사용자에게 보장.
    [ObservableProperty] private string saveStatus = "";

    // #4 삭제(휴지통)/실행취소 — MainViewModel이 직접 소유(열린 노트 삭제 시 에디터까지 정리).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoDeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoDeleteWithLoadingCommand))]
    private bool isUndoAvailable;

    [ObservableProperty] private string undoMessage = "";
    private int _lastDeletedNoteId;

    public MainViewModel(
        IGroupRepository groupRepo,
        INoteRepository noteRepo,
        IAutosaveService autosave,
        IRecoveryJournal recovery,
        TimeProvider time,
        ISearchService search,
        Func<ChecklistViewModel> checklistEditorFactory,
        Func<WeeklyReportViewModel> weeklyReportEditorFactory,
        Func<WorkEntriesViewModel>? workEntriesFactory = null)
    {
        _workEntriesFactory = workEntriesFactory;
        _groupRepo = groupRepo;
        _noteRepo = noteRepo;
        _autosave = autosave;
        _recovery = recovery;
        _time = time;
        _search = search;
        _checklistEditorFactory = checklistEditorFactory;
        _weeklyReportEditorFactory = weeklyReportEditorFactory;
    }

    public void LoadGroups(int? removedGroupParentId = null)
    {
        // 스냅샷: 펼친 그룹 + 선택
        var expanded = CollectExpanded(SidebarNodes);
        var prevGroupId = SelectedNode?.GroupId;
        var prevKind = SelectedNode?.Kind;
        ApplyGroupSnapshot(_groupRepo.GetAll(), expanded, prevGroupId, prevKind, removedGroupParentId);
    }

    public async Task LoadGroupsAsync(int? removedGroupParentId = null)
    {
        var expanded = CollectExpanded(SidebarNodes);
        var prevGroupId = SelectedNode?.GroupId;
        var prevKind = SelectedNode?.Kind;
        var groups = await Task.Run(() => _groupRepo.GetAll());
        ApplyGroupSnapshot(groups, expanded, prevGroupId, prevKind, removedGroupParentId);
    }

    private void ApplyGroupSnapshot(
        IReadOnlyList<Group> groups,
        HashSet<int> expanded,
        int? prevGroupId,
        SidebarNodeKind? prevKind,
        int? removedGroupParentId)
    {

        SidebarNodes.Clear();
        SystemNodes.Clear();

        // 사용자 그룹 트리 구성(parent_id → children), 형제 sort_order 순.
        var userGroups = groups.Where(g => !g.IsSystem).OrderBy(g => g.SortOrder).ThenBy(g => g.Id).ToList();
        var nodeById = new Dictionary<int, SidebarNodeViewModel>();
        foreach (var g in userGroups)
            nodeById[g.Id] = new SidebarNodeViewModel(g.Name, g.Id, SidebarNodeKind.Group);
        foreach (var g in userGroups)
        {
            var node = nodeById[g.Id];
            if (g.ParentId is int pid && nodeById.TryGetValue(pid, out var parent))
                parent.Children.Add(node);
            else
                SidebarNodes.Add(node);   // 루트
        }
        // (미분류)를 트리 루트로 마지막에.
        SidebarNodes.Add(new SidebarNodeViewModel("(미분류)", null, SidebarNodeKind.Unclassified));

        // 아래 고정 목록: 시스템 그룹(일일업무일지·주간보고) — #5 분리
        foreach (var g in groups.Where(g => g.IsSystem))
            SystemNodes.Add(new SidebarNodeViewModel(g.Name, g.Id, SidebarNodeKind.System));

        // 복원: 펼침
        ApplyExpanded(SidebarNodes, expanded);
        // 복원: 선택
        var target = FindNode(SidebarNodes, prevGroupId, prevKind);
        if (target is null && prevKind == SidebarNodeKind.Group && prevGroupId is int)
        {
            // 삭제된 그룹 → 호출자가 전달한 부모(있으면) 아니면 (미분류).
            // groups 스냅샷은 삭제 후이므로 삭제된 그룹을 다시 조회하지 않는다.
            target = FindNode(SidebarNodes, removedGroupParentId, SidebarNodeKind.Group)
                     ?? SidebarNodes.FirstOrDefault(n => n.Kind == SidebarNodeKind.Unclassified);
        }
        SelectedNode = target;   // OnSelectedNodeChanged → LoadNotes + (code-behind) IsSelected 동기화
    }

    private static HashSet<int> CollectExpanded(IEnumerable<SidebarNodeViewModel> nodes)
    {
        var set = new HashSet<int>();
        void Walk(SidebarNodeViewModel n)
        {
            if (n.IsExpanded && n.GroupId is int id) set.Add(id);
            foreach (var c in n.Children) Walk(c);
        }
        foreach (var n in nodes) Walk(n);
        return set;
    }

    private static void ApplyExpanded(IEnumerable<SidebarNodeViewModel> nodes, HashSet<int> expanded)
    {
        foreach (var n in nodes)
        {
            if (n.GroupId is int id && expanded.Contains(id)) n.IsExpanded = true;
            ApplyExpanded(n.Children, expanded);
        }
    }

    private static SidebarNodeViewModel? FindNode(IEnumerable<SidebarNodeViewModel> nodes, int? groupId, SidebarNodeKind? kind)
    {
        foreach (var n in nodes)
        {
            if (n.Kind == kind && n.GroupId == groupId) return n;
            var hit = FindNode(n.Children, groupId, kind);
            if (hit is not null) return hit;
        }
        return null;
    }

    partial void OnSelectedNodeChanged(SidebarNodeViewModel? value)
    {
        AppLog.Action("UI.SelectGroup", value is null
            ? "none"
            : $"kind={value.Kind} group_id={value.GroupId?.ToString() ?? "null"}");
        IsUndoAvailable = false;   // 그룹 이동 등 다른 동작 시 휴지통 Undo 토스트 자동 해제
        // 업무 내역 진입 시 SelectedNote는 이미 null이다. 그룹 이동으로 목록을 비워도
        // SelectedNoteChanged(null)이 다시 발생하지 않으므로 전체 화면 에디터를 직접 해제한다.
        if (CurrentEditor is WorkEntriesViewModel) ClearEditor();
        BuildFolders();
        BuildBreadcrumb();
        if (_suppressSelectedNodeLoad) return;
        if (CanUseAsyncUi()) _ = LoadNotesAsync(value);
        else LoadNotes();
    }

    partial void OnSelectedNoteChanged(NoteListItemViewModel? value)
    {
        AppLog.Action("UI.SelectNote", value is null ? "none" : $"note_id={value.Id}");
        if (value is null)
        {
            _editorLoadGeneration++;
            ClearEditor();
            return;
        }

        // 이미 우측에 호스팅 중인 노트로의 (프로그램적) 재선택은 에디터를 다시 만들지 않는다.
        // materialize 직후 목록 강조 동기화 및 이미 열린 노트로의 navigate에서 포커스/상태 보존.
        if (value.Id == _currentEditorNoteId) return;

        if (CanUseAsyncUi()) _ = OpenSelectedNoteAsync(value);
        else OpenSelectedNote(value);
    }

    private void OpenSelectedNote(NoteListItemViewModel value)
    {
        _editorLoadGeneration++;

        var note = _noteRepo.Get(value.Id);
        if (note is null) return;

        CurrentNoteType = note.Type;
        CurrentEditor = BuildEditorFor(note);
    }

    private async Task OpenSelectedNoteAsync(NoteListItemViewModel value)
    {
        using var perf = AppLog.Measure("OpenNote");
        var generation = ++_editorLoadGeneration;
        var previousChecklist = CurrentEditor as ChecklistViewModel;
        BeginBusy("메모를 불러오는 중…");
        try
        {
            if (previousChecklist is not null)
            {
                using var flushChecklist = AppLog.Measure("OpenNote.FlushChecklist", 100);
                await previousChecklist.FlushSavesAsync();
            }
            var note = await Task.Run(() =>
            {
                using (AppLog.Measure("OpenNote.FlushAutosave", 100))
                    _autosave.FlushAll();
                using (AppLog.Measure("OpenNote.ReadNote", 100))
                    return _noteRepo.Get(value.Id);
            });
            if (generation != _editorLoadGeneration || SelectedNote?.Id != value.Id || note is null) return;

            object? editor;
            using (AppLog.Measure("OpenNote.BuildEditor", 100))
                editor = await BuildEditorForAsync(note);
            if (generation != _editorLoadGeneration || SelectedNote?.Id != value.Id) return;
            CurrentNoteType = note.Type;
            _currentEditorNoteId = note.Id;
            CurrentEditor = editor;
        }
        catch (Exception ex)
        {
            // 선택 변경 이벤트에서 fire-and-forget으로 호출되므로 여기서 기록하지 않으면
            // 비동기 로딩 실패가 빈 편집기로만 보이고 원인이 로그에 남지 않는다.
            AppLog.Error("OpenNote", ex);
        }
        finally
        {
            EndBusy();
        }
    }

    private void ClearEditor()
    {
        CurrentEditor = null;
        IsEditorVisible = false;
        _currentEditorNoteId = null;
    }

    // NoteType → 에디터 VM 매핑(계약 §11: plain/checklist/weekly_report → 각 View 호스팅).
    private object? BuildEditorFor(Note note)
    {
        if (note.Type != NoteType.Plain && _current is { } previousPlain)
        {
            _autosave.FlushAll();
            _autosave.Unregister(previousPlain.Id);
            _current = null;
        }
        _currentEditorNoteId = note.Id;
        switch (note.Type)
        {
            case NoteType.Plain:
                OpenNote(note);             // 이미 조회한 노트 재사용(중복 DB 읽기 방지)
                return this;                // plain DataTemplate은 MainViewModel 자신에 바인딩
            case NoteType.Checklist:
                var checklist = _checklistEditorFactory();
                checklist.NavigateToDateRequested += OpenChecklistForDate;
                checklist.NoteMaterialized += OnChecklistMaterialized;
                checklist.Load(note);
                return checklist;
            case NoteType.WeeklyReport:
                var weekly = _weeklyReportEditorFactory();
                if (note.ReportWeekStart is DateOnly ws) weekly.SelectedDate = ws;
                if (note.ReportFormat is ReportFormatKind fmt && weekly.SelectedFormat != fmt)
                    weekly.SelectedFormat = fmt;
                else weekly.GenerateCommand.Execute(null);
                return weekly;
            default:
                return null;
        }
    }

    private async Task<object?> BuildEditorForAsync(Note note)
    {
        if (note.Type != NoteType.Plain && _current is { } previousPlain)
        {
            // OpenSelectedNoteAsync에서 이미 백그라운드 flush를 완료했다.
            _autosave.Unregister(previousPlain.Id);
            _current = null;
        }
        switch (note.Type)
        {
            case NoteType.Plain:
                OpenNote(note, flushPending: false);
                return this;
            case NoteType.Checklist:
                var checklist = _checklistEditorFactory();
                checklist.NavigateToDateRequested += OpenChecklistForDateWithLoading;
                checklist.NoteMaterialized += OnChecklistMaterialized;
                await checklist.LoadAsync(note);
                return checklist;
            case NoteType.WeeklyReport:
                var weekly = _weeklyReportEditorFactory();
                // ViewModel 속성 변경은 WPF UI 스레드에 남겨 둔다. 이전 구현은
                // Task.Run 안에서 PropertyChanged를 발생시켜 간헐적인 교차 스레드 오류를 냈다.
                if (note.ReportWeekStart is DateOnly ws) weekly.SelectedDate = ws;
                if (note.ReportFormat is ReportFormatKind fmt && weekly.SelectedFormat != fmt)
                    weekly.SelectedFormat = fmt;
                else
                    await weekly.GenerateInBackgroundCommand.ExecuteAsync(null);
                return weekly;
            default:
                return null;
        }
    }

    public void LoadNotes()
    {
        _notesLoadGeneration++;
        Notes.Clear();
        if (SelectedNode is null) return;

        foreach (var item in ReadNoteList(SelectedNode.GroupId))
            Notes.Add(item);
    }

    private IReadOnlyList<NoteListItemViewModel> ReadNoteList(int? groupId)
    {
        var notes = _noteRepo.GetByGroup(groupId)
            .OrderByDescending(n => n.Pinned)
            .ThenBy(n => n.SortOrder)              // 수동 순서(드래그) 우선; 미지정(0 동률)이면 최근수정순 폴백
            .ThenByDescending(n => n.UpdatedAt)
            .ThenByDescending(n => n.Id)           // GetByGroup SQL/Fake와 동일 정렬(완전 동률 시 id DESC)
            .ToList();

        var titles = NoteTitleResolver.ResolveList(notes);   // 같은 날짜 체크리스트 접미사
        var result = new List<NoteListItemViewModel>(notes.Count);
        for (int i = 0; i < notes.Count; i++)
            result.Add(new NoteListItemViewModel(notes[i].Id, titles[i], notes[i].Pinned, notes[i].UpdatedAt));
        return result;
    }

    private async Task LoadNotesAsync(SidebarNodeViewModel? node)
    {
        using var perf = AppLog.Measure("LoadNotes");
        var generation = ++_notesLoadGeneration;
        Notes.Clear();
        if (node is null) return;

        BeginBusy("메모 목록을 불러오는 중…");
        try
        {
            var items = await Task.Run(() => ReadNoteList(node.GroupId));
            if (generation != _notesLoadGeneration || !ReferenceEquals(SelectedNode, node)) return;
            foreach (var item in items) Notes.Add(item);
        }
        catch (Exception ex)
        {
            AppLog.Error("LoadNotes", ex);
        }
        finally
        {
            EndBusy();
        }
    }

    public Task ReloadNotesAsync() => LoadNotesAsync(SelectedNode);

    public async Task MoveNoteToGroupAsync(int noteId, int? groupId)
    {
        AppLog.Action("UI.MoveNote", $"note_id={noteId} target_group_id={groupId?.ToString() ?? "null"}");
        using var perf = AppLog.Measure("MoveNoteToGroup");
        BeginBusy("메모를 이동하는 중…");
        try
        {
            await Task.Run(() =>
            {
                var note = _noteRepo.Get(noteId);
                if (note is null) return;
                _noteRepo.SetGroup(note.Id, groupId);
            });
            await LoadNotesAsync(SelectedNode);
        }
        catch (Exception ex)
        {
            AppLog.Error("MoveNoteToGroup", ex);
        }
        finally
        {
            EndBusy();
        }
    }

    /// 메모 목록 드래그 순서변경: Notes를 이동한 뒤 현재 그룹의 보이는 목록 전체를 0..n-1로
    /// 재번호하여 sort_order를 영속화한다. 첫 드래그 시 baseline(0..n-1)을 확정해 sort_order=0 동률을 해소한다.
    /// 순서변경은 메타 조작 → updated_at 갱신 안 함, LoadNotes 재호출(깜빡임) 안 함.
    public void ReorderNote(int noteId, int newIndex)
    {
        var oldIndex = -1;
        for (int i = 0; i < Notes.Count; i++)
            if (Notes[i].Id == noteId) { oldIndex = i; break; }
        if (oldIndex < 0) return;

        if (newIndex < 0) newIndex = 0;
        if (newIndex > Notes.Count - 1) newIndex = Notes.Count - 1;
        if (oldIndex == newIndex) return;

        Notes.Move(oldIndex, newIndex);
        // 보이는 목록 전체를 재번호. 현재 Pinned은 항상 false(핀 UI 없음)라 경계 문제가 없다.
        // 향후 핀 기능을 추가하면 pinned/unpinned 경계를 넘는 드롭은 pinned DESC 재정렬로 되돌아가므로 클램프가 필요.
        _noteRepo.SetSortOrders(Notes.Select(n => n.Id).ToArray());
    }

    public async Task ReorderNoteAsync(int noteId, int newIndex)
    {
        AppLog.Action("UI.ReorderNote", $"note_id={noteId} target_index={newIndex} count={Notes.Count}");
        var oldIndex = Notes.ToList().FindIndex(n => n.Id == noteId);
        if (oldIndex < 0) return;
        newIndex = Math.Clamp(newIndex, 0, Notes.Count - 1);
        if (oldIndex == newIndex) return;

        Notes.Move(oldIndex, newIndex);
        var orderedIds = Notes.Select(n => n.Id).ToArray();
        try
        {
            using var perf = AppLog.Measure("ReorderNote");
            await Task.Run(() => _noteRepo.SetSortOrders(orderedIds));
        }
        catch (Exception ex)
        {
            AppLog.Error("ReorderNote", ex);
            await LoadNotesAsync(SelectedNode);
        }
    }

    [RelayCommand]
    private void NewPlainNote()
    {
        IsUndoAvailable = false;
        var now = _time.GetUtcNow();
        var note = new Note
        {
            Type = NoteType.Plain,
            // #5 일반 메모는 시스템 그룹(일일업무일지·주간보고)에 들어갈 수 없다 → 사용자 그룹일 때만 배치, 아니면 (미분류).
            GroupId = SelectedNode is { Kind: SidebarNodeKind.Group } ? SelectedNode.GroupId : null,
            Title = null,
            Body = "",
            BodyFormat = MarkdownEnabled ? "markdown" : "plain",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var id = _noteRepo.Create(note);
        NavigateToNote(id, note.GroupId);   // #3 새 메모를 즉시 선택 → 우측 편집창 표시
    }

    [RelayCommand]
    private async Task NewPlainNoteWithLoadingAsync()
    {
        AppLog.Action("UI.NewPlainNote", $"group_id={SelectedNode?.GroupId?.ToString() ?? "null"}");
        IsUndoAvailable = false;
        BeginBusy("새 메모를 만드는 중…");
        try
        {
            var groupId = SelectedNode is { Kind: SidebarNodeKind.Group } ? SelectedNode.GroupId : null;
            var now = _time.GetUtcNow();
            var newBodyFormat = MarkdownEnabled ? "markdown" : "plain";
            var id = await Task.Run(() =>
            {
                using var perf = AppLog.Measure("NewPlainNote.Persist", 100);
                return _noteRepo.Create(new Note
                {
                    Type = NoteType.Plain,
                    GroupId = groupId,
                    Title = null,
                    Body = "",
                    BodyFormat = newBodyFormat,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            });
            await NavigateToNoteAsync(id, groupId);
        }
        catch (Exception ex)
        {
            AppLog.Error("NewPlainNote", ex);
        }
        finally
        {
            EndBusy();
        }
    }

    [RelayCommand]
    private void NewChecklist()
    {
        IsUndoAvailable = false;
        var today = DateOnly.FromDateTime(_time.GetUtcNow().LocalDateTime.Date);

        var existing = _noteRepo.FindChecklistForDate(today);
        if (existing is not null) { NavigateToNote(existing.Id, existing.GroupId); return; }

        var group = _groupRepo.GetAll()
            .FirstOrDefault(g => g.IsSystem && g.Name == ChecklistViewModel.DailyLogGroupName);
        var now = _time.GetUtcNow();
        var note = new Note
        {
            Type = NoteType.Checklist,
            GroupId = group?.Id,
            LogDate = today,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var id = _noteRepo.Create(note);
        NavigateToNote(id, group?.Id);
    }

    [RelayCommand]
    private async Task NewChecklistWithLoadingAsync()
    {
        AppLog.Action("UI.NewChecklist");
        IsUndoAvailable = false;
        BeginBusy("오늘의 업무일지를 불러오는 중…");
        try
        {
            var today = DateOnly.FromDateTime(_time.GetUtcNow().LocalDateTime.Date);
            var destination = await Task.Run(() =>
            {
                using var perf = AppLog.Measure("NewChecklist.Resolve", 100);
                var existing = _noteRepo.FindChecklistForDate(today);
                if (existing is not null) return (existing.Id, existing.GroupId);

                var group = _groupRepo.GetAll()
                    .FirstOrDefault(g => g.IsSystem && g.Name == ChecklistViewModel.DailyLogGroupName);
                var now = _time.GetUtcNow();
                var note = new Note
                {
                    Type = NoteType.Checklist,
                    GroupId = group?.Id,
                    LogDate = today,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                return (_noteRepo.Create(note), group?.Id);
            });
            await NavigateToNoteAsync(destination.Item1, destination.Item2);
        }
        catch (Exception ex)
        {
            AppLog.Error("NewChecklist", ex);
        }
        finally
        {
            EndBusy();
        }
    }

    // 사이드바 노드 선택 → 목록 재로드 → 해당 노트 선택(에디터 호스팅 트리거).
    public void NavigateToNote(int noteId, int? groupId)
    {
        var node = (groupId is int gid ? FindNode(SidebarNodes, gid, SidebarNodeKind.Group) : null)
                   ?? SystemNodes.FirstOrDefault(n => n.GroupId == groupId)   // 체크리스트/주간보고는 시스템 그룹 소속
                   ?? SidebarNodes.FirstOrDefault(n => n.Kind == SidebarNodeKind.Unclassified);
        if (node is not null) SelectedNode = node;

        LoadNotes();   // SelectedNode가 이미 동일하면 OnSelectedNodeChanged가 안 울리므로 명시 재로드
        SelectedNote = Notes.FirstOrDefault(n => n.Id == noteId);
    }

    private async Task NavigateToNoteAsync(int noteId, int? groupId)
    {
        var node = (groupId is int gid ? FindNode(SidebarNodes, gid, SidebarNodeKind.Group) : null)
                   ?? SystemNodes.FirstOrDefault(n => n.GroupId == groupId)
                   ?? SidebarNodes.FirstOrDefault(n => n.Kind == SidebarNodeKind.Unclassified);

        if (node is not null && !ReferenceEquals(SelectedNode, node))
        {
            _suppressSelectedNodeLoad = true;
            try { SelectedNode = node; }
            finally { _suppressSelectedNodeLoad = false; }
        }

        await LoadNotesAsync(SelectedNode);
        SelectedNote = Notes.FirstOrDefault(n => n.Id == noteId);
    }

    // 날짜 선택/이동의 단일 진입점: 보류 저장 확정 → 조회 → 이동(또는 draft).
    public void OpenChecklistForDate(DateOnly date)
    {
        (CurrentEditor as ChecklistViewModel)?.FlushSaves();   // 이전 날짜 dirty 항목 확정(유실 방지)

        var found = _noteRepo.FindChecklistForDate(date);
        if (found is not null) { NavigateToNote(found.Id, found.GroupId); return; }

        LoadChecklistDraft(date);
    }

    private async void OpenChecklistForDateWithLoading(DateOnly date)
    {
        AppLog.Action("UI.NavigateChecklistDate", $"date={date:yyyy-MM-dd}");
        using var perf = AppLog.Measure("Checklist.NavigateDate");
        var checklist = CurrentEditor as ChecklistViewModel;
        BeginBusy("일일업무일지를 불러오는 중…");
        try
        {
            if (checklist is not null) await checklist.FlushSavesAsync();
            var found = await Task.Run(() => _noteRepo.FindChecklistForDate(date));
            if (found is not null) await NavigateToNoteAsync(found.Id, found.GroupId);
            else await LoadChecklistDraftAsync(date);
        }
        catch (Exception ex)
        {
            AppLog.Error("Checklist.NavigateDate", ex);
        }
        finally
        {
            EndBusy();
        }
    }

    // 노트가 없는 날짜: 생성하지 않고 빈 draft 에디터를 호스팅(첫 항목에서 실 노트 생성).
    private void LoadChecklistDraft(DateOnly date)
    {
        SelectedNote = null;                       // 이전 선택/에디터 정리(OnSelectedNoteChanged(null))
        var draft = _checklistEditorFactory();
        if (CanUseAsyncUi()) draft.NavigateToDateRequested += OpenChecklistForDateWithLoading;
        else draft.NavigateToDateRequested += OpenChecklistForDate;
        draft.NoteMaterialized += OnChecklistMaterialized;
        draft.LoadDraft(date);
        CurrentNoteType = NoteType.Checklist;
        CurrentEditor = draft;
        IsEditorVisible = true;
        _currentEditorNoteId = null;               // 아직 실 노트 없음
    }

    private async Task LoadChecklistDraftAsync(DateOnly date)
    {
        SelectedNote = null;
        var draft = _checklistEditorFactory();
        draft.NavigateToDateRequested += OpenChecklistForDateWithLoading;
        draft.NoteMaterialized += OnChecklistMaterialized;
        await draft.LoadDraftAsync(date);
        CurrentNoteType = NoteType.Checklist;
        CurrentEditor = draft;
        IsEditorVisible = true;
        _currentEditorNoteId = null;
    }

    // draft에서 첫 항목 추가로 실 노트가 생겼을 때: 목록 갱신 + 새 row 강조(에디터는 유지).
    private void OnChecklistMaterialized(int id)
    {
        if (CanUseAsyncUi())
        {
            _ = OnChecklistMaterializedAsync(id);
            return;
        }
        var editor = CurrentEditor;
        _currentEditorNoteId = id;                 // SelectedNote 설정 전에 → 가드로 재호스팅 방지
        LoadNotes();                               // 새 날짜 row 등장
        _currentEditorNoteId = id;                 // 실제 ListBox가 선택을 비운 경우 ClearEditor가 초기화할 수 있음
        SelectedNote = Notes.FirstOrDefault(n => n.Id == id);
        CurrentNoteType = NoteType.Checklist;
        CurrentEditor = editor;
        IsEditorVisible = true;
    }

    private async Task OnChecklistMaterializedAsync(int id)
    {
        var editor = CurrentEditor;
        try
        {
            _currentEditorNoteId = id;
            await LoadNotesAsync(SelectedNode);
            _currentEditorNoteId = id;
            SelectedNote = Notes.FirstOrDefault(n => n.Id == id);
            CurrentNoteType = NoteType.Checklist;
            CurrentEditor = editor;
            IsEditorVisible = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Checklist.Materialize", ex);
        }
    }

    // #4 메모 삭제 → 휴지통. 열려 있는(선택 중) 노트면 자동저장 정리 + 우측 에디터를 빈 화면으로.
    [RelayCommand]
    private void DeleteNote(NoteListItemViewModel? item)
    {
        if (item is null) return;

        // 우측에 열려 있는 노트(선택 경로 또는 툴바 주간보고 경로 포함)를 삭제하면 에디터를 비운다(#4·#6).
        if (_currentEditorNoteId == item.Id || _current?.Id == item.Id || SelectedNote?.Id == item.Id)
        {
            // 체크리스트는 지연된 Unloaded flush가 SoftDelete 뒤에 발화하면 deleted_at을 덮어써 부활시킨다.
            // 삭제 전에 미리 flush하여 dirty를 비우면 그 지연 flush가 no-op이 되어 부활을 막는다(좀비 방지).
            (CurrentEditor as ChecklistViewModel)?.FlushSaves();
            _autosave.FlushAll();          // plain 에디터 보류 저장 확정(데이터 보존)
            _autosave.Unregister(item.Id); // 이후 좀비 저장 방지
            _current = null;
            _currentEditorNoteId = null;
            SelectedNote = null;           // OnSelectedNoteChanged(null) → 에디터 비움
            CurrentEditor = null;
            IsEditorVisible = false;
            SaveStatus = "";
        }

        _noteRepo.SoftDelete(item.Id);     // deleted_at 설정(휴지통 이동)
        _lastDeletedNoteId = item.Id;
        Notes.Remove(item);                // 즉시 목록에서 제거(선택/연속 무관 확실)

        IsUndoAvailable = true;
        UndoMessage = "메모를 휴지통으로 옮겼습니다.";
    }

    [RelayCommand]
    private async Task DeleteNoteWithLoadingAsync(NoteListItemViewModel? item)
    {
        if (item is null) return;
        AppLog.Action("UI.DeleteNote", $"note_id={item.Id}");
        using var perf = AppLog.Measure("DeleteNote");
        var wasOpen = _currentEditorNoteId == item.Id || _current?.Id == item.Id || SelectedNote?.Id == item.Id;
        var checklist = wasOpen ? CurrentEditor as ChecklistViewModel : null;

        BeginBusy("메모를 삭제하는 중…");
        try
        {
            if (checklist is not null) await checklist.FlushSavesAsync();
            await Task.Run(() =>
            {
                if (wasOpen)
                {
                    _autosave.FlushAll();
                    _autosave.Unregister(item.Id);
                }
                _noteRepo.SoftDelete(item.Id);
            });

            if (wasOpen)
            {
                _current = null;
                _currentEditorNoteId = null;
                SelectedNote = null;
                ClearEditor();
                SaveStatus = "";
            }

            _lastDeletedNoteId = item.Id;
            Notes.Remove(item);
            IsUndoAvailable = true;
            UndoMessage = "메모를 휴지통으로 옮겼습니다.";
        }
        catch (Exception ex)
        {
            AppLog.Error("DeleteNote", ex);
        }
        finally
        {
            EndBusy();
        }
    }

    private bool CanUndoDelete() => IsUndoAvailable;

    [RelayCommand(CanExecute = nameof(CanUndoDelete))]
    private void UndoDelete()
    {
        if (!IsUndoAvailable) return;
        _noteRepo.Restore(_lastDeletedNoteId);
        IsUndoAvailable = false;
        UndoMessage = "";

        // 복원된 메모가 보이도록 해당 그룹으로 이동(삭제와 실행취소 사이 그룹을 바꿔도 결과가 보인다).
        var restored = _noteRepo.Get(_lastDeletedNoteId);
        if (restored is not null) NavigateToNote(restored.Id, restored.GroupId);
        else LoadNotes();
    }

    [RelayCommand(CanExecute = nameof(CanUndoDelete))]
    private async Task UndoDeleteWithLoadingAsync()
    {
        if (!IsUndoAvailable) return;
        var noteId = _lastDeletedNoteId;
        AppLog.Action("UI.UndoDelete", $"note_id={noteId}");
        BeginBusy("메모를 복원하는 중…");
        try
        {
            var restored = await Task.Run(() =>
            {
                _noteRepo.Restore(noteId);
                return _noteRepo.Get(noteId);
            });
            IsUndoAvailable = false;
            UndoMessage = "";
            if (restored is not null) await NavigateToNoteAsync(restored.Id, restored.GroupId);
            else await LoadNotesAsync(SelectedNode);
        }
        catch (Exception ex)
        {
            AppLog.Error("UndoDelete", ex);
        }
        finally
        {
            EndBusy();
        }
    }

    [RelayCommand]
    private void OpenWorkEntries()
    {
        (CurrentEditor as ChecklistViewModel)?.FlushSaves();
        _autosave.FlushAll();
        if (_workEntriesFactory is null) return;
        SelectedNode = null;
        SelectedNote = null;
        _editorLoadGeneration++;
        _current = null;
        _currentEditorNoteId = null;
        IsUndoAvailable = false;
        CurrentEditor = _workEntriesFactory();
    }

    [RelayCommand]
    private void OpenWeeklyReport()
    {
        IsUndoAvailable = false;
        var weekly = _weeklyReportEditorFactory();   // 기본 = 오늘이 포함된 주(M4 생성자)
        weekly.GenerateCommand.Execute(null);        // 멱등 로드/생성(필요 시 새 주간보고 노트 생성)

        // 1) 사이드바(주간보고 시스템 그룹)·목록을 먼저 동기화한다.
        //    (SelectedNode 변경 → LoadNotes → SelectedNote=null → CurrentEditor=null 이 될 수 있으므로 에디터는 그 뒤에 설정)
        if (weekly.CurrentNoteId is int id)
        {
            var note = _noteRepo.Get(id);
            var node = SystemNodes.FirstOrDefault(n => n.GroupId == note?.GroupId);
            if (node is not null) SelectedNode = node;
        }

        // 2) 주간보고 에디터를 호스팅(최종 상태).
        CurrentNoteType = NoteType.WeeklyReport;
        CurrentEditor = weekly;
        _currentEditorNoteId = weekly.CurrentNoteId;
    }

    [RelayCommand]
    private async Task LoadWeeklyReportAsync()
    {
        AppLog.Action("UI.OpenWeeklyReport");
        using var perf = AppLog.Measure("LoadWeeklyReport");
        IsUndoAvailable = false;
        BeginBusy("주간보고를 생성하는 중…");
        try
        {
            var weekly = _weeklyReportEditorFactory();
            await weekly.GenerateInBackgroundCommand.ExecuteAsync(null);

            if (weekly.CurrentNoteId is int id)
            {
                var note = await Task.Run(() => _noteRepo.Get(id));
                var node = SystemNodes.FirstOrDefault(n => n.GroupId == note?.GroupId);
                if (node is not null) SelectedNode = node;
            }

            CurrentNoteType = NoteType.WeeklyReport;
            CurrentEditor = weekly;
            _currentEditorNoteId = weekly.CurrentNoteId;
        }
        catch (Exception ex)
        {
            AppLog.Error("LoadWeeklyReport", ex);
        }
        finally
        {
            EndBusy();
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        AppServices.Resolve<ISettingsWindowService>().ShowSettings();
        (CurrentEditor as WeeklyReportViewModel)?.RefreshSettings();
        if (CurrentEditor is ChecklistViewModel checklist) await checklist.RefreshClientsAsync();
    }

    [RelayCommand]
    private void Search()
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        foreach (var hit in _search.Search(SearchText))
            SearchResults.Add(hit);
    }

    [RelayCommand]
    private async Task RunSearchAsync()
    {
        using var perf = AppLog.Measure("Search");
        var query = SearchText.Trim();
        AppLog.Action("UI.Search", $"length={query.Length}");
        var generation = ++_searchGeneration;
        SearchResults.Clear();
        if (query.Length == 0) return;

        BeginBusy("검색하는 중…");
        try
        {
            var results = await Task.Run(() => _search.Search(query));
            if (generation != _searchGeneration || !string.Equals(query, SearchText.Trim(), StringComparison.Ordinal)) return;
            foreach (var hit in results) SearchResults.Add(hit);
        }
        catch (Exception ex)
        {
            AppLog.Error("Search", ex);
        }
        finally
        {
            EndBusy();
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchGeneration++;
        SearchText = string.Empty;
        SearchResults.Clear();
    }

    [RelayCommand]
    private void OpenSearchHit(SearchHit hit)    // 계약 §9.3 / M2 시그니처: non-nullable SearchHit
    {
        if (hit is null) return;                 // 내부 null 가드(외부에서 null 전달 방지)
        var note = _noteRepo.Get(hit.NoteId);
        if (note is null) return;
        NavigateToNote(hit.NoteId, note.GroupId);
    }

    [RelayCommand]
    private async Task NavigateToSearchHitAsync(SearchHit? hit)
    {
        if (hit is null) return;
        BeginBusy("메모로 이동하는 중…");
        try
        {
            var note = await Task.Run(() => _noteRepo.Get(hit.NoteId));
            if (note is not null) await NavigateToNoteAsync(hit.NoteId, note.GroupId);
        }
        catch (Exception ex)
        {
            AppLog.Error("Search.Navigate", ex);
        }
        finally
        {
            EndBusy();
        }
    }

    public void OpenNote(int noteId)
    {
        _autosave.FlushAll();                       // 조회 실패여도 이전 노트 저장은 확정
        var note = _noteRepo.Get(noteId);
        if (note is null) return;

        OpenNote(note, flushPending: false);
    }

    private void OpenNote(Note note, bool flushPending = true)
    {
        var previousNoteId = _current?.Id;
        if (flushPending)
            _autosave.FlushAll();                   // 동기 API/테스트 경로의 데이터 보존
        if (previousNoteId is int oldId && oldId != note.Id)
            _autosave.Unregister(oldId);             // 장기 사용 시 노트별 콜백 누적 방지

        _current = note;
        CurrentNoteType = note.Type;   // M9 뷰 호스팅용
        _suppressDirty = true;
        EditorTitle = note.Title ?? "";
        EditorBody = note.Body ?? "";
        _suppressDirty = false;

        BodyFormat = note.BodyFormat;
        // 새/빈 본문 → 편집 모드, 내용 있으면 읽기 모드(markdown 노트만 읽기/편집 구분 의미 있음).
        ViewMode = MarkdownEnabled && note.BodyFormat == "markdown" && !string.IsNullOrEmpty(note.Body)
            ? MarkdownViewMode.Read : MarkdownViewMode.Edit;

        SaveStatus = "";   // 노트 전환 시 저장 표시 초기화
        HeaderText = EditorHeaderFormatter.Format(note.CreatedAt.ToLocalTime(), note.UpdatedAt.ToLocalTime());
        IsEditorVisible = true;
        _autosave.Register(note.Id, snapshot => SaveCurrent(note.Id, snapshot));
    }

    private static bool CanUseAsyncUi()
        => Application.Current?.Dispatcher is { } dispatcher && dispatcher.CheckAccess();

    private void BeginBusy(string message)
    {
        _busyCount++;
        LoadingMessage = message;
        IsBusy = true;
    }

    private void EndBusy()
    {
        if (_busyCount > 0) _busyCount--;
        if (_busyCount == 0) IsBusy = false;
    }

    partial void OnEditorTitleChanged(string value) => OnContentChanged();
    partial void OnEditorBodyChanged(string value) => OnContentChanged();

    private void OnContentChanged()
    {
        if (_suppressDirty || _current is null) return;

        // #2 목록의 제목을 즉시 갱신(탭 전환 없이 바로 반영).
        UpdateListItemTitle(_current.Id, ResolveLiveTitle());
        // #3 저장 대기 표시.
        SaveStatus = "저장 중…";

        // 변경 시점에 (title, body) 스냅샷을 캡처해 복구 저널과 자동저장에 동일하게 전달한다.
        // 자동저장 콜백이 뒤늦게(다른 노트로 전환된 뒤) 발화해도 라이브 에디터 상태를
        // 다시 읽지 않으므로 노트 간 내용 오염 레이스가 발생하지 않는다.
        _recovery.Append(new RecoverySnapshot(_current.Id, EditorTitle, EditorBody, _time.GetUtcNow()));
        _autosave.NotifyChanged(_current.Id, new AutosaveSnapshot(EditorTitle, EditorBody));
    }

    // 편집 중 라이브 제목(§5.1과 동일 규칙: title 우선, 없으면 본문 첫 비어있지 않은 줄).
    private string ResolveLiveTitle()
    {
        if (!string.IsNullOrWhiteSpace(EditorTitle)) return EditorTitle.Trim();
        if (!string.IsNullOrEmpty(EditorBody))
        {
            var isMarkdown = MarkdownEnabled && _current?.BodyFormat == "markdown";
            // 본문 전체를 Split하면 키 입력마다 문자열 배열과 모든 줄을 새로 할당한다.
            // 제목에 필요한 첫 비어있지 않은 줄까지만 span으로 훑는다.
            var body = EditorBody.AsSpan();
            var start = 0;
            while (start <= body.Length)
            {
                var remaining = body[start..];
                var newline = remaining.IndexOf('\n');
                var line = (newline >= 0 ? remaining[..newline] : remaining).Trim();
                if (!line.IsEmpty)
                {
                    var title = line.ToString();
                    return isMarkdown ? Memoria.Core.Text.MarkdownText.StripMarkers(title) : title;
                }
                if (newline < 0) break;
                start += newline + 1;
            }
        }
        return "(제목 없음)";
    }

    private void UpdateListItemTitle(int noteId, string title)
    {
        var item = Notes.FirstOrDefault(n => n.Id == noteId);
        if (item is not null) item.DisplayTitle = title;
    }

    // UI 스레드 보장 헬퍼(자동저장 콜백은 백그라운드 스레드에서 호출됨).
    // Application.Current가 없으면(단위 테스트) 인라인 실행한다.
    private static void PostToUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(action);
    }

    // 자동저장 콜백(백그라운드 스레드). 변경 시점 스냅샷만 사용하고 라이브 에디터 상태나
    // ObservableCollection은 건드리지 않는다.
    private void SaveCurrent(int noteId, AutosaveSnapshot snapshot)
    {
        using var perf = AppLog.Measure("Autosave.SaveCurrent", 100);
        var note = _noteRepo.Get(noteId);
        if (note is null) return;

        note.Title = string.IsNullOrWhiteSpace(snapshot.Title) ? null : snapshot.Title;
        note.Body = snapshot.Body;
        note.UpdatedAt = _time.GetUtcNow();         // §7.7 콘텐츠 변경 시에만 갱신
        _noteRepo.Update(note);
        _recovery.Clear(noteId);

        // #3 저장 완료 표시(콜백은 백그라운드 스레드 → UI 스레드로 마샬링).
        var stamp = _time.GetLocalNow().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        PostToUi(() => SaveStatus = $"저장됨 {stamp}");
    }

    private void BuildFolders()
    {
        Folders.Clear();
        if (SelectedNode is null) return;
        foreach (var child in SelectedNode.Children.Where(c => c.Kind == SidebarNodeKind.Group))
            Folders.Add(new FolderEntryViewModel(child));
    }

    private void BuildBreadcrumb()
    {
        Breadcrumb.Clear();
        if (SelectedNode is null) return;
        var path = new System.Collections.Generic.List<SidebarNodeViewModel>();
        if (FindPath(SidebarNodes, SelectedNode, path))
            foreach (var n in path) Breadcrumb.Add(new BreadcrumbSegmentViewModel(n));
        else
            Breadcrumb.Add(new BreadcrumbSegmentViewModel(SelectedNode)); // 시스템/미분류 단일 조각
    }

    private static bool FindPath(
        System.Collections.Generic.IEnumerable<SidebarNodeViewModel> nodes,
        SidebarNodeViewModel target,
        System.Collections.Generic.List<SidebarNodeViewModel> path)
    {
        foreach (var n in nodes)
        {
            path.Add(n);
            if (ReferenceEquals(n, target)) return true;
            if (FindPath(n.Children, target, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    /// 가운데 폴더/브레드크럼 클릭 → 그 노드를 현재 폴더로.
    public void NavigateToFolder(SidebarNodeViewModel node) => SelectedNode = node;

    // 시작 시 감지된 미저장 스냅샷을 DB에 반영(§8.1).
    public void ApplyRecovery(IReadOnlyList<RecoverySnapshot> snapshots)
    {
        foreach (var s in snapshots)
        {
            var note = _noteRepo.Get(s.NoteId);
            if (note is null) { _recovery.Clear(s.NoteId); continue; }

            note.Title = string.IsNullOrWhiteSpace(s.Title) ? null : s.Title;
            note.Body = s.Body;
            note.UpdatedAt = _time.GetUtcNow();
            _noteRepo.Update(note);
            _recovery.Clear(s.NoteId);
        }
    }
}

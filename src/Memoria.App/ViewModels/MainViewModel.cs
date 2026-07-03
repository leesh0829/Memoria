using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    private Note? _current;                 // plain 에디터의 현재 노트(헤더/본문 로직용)
    private int? _currentEditorNoteId;      // #6 우측에 호스팅 중인 노트 id(plain/checklist/weekly 공통)
    private bool _suppressDirty;

    public ObservableCollection<SidebarNodeViewModel> SidebarNodes { get; } = new();   // 사용자 그룹 + (미분류)
    public ObservableCollection<SidebarNodeViewModel> SystemNodes { get; } = new();    // #5 시스템 그룹(하단 고정)
    public ObservableCollection<NoteListItemViewModel> Notes { get; } = new();
    public ObservableCollection<SearchHit> SearchResults { get; } = new();

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

    // RM6: 마크다운 에디터 상태
    [ObservableProperty] private bool isPreviewMode = true;
    [ObservableProperty] private string bodyFormat = "plain";

    public bool IsMarkdown => BodyFormat == "markdown";
    public bool ShowToolbar => IsMarkdown && !IsPreviewMode;
    public bool ShowPreview => IsMarkdown && IsPreviewMode;
    public bool ShowSource  => !ShowPreview;   // plain이거나 편집 모드

    partial void OnIsPreviewModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowToolbar));
        OnPropertyChanged(nameof(ShowPreview));
        OnPropertyChanged(nameof(ShowSource));
    }

    partial void OnBodyFormatChanged(string value)
    {
        OnPropertyChanged(nameof(IsMarkdown));
        OnPropertyChanged(nameof(ShowToolbar));
        OnPropertyChanged(nameof(ShowPreview));
        OnPropertyChanged(nameof(ShowSource));
    }

    [RelayCommand]
    private void TogglePreview() => IsPreviewMode = !IsPreviewMode;

    [RelayCommand]
    private void ConvertToMarkdown()
    {
        if (_current is null || _current.BodyFormat == "markdown") return;
        _current.BodyFormat = "markdown";
        _noteRepo.Update(_current);         // 즉시 저장(디바운스 밖)
        BodyFormat = "markdown";
        IsPreviewMode = false;              // 전환 직후 편집 모드
        UpdateListItemTitle(_current.Id, ResolveLiveTitle());
    }

    // #3 자동저장 상태 표시("저장 중…" → "저장됨 HH:mm:ss"). 저장 없이 자동 영속됨을 사용자에게 보장.
    [ObservableProperty] private string saveStatus = "";

    // #4 삭제(휴지통)/실행취소 — MainViewModel이 직접 소유(열린 노트 삭제 시 에디터까지 정리).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoDeleteCommand))]
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
        Func<WeeklyReportViewModel> weeklyReportEditorFactory)
    {
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

        SidebarNodes.Clear();
        SystemNodes.Clear();
        var groups = _groupRepo.GetAll();

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
        IsUndoAvailable = false;   // 그룹 이동 등 다른 동작 시 휴지통 Undo 토스트 자동 해제
        LoadNotes();
    }

    partial void OnSelectedNoteChanged(NoteListItemViewModel? value)
    {
        if (value is null)
        {
            CurrentEditor = null;
            IsEditorVisible = false;
            _currentEditorNoteId = null;
            return;
        }

        var note = _noteRepo.Get(value.Id);
        if (note is null) return;

        CurrentNoteType = note.Type;
        CurrentEditor = BuildEditorFor(note);
    }

    // NoteType → 에디터 VM 매핑(계약 §11: plain/checklist/weekly_report → 각 View 호스팅).
    private object? BuildEditorFor(Note note)
    {
        _currentEditorNoteId = note.Id;
        switch (note.Type)
        {
            case NoteType.Plain:
                OpenNote(note.Id);          // 기존 M2 plain 에디터 로직(헤더/본문/IsEditorVisible) 재사용
                return this;                // plain DataTemplate은 MainViewModel 자신에 바인딩
            case NoteType.Checklist:
                var checklist = _checklistEditorFactory();
                checklist.Load(note);
                return checklist;
            case NoteType.WeeklyReport:
                var weekly = _weeklyReportEditorFactory();
                if (note.ReportWeekStart is DateOnly ws) weekly.SelectedDate = ws;
                if (note.ReportFormat is ReportFormatKind fmt) weekly.SelectedFormat = fmt;
                weekly.GenerateCommand.Execute(null);   // 멱등 로드(M4: 기존 body 재사용)
                return weekly;
            default:
                return null;
        }
    }

    public void LoadNotes()
    {
        Notes.Clear();
        if (SelectedNode is null) return;

        var notes = _noteRepo.GetByGroup(SelectedNode.GroupId)
            .OrderByDescending(n => n.Pinned)
            .ThenByDescending(n => n.UpdatedAt);

        foreach (var n in notes)
            Notes.Add(new NoteListItemViewModel(n.Id, NoteTitleResolver.Resolve(n), n.Pinned, n.UpdatedAt));
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
            BodyFormat = "markdown",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var id = _noteRepo.Create(note);
        NavigateToNote(id, note.GroupId);   // #3 새 메모를 즉시 선택 → 우측 편집창 표시
    }

    [RelayCommand]
    private void NewChecklist()
    {
        IsUndoAvailable = false;
        var group = _groupRepo.GetAll()
            .FirstOrDefault(g => g.IsSystem && g.Name == ChecklistViewModel.DailyLogGroupName);

        var now = _time.GetUtcNow();
        var note = new Note
        {
            Type = NoteType.Checklist,
            GroupId = group?.Id,
            LogDate = DateOnly.FromDateTime(now.LocalDateTime.Date),
            CreatedAt = now,
            UpdatedAt = now,
        };
        var id = _noteRepo.Create(note);
        NavigateToNote(id, group?.Id);
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
    private void OpenSettings() => AppServices.Resolve<ISettingsWindowService>().ShowSettings();

    [RelayCommand]
    private void Search()
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        foreach (var hit in _search.Search(SearchText))
            SearchResults.Add(hit);
    }

    [RelayCommand]
    private void OpenSearchHit(SearchHit hit)    // 계약 §9.3 / M2 시그니처: non-nullable SearchHit
    {
        if (hit is null) return;                 // 내부 null 가드(외부에서 null 전달 방지)
        var note = _noteRepo.Get(hit.NoteId);
        if (note is null) return;
        NavigateToNote(hit.NoteId, note.GroupId);
    }

    public void OpenNote(int noteId)
    {
        _autosave.FlushAll();                       // 이전 노트의 보류 저장 확정

        var note = _noteRepo.Get(noteId);
        if (note is null) return;

        _current = note;
        CurrentNoteType = note.Type;   // M9 뷰 호스팅용
        _suppressDirty = true;
        EditorTitle = note.Title ?? "";
        EditorBody = note.Body ?? "";
        _suppressDirty = false;

        BodyFormat = note.BodyFormat;
        // 새/빈 본문 → 편집 모드, 내용 있으면 미리보기(markdown 노트만 미리보기 의미 있음).
        IsPreviewMode = note.BodyFormat == "markdown" && !string.IsNullOrEmpty(note.Body);

        SaveStatus = "";   // 노트 전환 시 저장 표시 초기화
        HeaderText = EditorHeaderFormatter.Format(note.CreatedAt.ToLocalTime(), note.UpdatedAt.ToLocalTime());
        IsEditorVisible = true;
        _autosave.Register(noteId, snapshot => SaveCurrent(noteId, snapshot));
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
            var isMarkdown = _current?.BodyFormat == "markdown";
            foreach (var line in EditorBody.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                return isMarkdown ? Memoria.Core.Text.MarkdownText.StripMarkers(trimmed) : trimmed;
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
        else dispatcher.Invoke(action);
    }

    // 자동저장 콜백(백그라운드 스레드). 변경 시점 스냅샷만 사용하고 라이브 에디터 상태나
    // ObservableCollection은 건드리지 않는다.
    private void SaveCurrent(int noteId, AutosaveSnapshot snapshot)
    {
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

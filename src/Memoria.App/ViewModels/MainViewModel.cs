using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    private Note? _current;
    private bool _suppressDirty;

    public ObservableCollection<SidebarNodeViewModel> SidebarNodes { get; } = new();
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

    public void LoadGroups()
    {
        SidebarNodes.Clear();
        var groups = _groupRepo.GetAll();

        foreach (var g in groups.Where(g => !g.IsSystem))
            SidebarNodes.Add(new SidebarNodeViewModel(g.Name, g.Id, SidebarNodeKind.Group));

        SidebarNodes.Add(new SidebarNodeViewModel("(미분류)", null, SidebarNodeKind.Unclassified));

        foreach (var g in groups.Where(g => g.IsSystem))
            SidebarNodes.Add(new SidebarNodeViewModel(g.Name, g.Id, SidebarNodeKind.System));
    }

    partial void OnSelectedNodeChanged(SidebarNodeViewModel? value) => LoadNotes();

    partial void OnSelectedNoteChanged(NoteListItemViewModel? value)
    {
        if (value is null)
        {
            CurrentEditor = null;
            IsEditorVisible = false;
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
        var now = _time.GetUtcNow();
        var note = new Note
        {
            Type = NoteType.Plain,
            GroupId = SelectedNode?.GroupId,
            Title = null,
            Body = "",
            CreatedAt = now,
            UpdatedAt = now,
        };
        _noteRepo.Create(note);
        LoadNotes();
    }

    [RelayCommand]
    private void NewChecklist()
    {
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
        var node = SidebarNodes.FirstOrDefault(n => n.GroupId == groupId)
                   ?? SidebarNodes.FirstOrDefault(n => n.Kind == SidebarNodeKind.Unclassified);
        if (node is not null) SelectedNode = node;

        LoadNotes();   // SelectedNode가 이미 동일하면 OnSelectedNodeChanged가 안 울리므로 명시 재로드
        SelectedNote = Notes.FirstOrDefault(n => n.Id == noteId);
    }

    [RelayCommand]
    private void OpenWeeklyReport()
    {
        var weekly = _weeklyReportEditorFactory();   // 기본 = 오늘이 포함된 주(M4 생성자)
        weekly.GenerateCommand.Execute(null);        // 멱등 로드/생성(필요 시 새 주간보고 노트 생성)
        CurrentNoteType = NoteType.WeeklyReport;
        CurrentEditor = weekly;
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

        HeaderText = EditorHeaderFormatter.Format(note.CreatedAt.ToLocalTime(), note.UpdatedAt.ToLocalTime());
        IsEditorVisible = true;
        _autosave.Register(noteId, snapshot => SaveCurrent(noteId, snapshot));
    }

    partial void OnEditorTitleChanged(string value) => OnContentChanged();
    partial void OnEditorBodyChanged(string value) => OnContentChanged();

    private void OnContentChanged()
    {
        if (_suppressDirty || _current is null) return;
        // 변경 시점에 (title, body) 스냅샷을 캡처해 복구 저널과 자동저장에 동일하게 전달한다.
        // 자동저장 콜백이 뒤늦게(다른 노트로 전환된 뒤) 발화해도 라이브 에디터 상태를
        // 다시 읽지 않으므로 노트 간 내용 오염 레이스가 발생하지 않는다.
        _recovery.Append(new RecoverySnapshot(_current.Id, EditorTitle, EditorBody, _time.GetUtcNow()));
        _autosave.NotifyChanged(_current.Id, new AutosaveSnapshot(EditorTitle, EditorBody));
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

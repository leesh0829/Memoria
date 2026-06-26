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
    private readonly ISearchService? _search;
    private readonly Func<ChecklistViewModel>? _checklistEditorFactory;
    private readonly Func<WeeklyReportViewModel>? _weeklyReportEditorFactory;

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
        ISearchService? search = null,
        Func<ChecklistViewModel>? checklistEditorFactory = null,
        Func<WeeklyReportViewModel>? weeklyReportEditorFactory = null)
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
                var checklist = _checklistEditorFactory!();
                checklist.Load(note);
                return checklist;
            case NoteType.WeeklyReport:
                var weekly = _weeklyReportEditorFactory!();
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
    private void NewChecklist() { /* M9에서 채움 */ }

    [RelayCommand]
    private void OpenWeeklyReport() { /* M9에서 채움 */ }

    [RelayCommand]
    private void OpenSettings() => AppServices.Resolve<ISettingsWindowService>().ShowSettings();

    [RelayCommand]
    private void Search() { /* M9에서 채움 */ }

    [RelayCommand]
    private void OpenSearchHit(SearchHit hit) { /* M9에서 채움 */ }

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
        _autosave.Register(noteId, () => SaveCurrent(noteId));
    }

    partial void OnEditorTitleChanged(string value) => OnContentChanged();
    partial void OnEditorBodyChanged(string value) => OnContentChanged();

    private void OnContentChanged()
    {
        if (_suppressDirty || _current is null) return;
        _recovery.Append(new RecoverySnapshot(_current.Id, EditorTitle, EditorBody, _time.GetUtcNow()));
        _autosave.NotifyChanged(_current.Id);
    }

    // 자동저장 콜백(백그라운드 스레드). 리포지토리/저널만 접근하고 ObservableCollection은 건드리지 않는다.
    private void SaveCurrent(int noteId)
    {
        var note = _noteRepo.Get(noteId);
        if (note is null) return;

        note.Title = string.IsNullOrWhiteSpace(EditorTitle) ? null : EditorTitle;
        note.Body = EditorBody;
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

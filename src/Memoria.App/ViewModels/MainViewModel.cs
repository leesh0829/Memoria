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
        TimeProvider time)
    {
        _groupRepo = groupRepo;
        _noteRepo = noteRepo;
        _autosave = autosave;
        _recovery = recovery;
        _time = time;
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
        if (value is not null) OpenNote(value.Id);
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

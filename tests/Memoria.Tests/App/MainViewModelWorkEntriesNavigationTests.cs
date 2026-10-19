using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;

namespace Memoria.Tests.App;

public class MainViewModelWorkEntriesNavigationTests
{
    private static WorkEntriesViewModel WorkEditor() => new(
        new EmptyWorkEntries(), new FakeClipboard(), new FakeConfirm());

    [Theory]
    [InlineData(SidebarNodeKind.Group, "사용자 그룹", 1)]
    [InlineData(SidebarNodeKind.Unclassified, "미분류", null)]
    [InlineData(SidebarNodeKind.System, "일일업무일지", 2)]
    [InlineData(SidebarNodeKind.System, "주간보고", 3)]
    public void SelectingSidebarAfterWorkEntries_ClosesFullScreenEditorEvenWhenNoNoteIsSelected(
        SidebarNodeKind kind, string name, int? groupId)
    {
        var work = WorkEditor();
        var (vm, notes, groups, _) = MainViewModelEditorHostTests.Build(() => work);
        if (groupId is int id) groups.Items.Add(new Group { Id = id, Name = name, IsSystem = kind == SidebarNodeKind.System });
        notes.Create(new Note { GroupId = groupId, Type = NoteType.Plain, Title = "목록에 표시할 메모" });
        vm.OpenWorkEntriesCommand.Execute(null);
        vm.CurrentEditor.Should().BeSameAs(work);
        vm.SelectedNote.Should().BeNull();
        var editorChanges = new List<object?>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.CurrentEditor)) editorChanges.Add(vm.CurrentEditor);
        };

        vm.SelectedNode = new SidebarNodeViewModel(name, groupId, kind);

        vm.CurrentEditor.Should().BeNull();
        editorChanges.Should().ContainSingle().Which.Should().BeNull();
        vm.Notes.Should().ContainSingle();
        // 가운데 목록이 복원된 뒤 실제 메모 선택도 정상적으로 새 에디터를 연다.
        vm.SelectedNote = vm.Notes[0];
        vm.CurrentEditor.Should().BeSameAs(vm);
    }

    [Theory]
    [InlineData(NoteType.Plain)]
    [InlineData(NoteType.Checklist)]
    [InlineData(NoteType.WeeklyReport)]
    public void SelectingNoteAfterWorkEntries_OpensItsEditor_AndCanReturnToWorkEntries(NoteType type)
    {
        var work = WorkEditor();
        var (vm, notes, _, _) = MainViewModelEditorHostTests.Build(() => work);
        var id = notes.Create(new Note
        {
            Type = type, Body = "메모", LogDate = new DateOnly(2026, 10, 19),
            ReportFormat = ReportFormatKind.B, ReportWeekStart = new DateOnly(2026, 10, 19),
        });
        vm.OpenWorkEntriesCommand.Execute(null);

        vm.SelectedNote = new NoteListItemViewModel(id, "메모", false, DateTimeOffset.UnixEpoch);

        vm.CurrentNoteType.Should().Be(type);
        vm.CurrentEditor.Should().NotBeNull().And.NotBeSameAs(work);
        if (type == NoteType.Plain) vm.CurrentEditor.Should().BeSameAs(vm);
        if (type == NoteType.Checklist) vm.CurrentEditor.Should().BeOfType<ChecklistViewModel>();
        if (type == NoteType.WeeklyReport) vm.CurrentEditor.Should().BeOfType<WeeklyReportViewModel>();
        vm.OpenWorkEntriesCommand.Execute(null);
        vm.CurrentEditor.Should().BeSameAs(work);
    }

    [Fact]
    public async Task WeeklyReportToolbarAfterWorkEntries_ReplacesEditorAndClearsBusyState()
    {
        var work = WorkEditor();
        var (vm, _, _, _) = MainViewModelEditorHostTests.Build(() => work);
        vm.OpenWorkEntriesCommand.Execute(null);

        await vm.LoadWeeklyReportCommand.ExecuteAsync(null);

        vm.CurrentEditor.Should().BeOfType<WeeklyReportViewModel>();
        vm.IsBusy.Should().BeFalse();
    }

    private sealed class EmptyWorkEntries : IWorkEntryRepository
    {
        public IReadOnlyList<WorkEntry> GetAll() => [];
        public void Save(WorkEntry entry) { }
        public void Delete(string id) { }
    }
}

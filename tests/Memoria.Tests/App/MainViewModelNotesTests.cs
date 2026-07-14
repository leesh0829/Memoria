using System;
using System.Linq;
using FluentAssertions;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelNotesTests
{
    private static MainViewModel Build(FakeGroupRepository groups, FakeNoteRepository notes, TimeProvider time)
        => new MainViewModel(groups, notes,
            new DebounceAutosaveService(time, 500),
            new FakeRecoveryJournal(),
            time,
            new FakeSearchService(),
            M9EditorFakes.ChecklistFactory(notes, groups),
            M9EditorFakes.WeeklyFactory(notes, groups, time));

    [Fact]
    public void Selecting_group_loads_notes_pinned_first_then_updated_desc()
    {
        var groups = new FakeGroupRepository();
        var gid = groups.Create(new Group { Name = "업무", SortOrder = 1 });
        var notes = new FakeNoteRepository();
        var t0 = DateTimeOffset.UnixEpoch;
        notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "오래됨", Pinned = false, UpdatedAt = t0 });
        notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "최신",   Pinned = false, UpdatedAt = t0.AddDays(2) });
        notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "고정",   Pinned = true,  UpdatedAt = t0.AddDays(1) });
        var vm = Build(groups, notes, new FakeTimeProvider());
        vm.LoadGroups();

        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == gid);

        vm.Notes.Select(n => n.DisplayTitle).Should().ContainInOrder("고정", "최신", "오래됨");
    }

    [Fact]
    public void Unclassified_node_loads_notes_with_null_group()
    {
        var groups = new FakeGroupRepository();
        var notes = new FakeNoteRepository();
        notes.Create(new Note { GroupId = null, Type = NoteType.Plain, Title = "미분류 메모", UpdatedAt = DateTimeOffset.UnixEpoch });
        var vm = Build(groups, notes, new FakeTimeProvider());
        vm.LoadGroups();

        vm.SelectedNode = vm.SidebarNodes.First(n => n.Kind == SidebarNodeKind.Unclassified);

        vm.Notes.Should().ContainSingle().Which.DisplayTitle.Should().Be("미분류 메모");
    }

    [Fact]
    public void NewPlainNote_creates_plain_note_in_selected_group_and_reloads()
    {
        var groups = new FakeGroupRepository();
        var gid = groups.Create(new Group { Name = "업무", SortOrder = 1 });
        var notes = new FakeNoteRepository();
        var time = new FakeTimeProvider();
        var vm = Build(groups, notes, time);
        vm.LoadGroups();
        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == gid);

        vm.NewPlainNoteCommand.Execute(null);

        notes.Items.Should().ContainSingle();
        notes.Items[0].Type.Should().Be(NoteType.Plain);
        notes.Items[0].GroupId.Should().Be(gid);
        notes.Items[0].CreatedAt.Should().Be(time.GetUtcNow());
        vm.Notes.Should().ContainSingle();
    }

    [Fact]
    public void NewPlainNote_in_system_group_falls_back_to_unclassified()
    {
        // #5 일반 메모는 시스템 그룹(일일업무일지·주간보고)에 들어갈 수 없다.
        var groups = new FakeGroupRepository();
        var sysId = groups.Create(new Group { Name = "주간보고", IsSystem = true, SortOrder = 10 });
        var notes = new FakeNoteRepository();
        var vm = Build(groups, notes, new FakeTimeProvider());
        vm.LoadGroups();
        vm.SelectedNode = vm.SystemNodes.First(n => n.GroupId == sysId);

        vm.NewPlainNoteCommand.Execute(null);

        notes.Items.Should().ContainSingle();
        notes.Items[0].GroupId.Should().BeNull(); // 시스템 그룹이 아니라 (미분류)
    }

    [Fact]
    public void DeleteNote_softdeletes_removes_from_list_and_enables_undo()
    {
        var groups = new FakeGroupRepository();
        var gid = groups.Create(new Group { Name = "업무", SortOrder = 1 });
        var notes = new FakeNoteRepository();
        var id = notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "삭제대상" });
        var vm = Build(groups, notes, new FakeTimeProvider());
        vm.LoadGroups();
        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == gid);
        var item = vm.Notes.Single();

        vm.DeleteNoteCommand.Execute(item);

        notes.Get(id)!.DeletedAt.Should().NotBeNull(); // 휴지통으로
        vm.Notes.Should().BeEmpty();                    // 목록에서 즉시 제거
        vm.IsUndoAvailable.Should().BeTrue();
    }

    [Fact]
    public void DeleteNote_of_open_note_clears_editor()
    {
        // #4 선택(열린) 메모를 삭제하면 우측 본문이 빈 화면이 되어야 한다.
        var groups = new FakeGroupRepository();
        var gid = groups.Create(new Group { Name = "업무", SortOrder = 1 });
        var notes = new FakeNoteRepository();
        notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "열린메모" });
        var vm = Build(groups, notes, new FakeTimeProvider());
        vm.LoadGroups();
        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == gid);
        var item = vm.Notes.Single();
        vm.SelectedNote = item;            // 에디터 호스팅
        vm.IsEditorVisible.Should().BeTrue();

        vm.DeleteNoteCommand.Execute(item);

        vm.CurrentEditor.Should().BeNull();
        vm.IsEditorVisible.Should().BeFalse();
        vm.SelectedNote.Should().BeNull();
    }

    [Fact]
    public void ReorderNote_moves_item_and_renumbers_sortOrder_and_persists()
    {
        var groups = new FakeGroupRepository();
        var gid = groups.Create(new Group { Name = "업무", SortOrder = 1 });
        var notes = new FakeNoteRepository();
        var t0 = DateTimeOffset.UnixEpoch;
        var a = notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "A", UpdatedAt = t0.AddDays(2) });
        var b = notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "B", UpdatedAt = t0.AddDays(1) });
        var c = notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "C", UpdatedAt = t0 });
        var vm = Build(groups, notes, new FakeTimeProvider());
        vm.LoadGroups();
        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == gid);
        vm.Notes.Select(n => n.DisplayTitle).Should().ContainInOrder("A", "B", "C"); // 초기: 최근수정순

        vm.ReorderNote(a, 2); // A를 맨 아래로

        vm.Notes.Select(n => n.DisplayTitle).Should().ContainInOrder("B", "C", "A");
        notes.Get(b)!.SortOrder.Should().Be(0);
        notes.Get(c)!.SortOrder.Should().Be(1);
        notes.Get(a)!.SortOrder.Should().Be(2);

        // 영속화 검증: 재로드해도 수동 순서 유지(GetByGroup sort_order ASC)
        vm.LoadNotes();
        vm.Notes.Select(n => n.DisplayTitle).Should().ContainInOrder("B", "C", "A");
    }

    [Fact]
    public void ReorderNote_does_not_change_updatedAt()
    {
        var groups = new FakeGroupRepository();
        var gid = groups.Create(new Group { Name = "업무", SortOrder = 1 });
        var notes = new FakeNoteRepository();
        var t0 = DateTimeOffset.UnixEpoch;
        var a = notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "A", UpdatedAt = t0.AddDays(2) });
        notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "B", UpdatedAt = t0.AddDays(1) });
        var vm = Build(groups, notes, new FakeTimeProvider());
        vm.LoadGroups();
        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == gid);

        vm.ReorderNote(a, 1);

        notes.Get(a)!.UpdatedAt.Should().Be(t0.AddDays(2)); // 순서변경은 메타 조작 → updated_at 불변
    }

    [Fact]
    public void ReorderNote_is_noop_for_unknown_id_or_same_index()
    {
        var groups = new FakeGroupRepository();
        var gid = groups.Create(new Group { Name = "업무", SortOrder = 1 });
        var notes = new FakeNoteRepository();
        var a = notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "A", UpdatedAt = DateTimeOffset.UnixEpoch.AddDays(1) });
        var b = notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "B", UpdatedAt = DateTimeOffset.UnixEpoch });
        var vm = Build(groups, notes, new FakeTimeProvider());
        vm.LoadGroups();
        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == gid);

        vm.ReorderNote(99999, 0);     // 없는 id
        vm.ReorderNote(a, 0);         // 같은 위치(A는 이미 0)

        vm.Notes.Select(n => n.DisplayTitle).Should().ContainInOrder("A", "B"); // 변화 없음
    }

    [Fact]
    public void UndoDelete_restores_note_to_list()
    {
        var groups = new FakeGroupRepository();
        var gid = groups.Create(new Group { Name = "업무", SortOrder = 1 });
        var notes = new FakeNoteRepository();
        var id = notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "복원대상" });
        var vm = Build(groups, notes, new FakeTimeProvider());
        vm.LoadGroups();
        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == gid);
        vm.DeleteNoteCommand.Execute(vm.Notes.Single());

        vm.UndoDeleteCommand.Execute(null);

        notes.Get(id)!.DeletedAt.Should().BeNull();         // 복원됨
        vm.IsUndoAvailable.Should().BeFalse();
        vm.Notes.Should().ContainSingle(n => n.Id == id);   // 목록에 다시 보임
    }
}

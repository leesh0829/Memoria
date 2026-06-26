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
}

using System;
using FluentAssertions;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelMarkdownModeTests
{
    private static MainViewModel NewVm(FakeGroupRepository g, FakeNoteRepository n)
    {
        var time = new FakeTimeProvider();
        return new MainViewModel(g, n,
            new DebounceAutosaveService(time, 500),
            new FakeRecoveryJournal(), time, new FakeSearchService(),
            M9EditorFakes.ChecklistFactory(n, g), M9EditorFakes.WeeklyFactory(n, g, time));
    }

    [Fact]
    public void OpenNote_MarkdownWithContent_OpensInRead()
    {
        var notes = new FakeNoteRepository();
        var now = DateTimeOffset.UtcNow;
        var id = notes.Create(new Note { Type = NoteType.Plain, Body = "내용", BodyFormat = "markdown", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(new FakeGroupRepository(), notes);

        vm.OpenNote(id);

        vm.ViewMode.Should().Be(MarkdownViewMode.Read);
        vm.ShowRead.Should().BeTrue();
        vm.ShowEdit.Should().BeFalse();
    }

    [Fact]
    public void OpenNote_EmptyMarkdown_OpensInEdit()
    {
        var notes = new FakeNoteRepository();
        var now = DateTimeOffset.UtcNow;
        var id = notes.Create(new Note { Type = NoteType.Plain, Body = "", BodyFormat = "markdown", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(new FakeGroupRepository(), notes);

        vm.OpenNote(id);

        vm.ViewMode.Should().Be(MarkdownViewMode.Edit);
        vm.ShowEdit.Should().BeTrue();
        vm.ShowToolbar.Should().BeTrue();
    }

    [Fact]
    public void SetModes_SwitchViewMode()
    {
        var notes = new FakeNoteRepository();
        var now = DateTimeOffset.UtcNow;
        var id = notes.Create(new Note { Type = NoteType.Plain, Body = "x", BodyFormat = "markdown", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(new FakeGroupRepository(), notes);
        vm.OpenNote(id);

        vm.SetEditModeCommand.Execute(null);
        vm.ViewMode.Should().Be(MarkdownViewMode.Edit);
        vm.SetRenderedModeCommand.Execute(null);
        vm.ViewMode.Should().Be(MarkdownViewMode.Rendered);
        vm.ShowRendered.Should().BeTrue();
        vm.SetReadModeCommand.Execute(null);
        vm.ViewMode.Should().Be(MarkdownViewMode.Read);
    }

    [Fact]
    public void PlainNote_AlwaysShowsEdit_NotReadOrRendered()
    {
        var notes = new FakeNoteRepository();
        var now = DateTimeOffset.UtcNow;
        var id = notes.Create(new Note { Type = NoteType.Plain, Body = "hi", BodyFormat = "plain", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(new FakeGroupRepository(), notes);
        vm.OpenNote(id);

        vm.ShowEdit.Should().BeTrue();
        vm.ShowRead.Should().BeFalse();
        vm.ShowRendered.Should().BeFalse();
    }
}

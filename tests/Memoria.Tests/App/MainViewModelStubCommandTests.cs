using System;
using FluentAssertions;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelStubCommandTests
{
    private static MainViewModel NewVm(FakeNoteRepository? notes = null, FakeTimeProvider? time = null)
    {
        time ??= new FakeTimeProvider();
        return new MainViewModel(
            new FakeGroupRepository(),
            notes ?? new FakeNoteRepository(),
            new DebounceAutosaveService(time, 500),
            new FakeRecoveryJournal(),
            time);
    }

    [Fact]
    public void Stub_commands_execute_without_throwing()
    {
        var vm = NewVm();

        vm.NewChecklistCommand.Execute(null);
        vm.OpenWeeklyReportCommand.Execute(null);
        vm.OpenSettingsCommand.Execute(null);
        vm.SearchCommand.Execute(null);
        vm.OpenSearchHitCommand.Execute(new SearchHit(1, "title", "snippet"));

        vm.SearchResults.Should().BeEmpty();
        vm.SearchText.Should().BeEmpty();
    }

    [Fact]
    public void OpenNote_sets_CurrentNoteType()
    {
        var notes = new FakeNoteRepository();
        var time = new FakeTimeProvider();
        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Title = "t", Body = "b", CreatedAt = t0, UpdatedAt = t0 });
        var vm = NewVm(notes, time);

        vm.OpenNote(1);

        vm.CurrentNoteType.Should().Be(NoteType.Plain);
    }

    [Fact]
    public void Setting_SelectedNote_opens_that_note()
    {
        var notes = new FakeNoteRepository();
        var time = new FakeTimeProvider();
        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Title = "sel", Body = "body", CreatedAt = t0, UpdatedAt = t0 });
        var vm = NewVm(notes, time);

        vm.SelectedNote = new NoteListItemViewModel(1, "sel", false, t0);

        vm.IsEditorVisible.Should().BeTrue();
        vm.EditorBody.Should().Be("body");
    }
}

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
    public void DisabledMarkdown_opens_source_and_preserves_existing_format_and_body()
    {
        var notes = new FakeNoteRepository();
        var now = DateTimeOffset.UtcNow;
        var id = notes.Create(new Note { Type = NoteType.Plain, Body = "**원문**", BodyFormat = "markdown", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(new FakeGroupRepository(), notes);
        vm.MarkdownEnabled = false;

        vm.OpenNote(id);

        vm.ShowRead.Should().BeFalse();
        vm.ShowRendered.Should().BeFalse();
        vm.ShowToolbar.Should().BeFalse();
        vm.ShowEdit.Should().BeTrue();
        vm.ShowMarkdownConversion.Should().BeFalse();
        vm.EditorBody.Should().Be("**원문**");
        notes.Get(id)!.BodyFormat.Should().Be("markdown");
        vm.SetReadModeCommand.CanExecute(null).Should().BeFalse();
        vm.SetRenderedModeCommand.CanExecute(null).Should().BeFalse();

        vm.MarkdownEnabled = true;
        vm.SetReadModeCommand.Execute(null);
        vm.ShowRead.Should().BeTrue();
    }

    [Fact]
    public void DisablingMarkdown_closes_active_preview_and_blocks_conversion()
    {
        var notes = new FakeNoteRepository();
        var now = DateTimeOffset.UtcNow;
        var id = notes.Create(new Note { Type = NoteType.Plain, Body = "메모", BodyFormat = "markdown", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(new FakeGroupRepository(), notes);
        vm.OpenNote(id);
        vm.ShowRead.Should().BeTrue();

        vm.MarkdownEnabled = false;

        vm.ShowRead.Should().BeFalse();
        vm.ShowEdit.Should().BeTrue();
        vm.ConvertToMarkdownCommand.CanExecute(null).Should().BeFalse();
        vm.ConvertToMarkdownWithLoadingCommand.CanExecute(null).Should().BeFalse();
        vm.NewPlainNoteCommand.Execute(null);
        // 이 테스트는 사이드바를 초기화하지 않으므로 화면 선택 상태 대신
        // 실제 저장된 새 메모를 검사한다. 기존 Markdown 서식도 보존돼야 한다.
        notes.Items.Should().HaveCount(2);
        notes.Items.Should().ContainSingle(note => note.Id != id)
            .Which.BodyFormat.Should().Be("plain");
        notes.Get(id)!.BodyFormat.Should().Be("markdown");
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

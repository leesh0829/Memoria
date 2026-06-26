using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core;
using Memoria.Core.Models;
using Memoria.Tests.Fakes;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class TrashViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 26, 0, 0, 0, TimeSpan.Zero);

    private static (TrashViewModel vm, FakeNoteRepository notes, FakeSettingsRepository settings) CreateSut()
    {
        var notes = new FakeNoteRepository { Clock = new FixedTimeProvider(Now) };
        var settings = new FakeSettingsRepository();
        var vm = new TrashViewModel(notes, settings, new FixedTimeProvider(Now));
        return (vm, notes, settings);
    }

    [Fact]
    public void RetentionDays_defaults_to_30_when_setting_absent()
    {
        var (vm, _, _) = CreateSut();
        vm.RetentionDays.Should().Be(30);
    }

    [Fact]
    public void RetentionDays_reads_setting()
    {
        var (vm, _, settings) = CreateSut();
        settings.Set(SettingsKeys.TrashRetentionDays, "14");
        vm.RetentionDays.Should().Be(14);
    }

    [Fact]
    public void Load_lists_only_trashed_notes()
    {
        var (vm, notes, _) = CreateSut();
        var trashedId = notes.Create(new Note { Type = NoteType.Plain, Title = "삭제됨" });
        notes.Create(new Note { Type = NoteType.Plain, Title = "활성" });
        notes.SoftDelete(trashedId);

        vm.Load();

        vm.Items.Should().HaveCount(1);
        vm.Items[0].Id.Should().Be(trashedId);
        vm.Items[0].DisplayTitle.Should().Be("삭제됨");
    }

    [Fact]
    public void DeleteNote_soft_deletes_and_sets_undo_state()
    {
        var (vm, notes, _) = CreateSut();
        var id = notes.Create(new Note { Type = NoteType.Plain, Title = "메모" });

        vm.DeleteNote(id);

        notes.Get(id)!.DeletedAt.Should().NotBeNull();
        vm.IsUndoAvailable.Should().BeTrue();
        vm.UndoMessage.Should().NotBeNullOrEmpty();
        vm.UndoCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Undo_restores_last_deleted_and_clears_state()
    {
        var (vm, notes, _) = CreateSut();
        var id = notes.Create(new Note { Type = NoteType.Plain, Title = "메모" });
        vm.DeleteNote(id);

        vm.Undo();

        notes.Get(id)!.DeletedAt.Should().BeNull();
        vm.IsUndoAvailable.Should().BeFalse();
        vm.UndoMessage.Should().BeNull();
        vm.UndoCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Undo_is_noop_without_pending_deletion()
    {
        var (vm, notes, _) = CreateSut();
        var id = notes.Create(new Note { Type = NoteType.Plain });
        notes.SoftDelete(id);

        vm.Undo(); // 대기 중인 삭제 없음

        notes.Get(id)!.DeletedAt.Should().NotBeNull(); // 복원되지 않음
    }
}

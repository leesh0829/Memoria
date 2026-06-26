using System;
using System.IO;
using FluentAssertions;
using Memoria.App.Services;
using Xunit;

namespace Memoria.Tests.App;

public class RecoveryJournalTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "memoria-rec-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Append_then_DetectPending_returns_last_snapshot_per_note()
    {
        var journal = new RecoveryJournal(TempDir());
        journal.Append(new RecoverySnapshot(7, "T", "B1", DateTimeOffset.UnixEpoch));
        journal.Append(new RecoverySnapshot(7, "T", "B2", DateTimeOffset.UnixEpoch));

        var pending = journal.DetectPending();

        pending.Should().ContainSingle();
        pending[0].NoteId.Should().Be(7);
        pending[0].Body.Should().Be("B2");
    }

    [Fact]
    public void Clear_removes_pending_for_note()
    {
        var journal = new RecoveryJournal(TempDir());
        journal.Append(new RecoverySnapshot(3, null, "x", DateTimeOffset.UnixEpoch));

        journal.Clear(3);

        journal.DetectPending().Should().BeEmpty();
    }

    [Fact]
    public void DetectPending_returns_one_per_note_across_files()
    {
        var journal = new RecoveryJournal(TempDir());
        journal.Append(new RecoverySnapshot(1, null, "a", DateTimeOffset.UnixEpoch));
        journal.Append(new RecoverySnapshot(2, null, "b", DateTimeOffset.UnixEpoch));

        journal.DetectPending().Should().HaveCount(2);
    }
}

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

    [Fact]
    public void DetectPending_skips_partial_final_line_and_returns_last_valid_snapshot()
    {
        var dir = TempDir();
        var journal = new RecoveryJournal(dir);
        journal.Append(new RecoverySnapshot(5, "T", "valid-body", DateTimeOffset.UnixEpoch));

        // Simulate a crash mid-write: the final journal line is a truncated/partial
        // JSON fragment. DetectPending must NOT throw on this corrupt content.
        File.AppendAllText(Path.Combine(dir, "5.json"), "{\"NoteId\":5,\"Body\":\"unfini");

        Action act = () => journal.DetectPending();
        act.Should().NotThrow();

        var pending = journal.DetectPending();
        pending.Should().ContainSingle();
        pending[0].NoteId.Should().Be(5);
        pending[0].Body.Should().Be("valid-body");
    }

    [Fact]
    public void DetectPending_skips_note_whose_only_line_is_corrupt()
    {
        var dir = TempDir();
        var journal = new RecoveryJournal(dir);
        File.AppendAllText(Path.Combine(dir, "9.json"), "{\"NoteId\":9,\"Body\":\"unfini");

        Action act = () => journal.DetectPending();
        act.Should().NotThrow();
        journal.DetectPending().Should().BeEmpty();
    }
}

using System;
using System.Collections.Generic;
using Memoria.App.Services;

namespace Memoria.Tests.App.Fakes;

internal sealed class FakeRecoveryJournal : IRecoveryJournal
{
    public List<RecoverySnapshot> Appended { get; } = new();
    public List<int> Cleared { get; } = new();
    public List<RecoverySnapshot> Pending { get; } = new();

    public void Append(RecoverySnapshot snapshot) => Appended.Add(snapshot);
    public void Clear(int noteId) => Cleared.Add(noteId);
    public IReadOnlyList<RecoverySnapshot> DetectPending() => Pending;
}

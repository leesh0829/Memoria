using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Memoria.App.Services;

public sealed class RecoveryJournal : IRecoveryJournal
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };
    private readonly string _dir;

    public RecoveryJournal(string recoveryDirectory)
    {
        _dir = recoveryDirectory;
        Directory.CreateDirectory(_dir);
    }

    private string PathFor(int noteId) => Path.Combine(_dir, $"{noteId}.json");

    public void Append(RecoverySnapshot snapshot)
    {
        var line = JsonSerializer.Serialize(snapshot, JsonOpts);
        File.AppendAllText(PathFor(snapshot.NoteId), line + Environment.NewLine);
    }

    public void Clear(int noteId)
    {
        var path = PathFor(noteId);
        if (File.Exists(path)) File.Delete(path);
    }

    public IReadOnlyList<RecoverySnapshot> DetectPending()
    {
        if (!Directory.Exists(_dir)) return Array.Empty<RecoverySnapshot>();

        var result = new List<RecoverySnapshot>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            string? last = null;
            foreach (var line in File.ReadLines(file))
                if (!string.IsNullOrWhiteSpace(line)) last = line;

            if (last is null) continue;
            var snap = JsonSerializer.Deserialize<RecoverySnapshot>(last, JsonOpts);
            if (snap is not null) result.Add(snap);
        }
        return result;
    }
}

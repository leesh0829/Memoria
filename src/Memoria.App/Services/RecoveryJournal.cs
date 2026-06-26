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
            // 크래시(이 기능이 대비하는 바로 그 상황)는 마지막 줄을 부분/손상 상태로
            // 남길 수 있다. 줄별로 역직렬화를 시도하며 손상 줄은 건너뛰고, 노트별로
            // 마지막으로 성공한 스냅샷을 채택한다. 잘못된 내용에 대해 절대 throw 하지 않는다.
            RecoverySnapshot? lastValid = null;
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var snap = JsonSerializer.Deserialize<RecoverySnapshot>(line, JsonOpts);
                    if (snap is not null) lastValid = snap;
                }
                catch (JsonException)
                {
                    // 부분/손상 줄(크래시로 인한 미완성 기록) — 건너뛴다.
                }
            }

            if (lastValid is not null) result.Add(lastValid);
        }
        return result;
    }
}

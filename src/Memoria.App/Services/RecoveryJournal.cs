using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Memoria.App.Services;

public sealed class RecoveryJournal : IRecoveryJournal, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };
    private static readonly TimeSpan FlushDelay = TimeSpan.FromMilliseconds(150);
    private readonly string _dir;
    private readonly object _gate = new();
    private readonly object _flushGate = new();
    private readonly Dictionary<int, PendingWrite> _pending = new();
    private readonly Dictionary<int, long> _versions = new();
    private readonly Timer _timer;
    private bool _disposed;

    private sealed record PendingWrite(RecoverySnapshot Snapshot, long Version);

    public RecoveryJournal(string recoveryDirectory)
    {
        _dir = recoveryDirectory;
        Directory.CreateDirectory(_dir);
        _timer = new Timer(static state => ((RecoveryJournal)state!).FlushPending(), this,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private string PathFor(int noteId) => Path.Combine(_dir, $"{noteId}.json");

    public void Append(RecoverySnapshot snapshot)
    {
        // 이 메서드는 키 입력마다 UI 스레드에서 호출된다. 스냅샷만 메모리에 교체하고
        // 실제 직렬화/파일 I/O는 짧게 디바운스한 ThreadPool 콜백에서 수행한다.
        lock (_gate)
        {
            if (_disposed) return;
            var version = _versions.TryGetValue(snapshot.NoteId, out var current) ? current + 1 : 1;
            _versions[snapshot.NoteId] = version;
            _pending[snapshot.NoteId] = new PendingWrite(snapshot, version);
            _timer.Change(FlushDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void FlushPending()
    {
        // Timer, DetectPending, Dispose가 동시에 들어와도 같은 배치를 중복 기록하지 않는다.
        lock (_flushGate)
        {
            List<PendingWrite> batch;
            lock (_gate)
            {
                if (_pending.Count == 0) return;
                batch = new List<PendingWrite>(_pending.Values);
                _pending.Clear();
            }

            foreach (var write in batch)
                WriteLatest(write);
        }
    }

    private void WriteLatest(PendingWrite write)
    {
        var noteId = write.Snapshot.NoteId;
        var tempPath = Path.Combine(_dir, $".{noteId}.{write.Version}.tmp");
        try
        {
            // 기존 append-only 파일은 본문 전체를 키 입력 횟수만큼 누적해 디스크 사용량과
            // 다음 시작의 복구 스캔 비용을 계속 키웠다. 최신 한 건만 원자적으로 교체한다.
            var line = JsonSerializer.Serialize(write.Snapshot, JsonOpts) + Environment.NewLine;
            File.WriteAllText(tempPath, line);

            lock (_gate)
            {
                // 쓰는 동안 Clear 또는 더 최신 Append가 발생했다면 오래된 임시 파일을 버린다.
                if (_versions.TryGetValue(noteId, out var current) && current == write.Version)
                    File.Move(tempPath, PathFor(noteId), overwrite: true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("RecoveryJournal.Flush", ex);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (Exception ex) { AppLog.Error("RecoveryJournal.TempCleanup", ex); }
        }
    }

    public void Clear(int noteId)
    {
        try
        {
            lock (_gate)
            {
                // 진행 중인 백그라운드 쓰기도 version 검사에서 폐기되게 한다.
                _versions[noteId] = _versions.TryGetValue(noteId, out var current) ? current + 1 : 1;
                _pending.Remove(noteId);
                var path = PathFor(noteId);
                if (File.Exists(path)) File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("RecoveryJournal.Clear", ex);
        }
    }

    public IReadOnlyList<RecoverySnapshot> DetectPending()
    {
        // 테스트뿐 아니라 향후 같은 프로세스에서 호출될 때도 아직 디바운스 중인 최신값을 포함한다.
        FlushPending();
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

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        // 정상 종료 직전 아직 DB 저장으로 Clear되지 않은 스냅샷도 유실하지 않는다.
        FlushPending();
        _timer.Dispose();
    }
}

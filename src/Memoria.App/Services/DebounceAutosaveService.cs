using System;
using System.Collections.Generic;
using System.Threading;

namespace Memoria.App.Services;

public sealed class DebounceAutosaveService : IAutosaveService, IDisposable
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly Dictionary<int, Action<AutosaveSnapshot>> _saves = new();
    private readonly ITimer _timer;
    // 보류 중인 노트별 최신 스냅샷(변경 시점에 캡처). 존재 여부가 곧 '보류 중'을 의미한다.
    private readonly Dictionary<int, AutosaveSnapshot> _pending = new();
    private readonly Dictionary<int, DateTimeOffset> _dueAt = new();
    private bool _disposed;

    public DebounceAutosaveService(TimeProvider timeProvider, int debounceMs)
    {
        _time = timeProvider;
        _debounce = TimeSpan.FromMilliseconds(debounceMs);
        // 키 입력마다 Timer를 Dispose/Create하지 않고 모든 노트가 하나의 타이머를 공유한다.
        // 이미 큐에 오른 콜백도 dueAt을 다시 검사하므로 디바운스 리셋을 깨뜨리지 않는다.
        _timer = _time.CreateTimer(_ => FireDue(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Register(int noteId, Action<AutosaveSnapshot> saveAction)
    {
        lock (_gate) { _saves[noteId] = saveAction; }
    }

    public void Unregister(int noteId)
    {
        lock (_gate)
        {
            _saves.Remove(noteId);
            _pending.Remove(noteId);
            _dueAt.Remove(noteId);
            ScheduleNextLocked();
        }
    }

    public void NotifyChanged(int noteId, AutosaveSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!_saves.ContainsKey(noteId)) return;
            _pending[noteId] = snapshot;   // 변경 시점 스냅샷 보관(노트 간 오염 방지)
            _dueAt[noteId] = _time.GetUtcNow() + _debounce;
            ScheduleNextLocked();
        }
    }

    private void FireDue()
    {
        var toRun = new List<(Action<AutosaveSnapshot> Action, AutosaveSnapshot Snapshot)>();
        lock (_gate)
        {
            if (_disposed) return;
            var now = _time.GetUtcNow();
            var dueIds = _dueAt.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();
            foreach (var noteId in dueIds)
            {
                _dueAt.Remove(noteId);
                if (_pending.Remove(noteId, out var snap) && _saves.TryGetValue(noteId, out var action))
                    toRun.Add((action, snap));
            }
            ScheduleNextLocked();
        }
        foreach (var (action, snapshot) in toRun) Invoke(action, snapshot);
    }

    private void ScheduleNextLocked()
    {
        if (_disposed) return;
        if (_dueAt.Count == 0)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }
        var delay = _dueAt.Values.Min() - _time.GetUtcNow();
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        _timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    public void FlushAll()
    {
        var toRun = new List<(Action<AutosaveSnapshot> Action, AutosaveSnapshot Snapshot)>();
        lock (_gate)
        {
            if (_disposed) return;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            foreach (var kv in _pending)
                if (_saves.TryGetValue(kv.Key, out var a)) toRun.Add((a, kv.Value));
            _pending.Clear();
            _dueAt.Clear();
        }
        foreach (var (action, snapshot) in toRun) Invoke(action, snapshot);
    }

    // 저장 콜백을 예외로부터 격리한다: 한 노트의 저장 실패가 ThreadPool 미처리 예외로
    // 프로세스를 종료시키지 않게 한다(복구 저널이 백스톱).
    private static void Invoke(Action<AutosaveSnapshot>? action, AutosaveSnapshot? snapshot)
    {
        if (action is null || snapshot is null) return;
        try { action(snapshot); }
        catch (Exception ex) { AppLog.Error("Autosave", ex); }
    }

    public void Dispose()
    {
        lock (_gate)
            if (_disposed) return;
        FlushAll();
        lock (_gate) _disposed = true;
        _timer.Dispose();
    }
}

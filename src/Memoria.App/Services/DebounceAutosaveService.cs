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
    private readonly Dictionary<int, ITimer> _timers = new();
    // 보류 중인 노트별 최신 스냅샷(변경 시점에 캡처). 존재 여부가 곧 '보류 중'을 의미한다.
    private readonly Dictionary<int, AutosaveSnapshot> _pending = new();
    // noteId 별 현재 예약된 타이머의 세대(generation). 리셋마다 증가시켜,
    // 교체된(Dispose 됐지만 ThreadPool 에 이미 올라간) 타이머의 stale 콜백을 식별한다.
    private readonly Dictionary<int, long> _generation = new();

    public DebounceAutosaveService(TimeProvider timeProvider, int debounceMs)
    {
        _time = timeProvider;
        _debounce = TimeSpan.FromMilliseconds(debounceMs);
    }

    public void Register(int noteId, Action<AutosaveSnapshot> saveAction)
    {
        lock (_gate) { _saves[noteId] = saveAction; }
    }

    public void Unregister(int noteId)
    {
        lock (_gate)
        {
            if (_timers.TryGetValue(noteId, out var t)) { t.Dispose(); _timers.Remove(noteId); }
            _saves.Remove(noteId);
            _pending.Remove(noteId);
        }
    }

    public void NotifyChanged(int noteId, AutosaveSnapshot snapshot)
    {
        lock (_gate)
        {
            if (!_saves.ContainsKey(noteId)) return;
            _pending[noteId] = snapshot;   // 변경 시점 스냅샷 보관(노트 간 오염 방지)
            if (_timers.TryGetValue(noteId, out var existing)) existing.Dispose();
            var generation = _generation.TryGetValue(noteId, out var g) ? g + 1 : 1;
            _generation[noteId] = generation;
            _timers[noteId] = _time.CreateTimer(_ => Fire(noteId, generation), null, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire(int noteId, long generation)
    {
        Action<AutosaveSnapshot>? action = null;
        AutosaveSnapshot? snapshot = null;
        lock (_gate)
        {
            // 현재 예약된 콜백만 진행. 리셋으로 교체된 타이머의 stale 콜백
            // (Dispose 전 이미 큐에 올라가 뒤늦게 발화)은 여기서 무시되어
            // 디바운스 리셋을 깨뜨리지 못한다.
            if (!_generation.TryGetValue(noteId, out var current) || current != generation) return;
            if (_timers.TryGetValue(noteId, out var t)) { t.Dispose(); _timers.Remove(noteId); }
            if (_pending.Remove(noteId, out var snap) && _saves.TryGetValue(noteId, out var a))
            {
                action = a;
                snapshot = snap;
            }
        }
        Invoke(action, snapshot);
    }

    public void FlushAll()
    {
        var toRun = new List<(Action<AutosaveSnapshot> Action, AutosaveSnapshot Snapshot)>();
        lock (_gate)
        {
            foreach (var kv in _timers) kv.Value.Dispose();
            _timers.Clear();
            foreach (var kv in _pending)
                if (_saves.TryGetValue(kv.Key, out var a)) toRun.Add((a, kv.Value));
            _pending.Clear();
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

    public void Dispose() => FlushAll();
}

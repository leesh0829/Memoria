using System;
using System.Collections.Generic;
using System.Threading;

namespace Memoria.App.Services;

public sealed class DebounceAutosaveService : IAutosaveService, IDisposable
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly Dictionary<int, Action> _saves = new();
    private readonly Dictionary<int, ITimer> _timers = new();
    private readonly HashSet<int> _pending = new();
    // noteId 별 현재 예약된 타이머의 세대(generation). 리셋마다 증가시켜,
    // 교체된(Dispose 됐지만 ThreadPool 에 이미 올라간) 타이머의 stale 콜백을 식별한다.
    private readonly Dictionary<int, long> _generation = new();

    public DebounceAutosaveService(TimeProvider timeProvider, int debounceMs)
    {
        _time = timeProvider;
        _debounce = TimeSpan.FromMilliseconds(debounceMs);
    }

    public void Register(int noteId, Action saveAction)
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

    public void NotifyChanged(int noteId)
    {
        lock (_gate)
        {
            if (!_saves.ContainsKey(noteId)) return;
            _pending.Add(noteId);
            if (_timers.TryGetValue(noteId, out var existing)) existing.Dispose();
            var generation = _generation.TryGetValue(noteId, out var g) ? g + 1 : 1;
            _generation[noteId] = generation;
            _timers[noteId] = _time.CreateTimer(_ => Fire(noteId, generation), null, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire(int noteId, long generation)
    {
        Action? action = null;
        lock (_gate)
        {
            // 현재 예약된 콜백만 진행. 리셋으로 교체된 타이머의 stale 콜백
            // (Dispose 전 이미 큐에 올라가 뒤늦게 발화)은 여기서 무시되어
            // 디바운스 리셋을 깨뜨리지 못한다.
            if (!_generation.TryGetValue(noteId, out var current) || current != generation) return;
            if (_timers.TryGetValue(noteId, out var t)) { t.Dispose(); _timers.Remove(noteId); }
            if (_pending.Remove(noteId) && _saves.TryGetValue(noteId, out var a)) action = a;
        }
        action?.Invoke();
    }

    public void FlushAll()
    {
        var toRun = new List<Action>();
        lock (_gate)
        {
            foreach (var kv in _timers) kv.Value.Dispose();
            _timers.Clear();
            foreach (var id in _pending)
                if (_saves.TryGetValue(id, out var a)) toRun.Add(a);
            _pending.Clear();
        }
        foreach (var a in toRun) a();
    }

    public void Dispose() => FlushAll();
}

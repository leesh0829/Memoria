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
            _timers[noteId] = _time.CreateTimer(_ => Fire(noteId), null, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire(int noteId)
    {
        Action? action = null;
        lock (_gate)
        {
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

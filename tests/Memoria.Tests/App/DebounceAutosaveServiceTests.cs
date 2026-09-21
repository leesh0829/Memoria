using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Memoria.App.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class DebounceAutosaveServiceTests
{
    private static readonly AutosaveSnapshot Snap = new("t", "b");

    [Fact]
    public void Save_receives_snapshot_captured_at_notify_time()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        AutosaveSnapshot? received = null;
        svc.Register(1, s => received = s);

        svc.NotifyChanged(1, new AutosaveSnapshot("title-1", "body-1"));
        time.Advance(TimeSpan.FromMilliseconds(500));

        received.Should().Be(new AutosaveSnapshot("title-1", "body-1"));
    }

    [Fact]
    public void Each_note_saves_its_own_snapshot_without_cross_contamination()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        var saved = new Dictionary<int, AutosaveSnapshot>();
        svc.Register(1, s => saved[1] = s);
        svc.Register(2, s => saved[2] = s);

        svc.NotifyChanged(1, new AutosaveSnapshot(null, "note-1"));
        svc.NotifyChanged(2, new AutosaveSnapshot(null, "note-2"));
        time.Advance(TimeSpan.FromMilliseconds(500));

        saved[1].Body.Should().Be("note-1");
        saved[2].Body.Should().Be("note-2");
    }

    [Fact]
    public void Save_callback_exception_is_swallowed_and_does_not_propagate()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        svc.Register(1, _ => throw new InvalidOperationException("boom"));

        svc.NotifyChanged(1, Snap);
        // 콜백이 throw 해도 타이머 발화(Advance)가 예외를 전파하지 않아야 한다.
        var act = () => time.Advance(TimeSpan.FromMilliseconds(500));
        act.Should().NotThrow();
    }

    [Fact]
    public void FlushAll_swallows_save_callback_exception()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        svc.Register(1, _ => throw new InvalidOperationException("boom"));

        svc.NotifyChanged(1, Snap);
        var act = () => svc.FlushAll();
        act.Should().NotThrow();
    }

    [Fact]
    public void Save_fires_only_after_debounce_window_elapses()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        var saves = 0;
        svc.Register(1, _ => saves++);

        svc.NotifyChanged(1, Snap);
        time.Advance(TimeSpan.FromMilliseconds(300));
        saves.Should().Be(0);

        time.Advance(TimeSpan.FromMilliseconds(200));
        saves.Should().Be(1);
    }

    [Fact]
    public void Repeated_changes_reset_the_debounce_timer()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        var saves = 0;
        svc.Register(1, _ => saves++);

        svc.NotifyChanged(1, Snap);
        time.Advance(TimeSpan.FromMilliseconds(300));
        svc.NotifyChanged(1, Snap);                       // 타이머 리셋
        time.Advance(TimeSpan.FromMilliseconds(300));
        saves.Should().Be(0);                       // 리셋 후 아직 500ms 미경과

        time.Advance(TimeSpan.FromMilliseconds(200));
        saves.Should().Be(1);
    }

    [Fact]
    public void FlushAll_runs_pending_save_immediately()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        var saves = 0;
        svc.Register(1, _ => saves++);

        svc.NotifyChanged(1, Snap);
        svc.FlushAll();

        saves.Should().Be(1);
    }

    [Fact]
    public void NotifyChanged_without_registration_is_ignored()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);

        svc.NotifyChanged(99, Snap);
        time.Advance(TimeSpan.FromMilliseconds(1000));
        // 예외 없이 통과하면 성공.
    }

    [Fact]
    public void Reset_before_window_fires_save_exactly_once_after_last_change()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        var saves = 0;
        svc.Register(1, _ => saves++);

        svc.NotifyChanged(1, Snap);                          // t=0, 첫 변경 (원래 타이머 t=500 만료 예정)
        time.Advance(TimeSpan.FromMilliseconds(400));  // t=400, 윈도 이전
        svc.NotifyChanged(1, Snap);                          // t=400, 리셋 (새 타이머 t=900 만료 예정)

        time.Advance(TimeSpan.FromMilliseconds(200));  // t=600: 원래 타이머가 만료됐을 시점은 지났지만
        saves.Should().Be(0);                          // 리셋되었으므로 저장되면 안 됨

        time.Advance(TimeSpan.FromMilliseconds(300));  // t=900: 마지막 변경 + 윈도
        saves.Should().Be(1);

        time.Advance(TimeSpan.FromMilliseconds(1000));  // 추가 경과
        saves.Should().Be(1);                           // 정확히 1회만 (중복/유령 발화 없음)
    }

    // 공유 타이머의 이전 예약 콜백이 리셋 뒤 이미 실행 큐에 올라온 상황을
    // ManualTimeProvider 로 결정론적으로 재현한다.
    [Fact]
    public void Stale_inflight_timer_callback_after_reset_does_not_fire_save_early()
    {
        var time = new ManualTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        var saves = 0;
        svc.Register(1, _ => saves++);

        svc.NotifyChanged(1, Snap);                       // t=0, 첫 예약은 t=500
        time.Advance(TimeSpan.FromMilliseconds(400));
        svc.NotifyChanged(1, Snap);                       // t=400, 새 만료는 t=900

        time.Advance(TimeSpan.FromMilliseconds(100));     // t=500
        // 첫 예약에서 이미 큐에 오른 콜백이 뒤늦게 실행돼도 새 만료 전에는 저장 금지.
        time.Timers.Single().FireCallback();
        saves.Should().Be(0);

        time.Advance(TimeSpan.FromMilliseconds(400));     // t=900
        time.Timers.Single().FireCallback();
        saves.Should().Be(1);
        time.Timers.Should().ContainSingle();             // 키 입력마다 타이머를 만들지 않음
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public List<ManualTimer> Timers { get; } = new();

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;

        public ManualTimer(TimerCallback callback, object? state)
        {
            _callback = callback;
            _state = state;
        }

        // Dispose 여부와 무관하게 콜백을 호출 — 이미 큐에 올라간 in-flight 콜백을 모사.
        public void FireCallback() => _callback(_state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

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
    [Fact]
    public void Save_fires_only_after_debounce_window_elapses()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        var saves = 0;
        svc.Register(1, () => saves++);

        svc.NotifyChanged(1);
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
        svc.Register(1, () => saves++);

        svc.NotifyChanged(1);
        time.Advance(TimeSpan.FromMilliseconds(300));
        svc.NotifyChanged(1);                       // 타이머 리셋
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
        svc.Register(1, () => saves++);

        svc.NotifyChanged(1);
        svc.FlushAll();

        saves.Should().Be(1);
    }

    [Fact]
    public void NotifyChanged_without_registration_is_ignored()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);

        svc.NotifyChanged(99);
        time.Advance(TimeSpan.FromMilliseconds(1000));
        // 예외 없이 통과하면 성공.
    }

    [Fact]
    public void Reset_before_window_fires_save_exactly_once_after_last_change()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        var saves = 0;
        svc.Register(1, () => saves++);

        svc.NotifyChanged(1);                          // t=0, 첫 변경 (원래 타이머 t=500 만료 예정)
        time.Advance(TimeSpan.FromMilliseconds(400));  // t=400, 윈도 이전
        svc.NotifyChanged(1);                          // t=400, 리셋 (새 타이머 t=900 만료 예정)

        time.Advance(TimeSpan.FromMilliseconds(200));  // t=600: 원래 타이머가 만료됐을 시점은 지났지만
        saves.Should().Be(0);                          // 리셋되었으므로 저장되면 안 됨

        time.Advance(TimeSpan.FromMilliseconds(300));  // t=900: 마지막 변경 + 윈도
        saves.Should().Be(1);

        time.Advance(TimeSpan.FromMilliseconds(1000));  // 추가 경과
        saves.Should().Be(1);                           // 정확히 1회만 (중복/유령 발화 없음)
    }

    // FakeTimeProvider 는 Dispose 된 타이머를 즉시 제거하므로 ThreadPool 경합을
    // 재현하지 못한다. 리셋으로 교체된(Dispose 된) 타이머의 콜백이 이미 큐에
    // 올라가 발화하는 상황을 ManualTimeProvider 로 결정론적으로 재현한다.
    [Fact]
    public void Stale_inflight_timer_callback_after_reset_does_not_fire_save_early()
    {
        var time = new ManualTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        var saves = 0;
        svc.Register(1, () => saves++);

        svc.NotifyChanged(1);   // 타이머 A 생성 (Timers[0])
        svc.NotifyChanged(1);   // 리셋: A Dispose, 타이머 B 생성 (Timers[1])

        // A 의 콜백이 Dispose 직전 이미 ThreadPool 에 올라가 뒤늦게 발화하는 경우.
        // 가드가 stale 콜백을 무시해야 한다.
        time.Timers[0].FireCallback();
        saves.Should().Be(0);

        // 현재(마지막) 타이머 B 가 정상 발화 → 정확히 1회 저장.
        time.Timers[1].FireCallback();
        saves.Should().Be(1);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public List<ManualTimer> Timers { get; } = new();

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

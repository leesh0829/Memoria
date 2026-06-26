using System;
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
}

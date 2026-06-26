// tests/Memoria.Tests/Windows/SingleInstanceServiceTests.cs
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Memoria.App.Windows;
using Xunit;

namespace Memoria.Tests.Windows;

public class SingleInstanceServiceTests
{
    [Fact]
    public void First_instance_acquires_second_does_not()
    {
        var id = Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceService($"mtx-{id}", $"pipe-{id}");
        using var second = new SingleInstanceService($"mtx-{id}", $"pipe-{id}");

        first.TryAcquire().Should().BeTrue();
        second.TryAcquire().Should().BeFalse();
    }

    [Fact]
    public async Task Second_instance_signals_first_via_pipe()
    {
        var id = Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceService($"mtx-{id}", $"pipe-{id}");
        var tcs = new TaskCompletionSource<PipeCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.CommandReceived += (_, cmd) => tcs.TrySetResult(cmd);

        first.TryAcquire().Should().BeTrue();

        using var second = new SingleInstanceService($"mtx-{id}", $"pipe-{id}");
        second.TryAcquire().Should().BeFalse();
        second.SignalExistingInstance(PipeCommand.NewNote);

        var winner = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        winner.Should().Be(tcs.Task);
        (await tcs.Task).Should().Be(PipeCommand.NewNote);
    }

    // FINDING 1: Dispose must request cancellation and let the server loop unwind promptly
    // (the loop is blocked in WaitForConnectionAsync), without throwing.
    [Fact]
    public void Dispose_after_acquire_is_prompt_and_does_not_throw()
    {
        var id = Guid.NewGuid().ToString("N");
        var svc = new SingleInstanceService($"mtx-{id}", $"pipe-{id}");
        svc.TryAcquire().Should().BeTrue();

        var sw = Stopwatch.StartNew();
        Action dispose = () => svc.Dispose();
        dispose.Should().NotThrow();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    // FINDING 1: cancellation while the server is mid-read (client connected but idle) must
    // break the loop cleanly rather than fault it; Dispose stays prompt and never throws.
    [Fact]
    public void Dispose_while_client_connected_but_idle_is_prompt_and_does_not_throw()
    {
        var id = Guid.NewGuid().ToString("N");
        var svc = new SingleInstanceService($"mtx-{id}", $"pipe-{id}");
        svc.TryAcquire().Should().BeTrue();

        // Connect a client but never write — the server is now awaiting ReadLineAsync(token).
        using var client = new NamedPipeClientStream(".", $"pipe-{id}", PipeDirection.Out);
        client.Connect(2000);

        var sw = Stopwatch.StartNew();
        Action dispose = () => svc.Dispose();
        dispose.Should().NotThrow();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    // FINDING 1: the server loop must keep accepting connections across multiple client
    // sessions — a single completed session must not terminate second-instance detection.
    [Fact]
    public async Task Server_keeps_accepting_across_multiple_client_sessions()
    {
        var id = Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceService($"mtx-{id}", $"pipe-{id}");

        var received = new ConcurrentQueue<PipeCommand>();
        var signal = new SemaphoreSlim(0);
        first.CommandReceived += (_, cmd) => { received.Enqueue(cmd); signal.Release(); };

        first.TryAcquire().Should().BeTrue();

        using (var s1 = new SingleInstanceService($"mtx-{id}", $"pipe-{id}"))
            s1.SignalExistingInstance(PipeCommand.NewNote);
        (await signal.WaitAsync(5000)).Should().BeTrue("the first session must be received");

        using (var s2 = new SingleInstanceService($"mtx-{id}", $"pipe-{id}"))
            s2.SignalExistingInstance(PipeCommand.Open);
        (await signal.WaitAsync(5000)).Should().BeTrue("the loop must still accept a second session");

        received.Should().BeEquivalentTo(
            new[] { PipeCommand.NewNote, PipeCommand.Open },
            o => o.WithStrictOrdering());
    }

    // FINDING 1: a single broken/empty client connection must not kill the server —
    // the next genuine client must still be served.
    [Fact]
    public async Task Server_survives_broken_client_then_serves_next()
    {
        var id = Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceService($"mtx-{id}", $"pipe-{id}");

        var signal = new SemaphoreSlim(0);
        first.CommandReceived += (_, _) => signal.Release();

        first.TryAcquire().Should().BeTrue();

        // Broken/empty client: connect then close without writing a full line.
        using (var broken = new NamedPipeClientStream(".", $"pipe-{id}", PipeDirection.Out))
            broken.Connect(2000);

        // A genuine client must still be served, proving the loop survived.
        using (var good = new SingleInstanceService($"mtx-{id}", $"pipe-{id}"))
            good.SignalExistingInstance(PipeCommand.NewNote);

        (await signal.WaitAsync(5000)).Should().BeTrue("the server must keep serving after a broken client");
    }
}

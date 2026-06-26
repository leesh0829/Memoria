// tests/Memoria.Tests/Windows/SingleInstanceServiceTests.cs
using System;
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
}

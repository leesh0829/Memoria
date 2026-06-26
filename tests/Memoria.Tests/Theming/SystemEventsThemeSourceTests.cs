// tests/Memoria.Tests/Theming/SystemEventsThemeSourceTests.cs
using FluentAssertions;
using Memoria.App.Theming;
using Xunit;

namespace Memoria.Tests.Theming;

public class SystemEventsThemeSourceTests
{
    [Fact]
    public void IsLight_delegates_to_registry_without_throwing()
    {
        using var source = new SystemEventsThemeSource();
        var act = () => source.IsLight();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_unsubscribes_and_does_not_throw()
    {
        var source = new SystemEventsThemeSource();
        source.Invoking(s => s.Dispose()).Should().NotThrow();
        // 이중 Dispose 안전
        source.Invoking(s => s.Dispose()).Should().NotThrow();
    }
}

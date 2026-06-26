// tests/Memoria.Tests/Windows/GlobalHotkeyServiceTests.cs
using FluentAssertions;
using Memoria.App.Windows;
using Xunit;

namespace Memoria.Tests.Windows;

public class GlobalHotkeyServiceTests
{
    [Fact]
    public void Register_returns_false_for_unparseable_hotkey()
    {
        using var svc = new GlobalHotkeyService();
        svc.Register("not-a-hotkey").Should().BeFalse();
    }

    [Fact]
    public void Register_returns_false_for_empty_string()
    {
        using var svc = new GlobalHotkeyService();
        svc.Register("").Should().BeFalse();
    }

    [Fact]
    public void Dispose_without_registration_does_not_throw()
    {
        var svc = new GlobalHotkeyService();
        svc.Invoking(s => s.Dispose()).Should().NotThrow();
    }
}

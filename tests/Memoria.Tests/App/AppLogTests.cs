using FluentAssertions;
using Memoria.App.Services;

namespace Memoria.Tests.App;

public sealed class AppLogTests
{
    [Fact]
    public void Diagnostic_does_not_feed_itself_back_into_activity_snapshot()
    {
        var marker = $"diagnostic-{Guid.NewGuid():N}";

        AppLog.Diagnostic("test", marker);

        AppLog.CaptureActivitySnapshot().Should().NotContain(marker);
    }

    [Fact]
    public void Recent_action_is_bounded_even_when_details_are_huge()
    {
        var marker = $"action-{Guid.NewGuid():N}";
        AppLog.Action(marker, new string('x', 100_000));

        var snapshot = AppLog.CaptureActivitySnapshot();

        snapshot.Should().Contain(marker);
        snapshot.Length.Should().BeLessThan(8_000);
    }
}

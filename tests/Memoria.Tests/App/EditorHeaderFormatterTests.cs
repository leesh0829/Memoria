using System;
using FluentAssertions;
using Memoria.App.ViewModels;
using Xunit;

namespace Memoria.Tests.App;

public class EditorHeaderFormatterTests
{
    [Fact]
    public void Formats_created_and_updated_in_their_own_offset()
    {
        var created = new DateTimeOffset(2026, 6, 22, 14, 3, 0, TimeSpan.FromHours(9));
        var updated = new DateTimeOffset(2026, 6, 26, 9, 41, 0, TimeSpan.FromHours(9));

        EditorHeaderFormatter.Format(created, updated)
            .Should().Be("생성 2026-06-22 14:03 · 수정 2026-06-26 09:41");
    }
}

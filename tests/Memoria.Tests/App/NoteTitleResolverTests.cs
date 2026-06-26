using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.App;

public class NoteTitleResolverTests
{
    [Fact]
    public void Uses_title_when_present()
    {
        var note = new Note { Title = "  명시 제목 ", Body = "첫 줄" };
        NoteTitleResolver.Resolve(note).Should().Be("명시 제목");
    }

    [Fact]
    public void Falls_back_to_first_nonempty_body_line_when_title_blank()
    {
        var note = new Note { Title = "   ", Body = "\n\n  본문 첫 줄\n둘째 줄" };
        NoteTitleResolver.Resolve(note).Should().Be("본문 첫 줄");
    }

    [Fact]
    public void Returns_placeholder_when_title_and_body_empty()
    {
        var note = new Note { Title = null, Body = "" };
        NoteTitleResolver.Resolve(note).Should().Be("(제목 없음)");
    }
}

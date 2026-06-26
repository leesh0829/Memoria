// tests/Memoria.Tests/Windows/PipeMessageTests.cs
using FluentAssertions;
using Memoria.App.Windows;
using Xunit;

namespace Memoria.Tests.Windows;

public class PipeMessageTests
{
    [Theory]
    [InlineData(PipeCommand.NewNote, "new-note")]
    [InlineData(PipeCommand.Open, "open")]
    public void Serialize_produces_stable_wire_token(PipeCommand cmd, string expected)
    {
        PipeMessage.Serialize(cmd).Should().Be(expected);
    }

    [Theory]
    [InlineData(PipeCommand.NewNote)]
    [InlineData(PipeCommand.Open)]
    public void Roundtrips(PipeCommand cmd)
    {
        PipeMessage.TryParse(PipeMessage.Serialize(cmd), out var parsed).Should().BeTrue();
        parsed.Should().Be(cmd);
    }

    [Theory]
    [InlineData(" NEW-NOTE \r\n", PipeCommand.NewNote)]
    [InlineData("Open", PipeCommand.Open)]
    public void TryParse_is_case_insensitive_and_trims(string line, PipeCommand expected)
    {
        PipeMessage.TryParse(line, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("delete")]
    public void TryParse_rejects_unknown(string? line)
    {
        PipeMessage.TryParse(line, out _).Should().BeFalse();
    }
}

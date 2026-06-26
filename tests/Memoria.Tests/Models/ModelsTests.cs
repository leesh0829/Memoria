using FluentAssertions;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Models;

public class ModelsTests
{
    [Fact]
    public void Enums_HaveExpectedMembers()
    {
        Enum.GetNames<NoteType>().Should().BeEquivalentTo("Plain", "Checklist", "WeeklyReport");
        Enum.GetNames<ItemKind>().Should().BeEquivalentTo("Task", "Issue");
        Enum.GetNames<ReportFormatKind>().Should().BeEquivalentTo("A", "B");
        Enum.GetNames<ThemeMode>().Should().BeEquivalentTo("Light", "Dark", "System");
    }

    [Fact]
    public void Note_DefaultsAndAssignment_Work()
    {
        var note = new Note
        {
            Type = NoteType.Checklist,
            LogDate = new DateOnly(2026, 6, 22),
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

        note.GroupId.Should().BeNull();
        note.DeletedAt.Should().BeNull();
        note.Type.Should().Be(NoteType.Checklist);
        note.LogDate.Should().Be(new DateOnly(2026, 6, 22));
    }

    [Fact]
    public void Client_DefaultEnabled_IsTrue()
    {
        new Client().Enabled.Should().BeTrue();
    }
}

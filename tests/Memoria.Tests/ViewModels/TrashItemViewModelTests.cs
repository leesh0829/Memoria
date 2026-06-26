using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class TrashItemViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DaysUntilPurge_ceils_remaining_days()
    {
        var note = new Note { Id = 1, Title = "T", DeletedAt = Now.AddDays(-5) };
        var vm = new TrashItemViewModel(note, retentionDays: 30, now: Now);

        vm.DaysUntilPurge.Should().Be(25);
        vm.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void DaysUntilPurge_is_zero_and_expired_when_past_retention()
    {
        var note = new Note { Id = 2, Title = "T", DeletedAt = Now.AddDays(-31) };
        var vm = new TrashItemViewModel(note, retentionDays: 30, now: Now);

        vm.DaysUntilPurge.Should().Be(0);
        vm.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void DisplayTitle_falls_back_when_blank()
    {
        var note = new Note { Id = 3, Title = "   ", DeletedAt = Now };
        var vm = new TrashItemViewModel(note, retentionDays: 30, now: Now);

        vm.DisplayTitle.Should().Be("(제목 없음)");
        vm.Id.Should().Be(3);
    }
}

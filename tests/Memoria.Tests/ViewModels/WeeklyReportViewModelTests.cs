// tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;

namespace Memoria.Tests.ViewModels;

public class WeeklyReportViewModelTests
{
    private static (WeeklyReportViewModel vm, FakeWeeklyReportService svc, FakeNoteRepository notes,
        FakeClientRepository clients, FakeGroupRepository groups, FakeSettingsRepository settings,
        FakeClipboardService clip, FakeConfirmationDialogService dlg) CreateSut(DateTimeOffset? now = null)
    {
        var svc = new FakeWeeklyReportService();
        var notes = new FakeNoteRepository();
        var clients = new FakeClientRepository();
        var groups = new FakeGroupRepository
        {
            Groups = { new Group { Id = 2, Name = "주간보고", IsSystem = true, SortOrder = 1 } }
        };
        var settings = new FakeSettingsRepository();
        var clip = new FakeClipboardService();
        var dlg = new FakeConfirmationDialogService();
        var vm = new WeeklyReportViewModel(
            svc, new FakeWeekCalculator(), notes, clients, groups, settings, clip, dlg,
            new FixedTimeProvider(now ?? new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero)));
        return (vm, svc, notes, clients, groups, settings, clip, dlg);
    }

    [Fact]
    public void Default_week_is_current_week_monday_to_friday()
    {
        // 2026-06-24 == 수요일 → 그 주 월 06/22, 금 06/26
        var (vm, _, _, _, _, _, _, _) = CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));

        vm.SelectedDate.Should().Be(new DateOnly(2026, 6, 24));
        vm.WeekStart.Should().Be(new DateOnly(2026, 6, 22));
        vm.WeekEnd.Should().Be(new DateOnly(2026, 6, 26));
        vm.WeekRangeLabel.Should().Be("06/22 ~ 06/26");
        vm.SelectedFormat.Should().Be(ReportFormatKind.A);
    }

    [Fact]
    public void Changing_selected_date_recomputes_week_range()
    {
        var (vm, _, _, _, _, _, _, _) = CreateSut();

        vm.SelectedDate = new DateOnly(2026, 1, 1); // 목요일 → 월 2025-12-29, 금 2026-01-02

        vm.WeekStart.Should().Be(new DateOnly(2025, 12, 29));
        vm.WeekEnd.Should().Be(new DateOnly(2026, 1, 2));
        vm.WeekRangeLabel.Should().Be("12/29 ~ 01/02");
    }
}

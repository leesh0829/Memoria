using FluentAssertions;
using Memoria.App.ViewModels;
using System.Threading.Tasks;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelWeeklyReportTests
{
    [Fact]
    public void OpenWeeklyReport_hosts_a_weekly_report_view_model()
    {
        var (vm, _, _, _) = MainViewModelEditorHostTests.Build();

        vm.OpenWeeklyReportCommand.Execute(null);

        vm.CurrentEditor.Should().BeOfType<WeeklyReportViewModel>();
    }

    [Fact]
    public async Task LoadWeeklyReport_hosts_editor_and_clears_busy_state()
    {
        var (vm, _, _, _) = MainViewModelEditorHostTests.Build();

        await vm.LoadWeeklyReportCommand.ExecuteAsync(null);

        vm.CurrentEditor.Should().BeOfType<WeeklyReportViewModel>();
        vm.IsBusy.Should().BeFalse();
    }
}

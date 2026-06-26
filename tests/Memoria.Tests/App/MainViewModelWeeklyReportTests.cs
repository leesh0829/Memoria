using FluentAssertions;
using Memoria.App.ViewModels;
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
}

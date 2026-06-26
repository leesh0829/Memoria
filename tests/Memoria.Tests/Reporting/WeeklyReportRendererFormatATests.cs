using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Xunit;

namespace Memoria.Tests.Reporting;

public class WeeklyReportRendererFormatATests
{
    private readonly IWeeklyReportRenderer _sut = new WeeklyReportRenderer();

    [Fact]
    public void FormatA_Golden_HasBlankLineBetweenTasksAndIssues()
    {
        var data = new WeeklyReportData(
            Tasks:
            [
                new ReportTask("task1", null, false),
                new ReportTask("task2", null, false),
            ],
            Issues:
            [
                new ReportIssue("issue1"),
                new ReportIssue("issue2"),
            ]);
        var options = new ReportRenderOptions();

        var text = _sut.Render(ReportFormatKind.A, data, options);

        const string expected =
            "[업무 내용]\n\t* task1\n\t* task2\n\n[이슈]\n\t* issue1\n\t* issue2";
        text.Should().Be(expected);
    }

    [Fact]
    public void FormatA_IncludeDoneOnly_FiltersTasksButKeepsAllIssues()
    {
        var data = new WeeklyReportData(
            Tasks:
            [
                new ReportTask("done task", null, true),
                new ReportTask("open task", null, false),
            ],
            Issues: [new ReportIssue("issue1")]);
        var options = new ReportRenderOptions { IncludeDoneOnly = true };

        var text = _sut.Render(ReportFormatKind.A, data, options);

        const string expected = "[업무 내용]\n\t* done task\n\n[이슈]\n\t* issue1";
        text.Should().Be(expected);
    }
}

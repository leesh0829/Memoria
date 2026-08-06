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

    [Fact]
    public void FormatA_PrependsClientName_ForClassifiedTask()
    {
        // 직접 생성 경로: 체크리스트 항목의 텍스트엔 회사명이 없고 ClientId로만 분류됨.
        var sld = new Client { Id = 1, Name = "SLD", SortOrder = 1, Enabled = true };
        var data = new WeeklyReportData(
            Tasks: [new ReportTask("비전회의", 1, true)],
            Issues: []);
        var options = new ReportRenderOptions { Clients = [sld] };

        var text = _sut.Render(ReportFormatKind.A, data, options);

        text.Should().Be("[업무 내용]\n\t* SLD 비전회의\n\n[이슈]");
    }

    [Fact]
    public void FormatA_DoesNotDoublePrefix_WhenTextAlreadyStartsWithClientName()
    {
        // 구글시트 경로: 텍스트에 이미 회사명이 포함되어 분류됨 → 접두어를 중복으로 붙이지 않는다.
        var sld = new Client { Id = 1, Name = "SLD", SortOrder = 1, Enabled = true };
        var data = new WeeklyReportData(
            Tasks: [new ReportTask("SLD 비전회의", 1, true)],
            Issues: []);
        var options = new ReportRenderOptions { Clients = [sld] };

        var text = _sut.Render(ReportFormatKind.A, data, options);

        text.Should().Be("[업무 내용]\n\t* SLD 비전회의\n\n[이슈]");
    }
}

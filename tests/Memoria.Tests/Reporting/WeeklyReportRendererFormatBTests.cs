using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Xunit;

namespace Memoria.Tests.Reporting;

public class WeeklyReportRendererFormatBTests
{
    private readonly IWeeklyReportRenderer _sut = new WeeklyReportRenderer();

    private static IReadOnlyList<Client> DisplayClients() =>
    [
        new() { Id = 1, Name = "SLD", SortOrder = 1, Enabled = true },
        new() { Id = 2, Name = "MTP", SortOrder = 2, Enabled = true },
        new() { Id = 3, Name = "코모텍", SortOrder = 3, Enabled = true },
        new() { Id = 4, Name = "충북테크놀로지파크", SortOrder = 4, Enabled = true },
        new() { Id = 5, Name = "자율형 공장", SortOrder = 5, Enabled = true },
        new() { Id = 6, Name = "카본센스", SortOrder = 6, Enabled = true },
    ];

    [Fact]
    public void FormatB_Golden_WithUnclassifiedSection()
    {
        var data = new WeeklyReportData(
            Tasks:
            [
                new ReportTask("SLD 점검", 1, false),
                new ReportTask("코모텍 미팅", 3, false),
                new ReportTask("자율형공장 라인 셋업", 5, false),
                new ReportTask("기타 정리", null, false),
            ],
            Issues:
            [
                new ReportIssue("장비 오류"),
                new ReportIssue("일정 지연"),
            ]);

        var options = new ReportRenderOptions
        {
            ReporterName = "이승현",
            WeekStart = new DateOnly(2026, 6, 23),
            WeekEnd = new DateOnly(2026, 6, 27),
            Clients = DisplayClients(),
        };

        var text = _sut.Render(ReportFormatKind.B, data, options);

        const string expected =
            "[ 이승현 주간 보고 (06/23 ~ 06/27) ]:\n" +
            "\n" +
            "[ SLD ]\n" +
            "\t* SLD 점검\n" +
            "\n" +
            "[ MTP ]\n" +
            "\n" +
            "[ 코모텍 ]\n" +
            "\t* 코모텍 미팅\n" +
            "\n" +
            "[ 충북테크놀로지파크 ]\n" +
            "\n" +
            "[ 자율형 공장 ]\n" +
            "\t* 자율형공장 라인 셋업\n" +
            "\n" +
            "[ 카본센스 ]\n" +
            "\n" +
            "[ 미분류 ]\n" +
            "\t* 기타 정리\n" +
            "\n" +
            "* 이슈사항:\n" +
            "\t* 장비 오류\n" +
            "\t* 일정 지연";
        text.Should().Be(expected);
    }

    [Fact]
    public void FormatB_OmitsUnclassifiedSection_WhenNoneUnclassified()
    {
        var data = new WeeklyReportData(
            Tasks: [new ReportTask("SLD 점검", 1, false)],
            Issues: []);
        var options = new ReportRenderOptions
        {
            WeekStart = new DateOnly(2026, 6, 22),
            WeekEnd = new DateOnly(2026, 6, 26),
            Clients = DisplayClients(),
        };

        var text = _sut.Render(ReportFormatKind.B, data, options);

        text.Should().NotContain("[ 미분류 ]");
        text.Should().Contain("[ 이승현 주간 보고 (06/22 ~ 06/26) ]:");
        text.Should().EndWith("* 이슈사항:");
    }
}

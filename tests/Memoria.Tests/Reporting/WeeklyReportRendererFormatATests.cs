using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.Tests.Reporting;

public class WeeklyReportRendererFormatATests
{
    private readonly WeeklyReportRenderer _renderer = new();
    private static readonly Client[] Clients =
    [
        new() { Id = 1, Name = "SLD" },
        new() { Id = 2, Name = "MTP" },
        new() { Id = 3, Name = "SLD 자율형공장" },
    ];

    [Fact]
    public void GroupsClients_UsesHyphensAliasesAndActualCompletionDates()
    {
        var data = new WeeklyReportData(
        [
            new("SLD 점검", 1, true, new(2026, 10, 15)),
            new("MTP 개발", 2, false),
            new("SLD 자율형공장 회의", 3, true, new(2026, 10, 14)),
            new("설계", 1, false),
        ], [new("장비 이슈")]);
        var text = _renderer.Render(ReportFormatKind.A, data, new() { Clients = Clients });
        text.Should().Be("[업무 내용]\n[ MES ]\n\t- 점검 [완료일자: 10/15]\n\t- 설계 [완료일자: --/--]"
            + "\n[ MTP ]\n\t- 개발 [완료일자: --/--]\n[ ICC ]\n\t- 회의 [완료일자: 10/14]\n\n[이슈]\n\t- 장비 이슈");
    }

    [Fact]
    public void RepeatedTasksAreMerged_WithoutMergingDifferentClients()
    {
        var data = new WeeklyReportData(
        [
            new("점검", 1, false), new(" 점검 ", 1, true, new(2026, 10, 14)),
            new("점검", 1, true, new(2026, 10, 15)), new("점검", 2, true, new(2026, 10, 13)),
        ], []);
        var text = _renderer.Render(ReportFormatKind.A, data, new() { Clients = Clients });
        text.Split("- 점검").Should().HaveCount(3);
        text.Should().Contain("10/15").And.NotContain("10/14").And.Contain("10/13");
    }

    [Fact]
    public void DoneOnly_FiltersTasksAndKeepsIssues()
    {
        var data = new WeeklyReportData([new("완료", null, true), new("미완료", null, false)], [new("이슈")]);
        var text = _renderer.Render(ReportFormatKind.A, data, new() { IncludeDoneOnly = true });
        text.Should().Contain("- 완료").And.NotContain("- 미완료").And.Contain("- 이슈");
    }
}

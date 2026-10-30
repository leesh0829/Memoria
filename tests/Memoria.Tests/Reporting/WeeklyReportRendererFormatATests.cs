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
        text.Should().Be("[업무 내용]\n- SLD PLC\n\t- 점검 [완료일자: 10/15]\n\t\t\to\n\t- 설계 [--/--~]\n\t\t\to"
            + "\n- MTP\n\t- 개발 [--/--~]\n\t\t\to\n- ICC\n\t- 회의 [완료일자: 10/14]\n\t\t\to\n\n[이슈]\n\t- 장비 이슈");
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

    [Theory]
    [InlineData("SLD", "SLD PLC")]
    [InlineData("코모텍", "코모텍 PLC")]
    [InlineData("SLD 도금", "도금 PLC")]
    public void PlcAliasesAndThreeTabDetails_AreUsedForTasks_AndAliasForIssues(string name, string header)
    {
        var data = new WeeklyReportData([new(name + " 점검", 1, false)], [new(name + " 지연", 1)]);
        var options = new ReportRenderOptions { Clients = [new() { Id = 1, Name = name }], Indent = "  " };
        var a = _renderer.Render(ReportFormatKind.A, data, options);
        a.Should().Be("[업무 내용]\n- " + header + "\n  - 점검 [--/--~]\n\t\t\to\n\n[이슈]\n- " + header + "\n  - 지연");
        _renderer.Render(ReportFormatKind.C, data, options).Should()
            .Be("[업무 내용]\n- 점검 [--/--~]\n\n[이슈]\n- " + header + "\n  - 지연");
    }


    [Theory]
    [InlineData(ReportFormatKind.A)]
    [InlineData(ReportFormatKind.C)]
    public void UnfinishedTasks_ShowStartDateRange_WhileDoneTasksKeepCompletionDate(ReportFormatKind format)
    {
        var data = new WeeklyReportData([
            new("진행 업무", null, false, CompletedDate: new(2026, 10, 29), StartedDate: new(2026, 10, 23)),
            new("완료 업무", null, true, CompletedDate: new(2026, 10, 29), StartedDate: new(2026, 10, 23)),
            new("날짜 없는 진행", null, false),
        ], []);
        var text = _renderer.Render(format, data, new());
        text.Should().Contain("- 진행 업무 [10/23~]")
            .And.Contain("- 완료 업무 [완료일자: 10/29]")
            .And.Contain("- 날짜 없는 진행 [--/--~]")
            .And.NotContain("진행 업무 [완료일자:");
    }

}

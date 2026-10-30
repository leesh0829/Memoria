using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.Tests.Reporting;

public class WeeklyReportRendererFormatCTests
{
    [Fact]
    public void CRemovesTaskHeadersMarkersAndIndent_AndIgnoresLegacyDClientFilter()
    {
        Client[] clients = [new() { Id = 1, Name = "SLD" }, new() { Id = 2, Name = "SLD 자율형공장" }];
        var data = new WeeklyReportData(
            [new("SLD 점검", 1, true, new(2026, 10, 15)), new("SLD 자율형공장 회의", 2, false)],
            [new("이슈")]);
        var options = new ReportRenderOptions { Clients = clients, ClientIdsC = [] };
        var renderer = new WeeklyReportRenderer();
        var a = renderer.Render(ReportFormatKind.A, data, options);
        var c = renderer.Render(ReportFormatKind.C, data, options);
        a.Should().Contain("\t\t\to").And.Contain("\t- 점검");
        c.Should().Be("[업무 내용]\n- 점검 [완료일자: 10/15]\n- 회의 [--/--~]\n\n[이슈]\n\t- 이슈");
    }
    [Theory]
    [InlineData(ReportFormatKind.A)]
    [InlineData(ReportFormatKind.B)]
    [InlineData(ReportFormatKind.C)]
    [InlineData(ReportFormatKind.D)]
    public void Reports_NormalizeLegacyChungbukName(ReportFormatKind format)
    {
        var data = new WeeklyReportData([new("현장: 충북테크놀로지파크 점검", null, true)], []);
        new WeeklyReportRenderer().Render(format, data, new()).Should()
            .NotContain("충북테크놀로지파크").And.Contain("충북테크노파크 (FEMS)");
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("    ")]
    [InlineData("")]
    public void CRemovesTaskIndentAndMarkers_PreservesCustomHeadersOrderDatesAndIssues(string indent)
    {
        var options = new ReportRenderOptions
        {
            Clients = [new() { Id = 1, Name = "SLD" }, new() { Id = 2, Name = "MTP" }],
            Indent = indent, TaskHeaderA = "이번주 업무", IssueHeaderA = "이번주 이슈",
        };
        var data = new WeeklyReportData(
            [new("MTP 개발", 2, true, new(2026, 10, 15)), new("SLD 점검", 1, false), new("SLD 점검", 1, false)],
            [new("MTP"), new("이슈")]);
        var renderer = new WeeklyReportRenderer();
        var a = renderer.Render(ReportFormatKind.A, data, options);
        var c = renderer.Render(ReportFormatKind.C, data, options);
        a.Should().Contain("\n- SLD PLC\n").And.Contain("\n- MTP\n").And.NotContain("[ SLD PLC ]");
        c.Should().Be("이번주 업무\n- 점검 [--/--~]\n"
            + "- 개발 [완료일자: 10/15]\n\n이번주 이슈\n" + indent + "- MTP\n" + indent + "- 이슈");
    }

    [Fact]
    public void RemovingHeaders_PreservesEditedNarrativeAndWindowsNewlines()
    {
        var options = new ReportRenderOptions { Clients = [new() { Id = 2, Name = "MTP" }] };
        var a = "[업무 내용]\r\n- MES\r\n\t- 직접 수정한 업무 [완료일자: 10/15]\r\n"
            + "- MTP\r\n\t추가 설명: MTP 현장\r\n\r\n[이슈]\r\n- MTP";
        FormatAReport.WithoutClientHeaders(a, options).Should().Be(
            "[업무 내용]\r\n- 직접 수정한 업무 [완료일자: 10/15]\r\n"
            + "\t추가 설명: MTP 현장\r\n\r\n[이슈]\r\n- MTP");
    }


    [Fact]
    public void CFromEditedA_RemovesGeneratedMarkersAndTaskIndent_ButKeepsDetailsAndIssues()
    {
        var options = new ReportRenderOptions();
        var text = "[업무 내용]\r\n- SLD PLC\r\n\t- 진행 [10/23~]\r\n\t\t\to\r\n"
            + "\t\t\to 직접 작성한 설명\r\n\r\n[이슈]\r\n- SLD PLC\r\n\t- 이슈\r\n\t\t\to";
        FormatAReport.WithoutClientHeaders(text, options).Should().Be(
            "[업무 내용]\r\n- 진행 [10/23~]\r\n\t\t\to 직접 작성한 설명\r\n\r\n[이슈]\r\n- SLD PLC\r\n\t- 이슈\r\n\t\t\to");
    }

}

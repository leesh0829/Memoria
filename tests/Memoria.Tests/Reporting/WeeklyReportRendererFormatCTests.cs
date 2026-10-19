using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.Tests.Reporting;

public class WeeklyReportRendererFormatCTests
{
    [Fact]
    public void CMatchesAWithoutClientHeaders_AndIgnoresLegacyDClientFilter()
    {
        Client[] clients = [new() { Id = 1, Name = "SLD" }, new() { Id = 2, Name = "SLD 자율형공장" }];
        var data = new WeeklyReportData(
            [new("SLD 점검", 1, true, new(2026, 10, 15)), new("SLD 자율형공장 회의", 2, false)],
            [new("이슈")]);
        var options = new ReportRenderOptions { Clients = clients, ClientIdsC = [] };
        var renderer = new WeeklyReportRenderer();
        var a = renderer.Render(ReportFormatKind.A, data, options);
        var c = renderer.Render(ReportFormatKind.C, data, options);
        c.Should().Be(a.Replace("[ MES ]\n", "").Replace("[ ICC ]\n", ""));
        c.Should().Be("[업무 내용]\n\t- 점검 [완료일자: 10/15]\n\t- 회의 [완료일자: --/--]\n\n[이슈]\n\t- 이슈");
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

}

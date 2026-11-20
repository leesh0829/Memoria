using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.Tests.Reporting;

public class ReportIndentationTests
{
    [Theory]
    [InlineData(ReportFormatKind.A)]
    [InlineData(ReportFormatKind.B)]
    [InlineData(ReportFormatKind.C)]
    public void LegacyTabSettings_RenderAsTwoSpacesPerTab(ReportFormatKind format)
    {
        var options = new ReportRenderOptions { Indent = "\t", Clients = [new() { Id = 1, Name = "SLD" }] };
        var text = new WeeklyReportRenderer().Render(format,
            new WeeklyReportData([new("SLD 점검", 1, false)], [new("이슈")]), options);
        text.Should().NotContain("\t");
        if (format == ReportFormatKind.A)
            text.Should().Contain("\n- 점검").And.NotContain("\n      o");
        if (format == ReportFormatKind.B)
            text.Should().Contain("\n  * SLD 점검");
    }

    [Fact]
    public void C_RemovesSpaceBasedGeneratedMarkers_ButKeepsWrittenDetails()
    {
        var text = "[업무 내용]\n- SLD PLC\n  - 업무\n      o\n      o 직접 쓴 설명\n\n[이슈]\n  - 공지";
        FormatAReport.WithoutClientHeaders(text, new()).Should().Be(
            "[업무 내용]\n- 업무\n      o 직접 쓴 설명\n\n[이슈]\n  - 공지");
    }
}

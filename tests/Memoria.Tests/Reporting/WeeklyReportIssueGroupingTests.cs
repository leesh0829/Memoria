using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.Tests.Reporting;

public class WeeklyReportIssueGroupingTests
{
    private static readonly Client[] Clients =
    [
        new() { Id = 1, Name = "SLD" },
        new() { Id = 2, Name = "MTP" },
        new() { Id = 3, Name = "SLD 자율형공장" },
    ];

    [Theory]
    [InlineData(ReportFormatKind.A)]
    [InlineData(ReportFormatKind.C)]
    public void Issues_AreGroupedWithUnclassifiedFirst_AndLeadingClientNamesRemoved(ReportFormatKind format)
    {
        var data = new WeeklyReportData([], [
            new("MTP 일정 협의", 2), new("SLD 점검 지연", 1), new("공통 이슈"),
            new("SLD 자율형공장 설비 확인", 3), new("MTP 추가 협의", 2), new("일반 공지"),
        ]);
        var text = new WeeklyReportRenderer().Render(format, data, new() { Clients = Clients });
        text.Should().Be("[업무 내용]\n\n[이슈]\n\t- 공통 이슈\n\t- 일반 공지\n"
            + "- SLD PLC\n\t- 점검 지연\n- MTP\n\t- 일정 협의\n\t- 추가 협의\n- ICC\n\t- 설비 확인");
    }

    [Fact]
    public void Issues_PreserveDistinctClientsAndEmbeddedCompanyNames_AndDeduplicateWithinEachClient()
    {
        var data = new WeeklyReportData([], [
            new("동일 문장", 2), new("동일 문장", 1), new("동일 문장", 1),
            new("검토: MTP 담당자 협의", 1), new("삭제된 고객사 업무", 99),
        ]);
        var text = new WeeklyReportRenderer().Render(ReportFormatKind.A, data, new() { Clients = Clients });
        text.Should().Be("[업무 내용]\n\n[이슈]\n\t- 삭제된 고객사 업무\n"
            + "- SLD PLC\n\t- 동일 문장\n\t- 검토: MTP 담당자 협의\n- MTP\n\t- 동일 문장");
    }

    [Fact]
    public void CFromCurrentA_PreservesIssueGroupsAndUnclassifiedPriority()
    {
        var options = new ReportRenderOptions { Clients = Clients };
        var data = new WeeklyReportData([new("SLD 업무", 1, false)],
            [new("MTP 협의", 2), new("공지"), new("SLD 점검", 1)]);
        var renderer = new WeeklyReportRenderer();
        var a = renderer.Render(ReportFormatKind.A, data, options);
        var c = renderer.Render(ReportFormatKind.C, data, options);
        c.Should().Be(FormatAReport.WithoutClientHeaders(a, options));
        c.Should().Contain("[이슈]\n\t- 공지\n- SLD PLC\n\t- 점검\n- MTP\n\t- 협의");
    }
}

using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Xunit;

namespace Memoria.Tests.Reporting;

public class WeeklyReportRendererFormatCTests
{
    private readonly IWeeklyReportRenderer _sut = new WeeklyReportRenderer();

    [Fact]
    public void FormatC_Golden_TaskLineFollowedByEmptyMarkerLine()
    {
        var data = new WeeklyReportData(
            Tasks:
            [
                new ReportTask("라인 셋업 점검", null, false),
                new ReportTask("미팅 정리", null, false),
            ],
            Issues: []);
        var options = new ReportRenderOptions();

        var text = _sut.Render(ReportFormatKind.C, data, options);

        const string expected =
            "[ 주간 실적 ]\n\n== 이승현\n- 라인 셋업 점검\n    o\n- 미팅 정리\n    o\n\n[ 차주 계획 ]";
        text.Should().Be(expected);
    }

    [Fact]
    public void FormatC_OmitsIssues()
    {
        var data = new WeeklyReportData(
            Tasks: [new ReportTask("업무1", null, false)],
            Issues: [new ReportIssue("장비 오류"), new ReportIssue("일정 지연")]);

        var text = _sut.Render(ReportFormatKind.C, data, new ReportRenderOptions());

        text.Should().NotContain("장비 오류").And.NotContain("일정 지연");
        text.Should().Be("[ 주간 실적 ]\n\n== 이승현\n- 업무1\n    o\n\n[ 차주 계획 ]");
    }

    [Fact]
    public void FormatC_DoesNotPrependClientName()
    {
        // 양식 A와 달리 고객사 접두를 붙이지 않고 체크리스트 원문을 그대로 쓴다.
        var sld = new Client { Id = 1, Name = "SLD", SortOrder = 1, Enabled = true };
        var data = new WeeklyReportData(
            Tasks: [new ReportTask("비전회의", 1, true)],
            Issues: []);
        var options = new ReportRenderOptions { Clients = [sld] };

        var text = _sut.Render(ReportFormatKind.C, data, options);

        text.Should().Be("[ 주간 실적 ]\n\n== 이승현\n- 비전회의\n    o\n\n[ 차주 계획 ]");
    }

    [Fact]
    public void FormatC_IncludeDoneOnly_FiltersOpenTasks()
    {
        var data = new WeeklyReportData(
            Tasks:
            [
                new ReportTask("완료 업무", null, true),
                new ReportTask("미완료 업무", null, false),
            ],
            Issues: []);
        var options = new ReportRenderOptions { IncludeDoneOnly = true };

        var text = _sut.Render(ReportFormatKind.C, data, options);

        text.Should().Be("[ 주간 실적 ]\n\n== 이승현\n- 완료 업무\n    o\n\n[ 차주 계획 ]");
    }

    [Fact]
    public void FormatC_NoTasks_KeepsHeaderSkeleton()
    {
        var data = new WeeklyReportData(Tasks: [], Issues: []);

        var text = _sut.Render(ReportFormatKind.C, data, new ReportRenderOptions());

        text.Should().Be("[ 주간 실적 ]\n\n== 이승현\n\n[ 차주 계획 ]");
    }

    [Fact]
    public void FormatC_KeepsOnlyTasksOfSelectedClients()
    {
        var data = new WeeklyReportData(
            Tasks:
            [
                new ReportTask("자율형공장 라인 셋업", 5, false),
                new ReportTask("SLD 비전회의", 1, false),
                new ReportTask("분류 안 된 업무", null, false),
            ],
            Issues: []);
        var options = new ReportRenderOptions { ClientIdsC = [5] };

        var text = _sut.Render(ReportFormatKind.C, data, options);

        text.Should().Be("[ 주간 실적 ]\n\n== 이승현\n- 자율형공장 라인 셋업\n    o\n\n[ 차주 계획 ]");
    }

    [Fact]
    public void FormatC_EmptyClientFilter_OutputsNoTasks()
    {
        var data = new WeeklyReportData(
            Tasks: [new ReportTask("업무1", 1, false)],
            Issues: []);
        var options = new ReportRenderOptions { ClientIdsC = [] };

        var text = _sut.Render(ReportFormatKind.C, data, options);

        text.Should().Be("[ 주간 실적 ]\n\n== 이승현\n\n[ 차주 계획 ]");
    }

    [Fact]
    public void FormatC_NullClientFilter_KeepsEveryTask()
    {
        var data = new WeeklyReportData(
            Tasks:
            [
                new ReportTask("분류된 업무", 1, false),
                new ReportTask("미분류 업무", null, false),
            ],
            Issues: []);
        var options = new ReportRenderOptions { ClientIdsC = null };

        var text = _sut.Render(ReportFormatKind.C, data, options);

        text.Should().Be("[ 주간 실적 ]\n\n== 이승현\n- 분류된 업무\n    o\n- 미분류 업무\n    o\n\n[ 차주 계획 ]");
    }

    [Fact]
    public void FormatC_ClientFilterAndIncludeDoneOnly_ApplyTogether()
    {
        var data = new WeeklyReportData(
            Tasks:
            [
                new ReportTask("완료 + 대상 고객사", 5, true),
                new ReportTask("미완료 + 대상 고객사", 5, false),
                new ReportTask("완료 + 다른 고객사", 1, true),
            ],
            Issues: []);
        var options = new ReportRenderOptions { ClientIdsC = [5], IncludeDoneOnly = true };

        var text = _sut.Render(ReportFormatKind.C, data, options);

        text.Should().Be("[ 주간 실적 ]\n\n== 이승현\n- 완료 + 대상 고객사\n    o\n\n[ 차주 계획 ]");
    }

    [Fact]
    public void FormatC_UsesConfiguredNameHeadersAndMarker()
    {
        var data = new WeeklyReportData(Tasks: [new ReportTask("업무1", null, false)], Issues: []);
        var options = new ReportRenderOptions
        {
            ReporterName = "홍길동",
            TitleHeaderC = "[ 이번주 한 일 ]",
            PlanHeaderC = "[ 다음주 할 일 ]",
            DetailMarkerC = "-",
        };

        var text = _sut.Render(ReportFormatKind.C, data, options);

        text.Should().Be("[ 이번주 한 일 ]\n\n== 홍길동\n- 업무1\n    -\n\n[ 다음주 할 일 ]");
    }
}

using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.Tests.Reporting;

public class WeeklyReportLayoutTests
{
    private static readonly Client[] Clients =
    [
        new() { Id = 1, Name = "SLD" },
        new() { Id = 2, Name = "SLD 도금" },
        new() { Id = 3, Name = "SLD 자율형공장" },
    ];

    [Theory]
    [InlineData(ReportFormatKind.A)]
    [InlineData(ReportFormatKind.C)]
    public void EmptyReports_KeepAllThreeSections(ReportFormatKind format)
        => new WeeklyReportRenderer().Render(format, new([], []), new()).Should()
            .Be("[업무 내용]\n\n[이슈]\n\n[일정]");

    [Fact]
    public void B_KeepsItsExistingIssueSectionAndDoesNotAddScheduleSections()
    {
        var text = new WeeklyReportRenderer().Render(ReportFormatKind.B,
            new([], [new("11/16 연차")]), new());
        text.Should().Contain("* 이슈사항:\n  * 11/16 연차").And.NotContain("[일정]");
    }

    [Fact]
    public void A_UsesClientCountsParenthesizedDatesAndWrittenDetails()
    {
        var data = new WeeklyReportData([
            new("SLD DV18 게더링데이터 미수집 조치", 1, true, new(2026, 11, 18)),
            new("도금 설비 PLC 데이터 수집\n  - 현재 노트북에서 데이터 수집 중\n서버로 이전 예정", 2, false, StartedDate: new(2026, 11, 13)),
            new("AMR AAS 동적데이터 등록 안내 처리 [디지트랙]", 3, false, StartedDate: new(2026, 11, 18)),
        ], [
            new("11/16 ~ 11/17 연차"),
            new("11/19 SLD 파주 외근 (SK 설비 점검)", 1),
            new("PLC 에이전트 / DB 서버 이전, 세팅 VPN 발급으로 인해 딜레이", 2),
        ]);
        var text = new WeeklyReportRenderer().Render(ReportFormatKind.A, data, new() { Clients = Clients });
        text.Should().Be("""
            [업무 내용]

            ■ SLD PLC — 1건 (완료 1건)
            - DV18 게더링데이터 미수집 조치 (11/18 완료)

            ■ SLD 도금 PLC — 1건 (완료 0건)
            - 도금 설비 PLC 데이터 수집 (11/13~)
              - 현재 노트북에서 데이터 수집 중
              - 서버로 이전 예정

            ■ ICC — 1건 (완료 0건)
            - AMR AAS 동적데이터 등록 안내 처리 (디지트랙, 11/18~)

            [이슈]

            ■ SLD 도금 PLC
            - PLC 에이전트 / DB 서버 이전, 세팅 VPN 발급으로 인해 딜레이

            [일정]

            - 11/16~11/17: 연차
            - 11/19: SLD 파주 외근 (SK 설비 점검)
            """.Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData(ReportFormatKind.A)]
    [InlineData(ReportFormatKind.C)]
    public void Schedules_AreSeparatedWithoutMovingTechnicalIssuesOrExpenseClaims(ReportFormatKind format)
    {
        var issues = new ReportIssue[]
        {
            new("11/16 ~ 11/17 연차"), new("11/23~11/24 SLD 공주 출장 예정", 1),
            new("11/25 KOSMO AAS 대면 강의 참석 예정"),
            new("12일 고속버스 매출전표 청구 후 공주 출장 법인카드 지출 결의서 작성"),
            new("VPN 및 방화벽 설정 문제로 서버 이전이 지연되고 있음", 2),
            new("11월 테스트 운영을 위한 작업 일정 협의", 3),
        };
        var text = new WeeklyReportRenderer().Render(format, new([], issues), new() { Clients = Clients });
        var sections = text.Split("[일정]");
        sections.Should().HaveCount(2);
        sections[0].Should().Contain("지출 결의서").And.Contain("서버 이전이 지연").And.Contain("작업 일정 협의")
            .And.NotContain("공주 출장 예정").And.NotContain("강의 참석 예정");
        sections[1].Should().Contain("11/16~11/17: 연차").And.Contain("공주 출장 예정").And.Contain("강의 참석 예정")
            .And.NotContain("■").And.NotContain("지출 결의서");
    }

    [Fact]
    public void A_CountsDeduplicatedVisibleTasks_AndDoesNotMergeDifferentClients()
    {
        var data = new WeeklyReportData([
            new("점검", 1, false), new(" 점검 ", 1, true, new(2026, 11, 18)),
            new("점검", 2, false), new("숨길 업무", 1, false),
        ], []);
        var renderer = new WeeklyReportRenderer();
        renderer.Render(ReportFormatKind.A, data, new() { Clients = Clients }).Should()
            .Contain("■ SLD PLC — 2건 (완료 1건)").And.Contain("■ SLD 도금 PLC — 1건 (완료 0건)");
        renderer.Render(ReportFormatKind.A, data, new() { Clients = Clients, IncludeDoneOnly = true }).Should()
            .Contain("■ SLD PLC — 1건 (완료 1건)").And.NotContain("■ SLD 도금 PLC").And.NotContain("숨길 업무");
    }

    [Fact]
    public void C_FromEditedNewA_RemovesOnlyTaskHeaders_AndPreservesDetailsIssuesAndSchedules()
    {
        var a = "[업무 내용]\r\n\r\n■ SLD PLC — 1건 (완료 0건)\r\n- 직접 쓴 업무 (디지트랙, 11/18~)\r\n"
            + "  - 상세 설명\r\n    - 더 깊은 설명\r\n\r\n[이슈]\r\n\r\n■ SLD PLC\r\n- 이슈\r\n  - 이슈 상세\r\n\r\n[일정]\r\n\r\n- 11/25: 강의 참석 예정";
        FormatAReport.WithoutClientHeaders(a, new() { Clients = Clients }).Should().Be(
            a.Replace("■ SLD PLC — 1건 (완료 0건)\r\n", ""));
    }

    [Fact]
    public void C_KeepsClientDistinctionInIssues_AndNoClientTaskHeaders()
    {
        var data = new WeeklyReportData([new("점검 [디지트랙]", 1, false, StartedDate: new(2026, 11, 18))],
            [new("VPN 지연", 2)]);
        var text = new WeeklyReportRenderer().Render(ReportFormatKind.C, data, new() { Clients = Clients });
        text.Should().StartWith("[업무 내용]\n\n- 점검 (디지트랙, 11/18~)")
            .And.NotContain("건 (완료").And.NotContain("      o")
            .And.Contain("[이슈]\n\n■ SLD 도금 PLC\n- VPN 지연\n\n[일정]");
    }
}

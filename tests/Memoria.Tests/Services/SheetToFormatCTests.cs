using FluentAssertions;
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Memoria.Core.Sheets;
using Memoria.Tests.Data;
using Xunit;

namespace Memoria.Tests.Services;

/// 구글 시트 격자 → SheetWorkParser → BuildFromTexts(자동 분류) → 양식 C 렌더까지의 실제 경로.
public class SheetToFormatCTests
{
    // 2026-08-12(수) 기준 양식 C 기본 구간 = 08-07(금) ~ 08-13(목).
    private static readonly DateOnly Start = new(2026, 8, 7);
    private static readonly DateOnly End = new(2026, 8, 13);

    private static IReadOnlyList<IReadOnlyList<string>> Grid() =>
    [
        ["일자", "작업내역", "특이사항"],
        ["2026.08.06 (목)", "1. 자율형공장 이전주 업무", ""],           // 구간 이전 → 제외
        ["2026.08.07 (금)", "1. 자율형공장 라인 셋업\n2. SLD 비전회의", "1. 장비 오류"],
        ["2026.08.08 (토)", "1. 자율형공장 주말 점검", ""],             // 주말도 구간 안이면 포함
        ["2026.08.11 (화)", "1. 코모텍 미팅\n2. 자율형 공장 설비 확인", ""],
        ["2026.08.13 (목)", "1. 자율형공장 보고서 작성", ""],
        ["2026.08.14 (금)", "1. 자율형공장 다음주 업무", ""],           // 구간 이후 → 제외
    ];

    private static (WeeklyReportService Svc, ClientRepository Clients) Build(TestDb db) =>
        (new WeeklyReportService(
            new WeekCalculator(), new NoteRepository(db.Factory), new ChecklistRepository(db.Factory),
            new ClientClassifier(), new ClientRepository(db.Factory), new WeeklyReportRenderer()),
         new ClientRepository(db.Factory));

    [Fact]
    public void SheetPath_RendersFormatC_WithOnlySldAutonomousFactoryTasks()
    {
        using var db = new TestDb();
        var (svc, clients) = Build(db);
        var enabled = clients.GetAll(enabledOnly: true);
        var options = new ReportRenderOptions
        {
            WeekStart = Start,
            WeekEnd = End,
            Clients = enabled,
            ClientIdsC = FormatCClients.Resolve(stored: null, enabled),   // 기본값 = SLD 자율형공장
        };

        var parsed = SheetWorkParser.Parse(Grid(), Start, End);
        var built = svc.BuildFromTexts(parsed.Tasks, parsed.Issues, Start, End, options);
        var text = svc.Render(ReportFormatKind.C, built.Data, options);

        const string expected =
            "[ 주간 실적 ]\n\n== 이승현\n" +
            "- 자율형공장 라인 셋업\n    o\n" +
            "- 자율형공장 주말 점검\n    o\n" +
            "- 자율형 공장 설비 확인\n    o\n" +
            "- 자율형공장 보고서 작성\n    o\n" +
            "\n[ 차주 계획 ]";
        text.Should().Be(expected);
    }

    [Fact]
    public void SheetPath_ParsesOnlyRowsInsideFridayToThursdayRange()
    {
        var parsed = SheetWorkParser.Parse(Grid(), Start, End);

        parsed.Tasks.Should().Equal(
            "자율형공장 라인 셋업",
            "SLD 비전회의",
            "자율형공장 주말 점검",
            "코모텍 미팅",
            "자율형 공장 설비 확인",
            "자율형공장 보고서 작성");
        parsed.Tasks.Should().NotContain("자율형공장 이전주 업무").And.NotContain("자율형공장 다음주 업무");
    }

    [Fact]
    public void SheetPath_ClassifiesAutonomousFactoryAheadOfSld()
    {
        using var db = new TestDb();
        var (svc, clients) = Build(db);
        var enabled = clients.GetAll(enabledOnly: true);
        var factoryId = enabled.Single(c => c.Name == FormatCClients.DefaultClientName).Id;
        var sldId = enabled.Single(c => c.Name == "SLD").Id;
        var options = new ReportRenderOptions { WeekStart = Start, WeekEnd = End, Clients = enabled };

        var parsed = SheetWorkParser.Parse(Grid(), Start, End);
        var built = svc.BuildFromTexts(parsed.Tasks, parsed.Issues, Start, End, options);

        built.Data.Tasks.Should().Contain(t => t.Text == "자율형공장 라인 셋업" && t.ClientId == factoryId);
        built.Data.Tasks.Should().Contain(t => t.Text == "자율형 공장 설비 확인" && t.ClientId == factoryId);
        built.Data.Tasks.Should().Contain(t => t.Text == "SLD 비전회의" && t.ClientId == sldId);
    }
}

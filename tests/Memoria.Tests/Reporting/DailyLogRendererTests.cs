using System.Collections.Generic;
using FluentAssertions;
using Memoria.Core.Reporting;
using Xunit;

namespace Memoria.Tests.Reporting;

public class DailyLogRendererTests
{
    private static readonly Dictionary<int, string> NoClients = new();

    // ---- Tasks ----

    [Fact]
    public void RenderTasks_empty_returns_empty_string()
    {
        DailyLogRenderer.RenderTasks(new List<(string, int?)>(), NoClients)
            .Should().Be("");
    }

    [Fact]
    public void RenderTasks_unclassified_has_no_prefix()
    {
        var tasks = new List<(string, int?)> { ("할 일 하나", null) };
        DailyLogRenderer.RenderTasks(tasks, NoClients).Should().Be("1. 할 일 하나");
    }

    [Fact]
    public void RenderTasks_with_client_prefixes_name_verbatim()
    {
        var tasks = new List<(string, int?)> { ("점검", 5) };
        var clients = new Dictionary<int, string> { [5] = "SLD" };
        DailyLogRenderer.RenderTasks(tasks, clients).Should().Be("1. SLD 점검");
    }

    [Fact]
    public void RenderTasks_unknown_clientId_omits_prefix()
    {
        var tasks = new List<(string, int?)> { ("점검", 99) };
        var clients = new Dictionary<int, string> { [5] = "SLD" };
        DailyLogRenderer.RenderTasks(tasks, clients).Should().Be("1. 점검");
    }

    [Fact]
    public void RenderTasks_skips_empty_and_whitespace_then_numbers_contiguously()
    {
        var tasks = new List<(string, int?)>
        {
            ("   ", 5),
            ("실제 업무", 5),
            ("", null),
            ("두번째", null),
        };
        var clients = new Dictionary<int, string> { [5] = "MTP" };
        DailyLogRenderer.RenderTasks(tasks, clients)
            .Should().Be("1. MTP 실제 업무\n2. 두번째");
    }

    [Fact]
    public void RenderTasks_multiple_numbers_restart_at_one()
    {
        var tasks = new List<(string, int?)> { ("a", 1), ("b", 2) };
        var clients = new Dictionary<int, string> { [1] = "A", [2] = "B" };
        DailyLogRenderer.RenderTasks(tasks, clients).Should().Be("1. A a\n2. B b");
    }

    [Fact]
    public void RenderTasks_text_is_verbatim_not_trimmed_when_kept()
    {
        // 내부/양끝 공백은 사용자 콘텐츠이므로 보존(공백-only만 스킵).
        var tasks = new List<(string, int?)> { ("07/15  파주 출장", null) };
        DailyLogRenderer.RenderTasks(tasks, NoClients).Should().Be("1. 07/15  파주 출장");
    }

    [Fact]
    public void RenderTasks_client_with_empty_name_omits_prefix()
    {
        var tasks = new List<(string, int?)> { ("업무", 7) };
        var clients = new Dictionary<int, string> { [7] = "  " };
        DailyLogRenderer.RenderTasks(tasks, clients).Should().Be("1. 업무");
    }

    [Fact]
    public void RenderTasks_canonical_example_byte_exact()
    {
        var tasks = new List<(string, int?)>
        {
            ("비전회의", 1),
            ("6층 11호기 추정 4090 그래픽카드 회수 여부 확인 -> 6층 9호기에 방치됨", 2),
            ("5호기에 필요한 랜선 적당한거 찾아서 결제 올리기 (작업은 다음주쯤인데 다시 계획 잡아서 자세한 일정 이메일로 보내야함)", 2),
        };
        var clients = new Dictionary<int, string> { [1] = "SLD", [2] = "SLD 자율형공장" };

        var expected =
            "1. SLD 비전회의\n" +
            "2. SLD 자율형공장 6층 11호기 추정 4090 그래픽카드 회수 여부 확인 -> 6층 9호기에 방치됨\n" +
            "3. SLD 자율형공장 5호기에 필요한 랜선 적당한거 찾아서 결제 올리기 (작업은 다음주쯤인데 다시 계획 잡아서 자세한 일정 이메일로 보내야함)";

        DailyLogRenderer.RenderTasks(tasks, clients).Should().Be(expected);
    }

    // ---- Issues ----

    [Fact]
    public void RenderIssues_empty_returns_empty_string()
    {
        DailyLogRenderer.RenderIssues(new List<string>()).Should().Be("");
    }

    [Fact]
    public void RenderIssues_single()
    {
        DailyLogRenderer.RenderIssues(new List<string> { "이슈 하나" }).Should().Be("1. 이슈 하나");
    }

    [Fact]
    public void RenderIssues_skips_whitespace_then_contiguous()
    {
        var issues = new List<string> { "  ", "실제 이슈", "" };
        DailyLogRenderer.RenderIssues(issues).Should().Be("1. 실제 이슈");
    }

    [Fact]
    public void RenderIssues_canonical_example_byte_exact()
    {
        var issues = new List<string>
        {
            "SLD 파주 공장 출장 (비전 회의)",
            "07/15  SLD 파주 출장 (새 서버 세팅 및 전원공사 확인 & 생산팀 라벨프린터 회의)",
            "SLD 자율형공장 비전 회의 이후 로직 변경 필요 (먼저 비전까지의 시간을 택타임 * 탭수로 계산 후, 그 시간을 호기 마다 다르게 파악해서 다 다로 적용후에 시간(약 30초정도)과 누적 불량(50개 생산된 것중에 연속 10개/누적20개)로 기준 변경",
            "SLD 자율형공장 비전 회의 이후 명일 오전에 비전 AI 서버 파악 후 경로 김민근 사원에게 알려주면 불량 라벨링된 JPG 받아서 분석 시작",
        };

        var expected =
            "1. SLD 파주 공장 출장 (비전 회의)\n" +
            "2. 07/15  SLD 파주 출장 (새 서버 세팅 및 전원공사 확인 & 생산팀 라벨프린터 회의)\n" +
            "3. SLD 자율형공장 비전 회의 이후 로직 변경 필요 (먼저 비전까지의 시간을 택타임 * 탭수로 계산 후, 그 시간을 호기 마다 다르게 파악해서 다 다로 적용후에 시간(약 30초정도)과 누적 불량(50개 생산된 것중에 연속 10개/누적20개)로 기준 변경\n" +
            "4. SLD 자율형공장 비전 회의 이후 명일 오전에 비전 AI 서버 파악 후 경로 김민근 사원에게 알려주면 불량 라벨링된 JPG 받아서 분석 시작";

        DailyLogRenderer.RenderIssues(issues).Should().Be(expected);
    }
}

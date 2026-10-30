using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.Tests.Models;

public class WorkEntryTests
{
    private static readonly DateTime Day = new(2026, 10, 19);
    private static WorkEntry Completed() => new()
    {
        Done = true, Statuses = ["완료"], RequestedDate = Day.AddDays(-10),
        ProcessingStart = Day.AddDays(-5), ProcessingEnd = Day.AddDays(-3),
    };


    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void CompletedWork_IsVisibleOnlyWhenCompletionIsOnOrAfterSelectedDate(int completionOffset, bool visible)
    {
        var entry = Completed();
        entry.RequestedDate = Day;
        entry.ProcessingStart = Day.AddDays(-10);
        entry.ProcessingEnd = Day.AddDays(completionOffset);
        entry.IsVisibleOn(Day).Should().Be(visible);
    }

    [Theory]
    [InlineData("대기중")]
    [InlineData("협의중")]
    [InlineData("진행중")]
    [InlineData("검토중")]
    public void ActiveStatuses_AreVisibleOutsideDateRange(string status)
    {
        var entry = Completed();
        entry.Statuses = [status];
        entry.IsVisibleOn(Day).Should().BeTrue();
    }

    [Fact]
    public void CompletedWithoutEnd_UsesSingleProcessingDate_AndHidesUnknownCompletion()
    {
        var entry = Completed();
        entry.ProcessingEnd = null;
        entry.IsVisibleOn(Day).Should().BeFalse();
        entry.ProcessingStart = Day;
        entry.IsVisibleOn(Day).Should().BeTrue();
        entry.ProcessingStart = Day.AddDays(1);
        entry.IsVisibleOn(Day).Should().BeTrue();
        entry.ProcessingStart = null;
        entry.RequestedDate = Day;
        entry.IsVisibleOn(Day).Should().BeFalse();
    }

    [Fact]
    public void NotStarted_IsVisibleOnlyOnRequestDate_RegardlessOfProcessingDates()
    {
        var entry = new WorkEntry { Done = true, Statuses = ["미진행"], RequestedDate = Day };
        entry.IsVisibleOn(Day).Should().BeTrue();
        entry.IsVisibleOn(Day.AddDays(1)).Should().BeFalse();
        entry.ProcessingStart = Day;
        entry.ProcessingEnd = Day.AddDays(1);
        entry.RequestedDate = Day.AddDays(-1);
        entry.IsVisibleOn(Day).Should().BeFalse();
        entry.RequestedDate = null;
        entry.IsVisibleOn(Day).Should().BeFalse();
    }

    [Fact]
    public void CompletedStatus_TakesDateFilterPriorityOverLegacyActiveStatus()
    {
        var entry = Completed();
        entry.Statuses = ["완료", "진행중"];
        entry.IsVisibleOn(Day).Should().BeFalse();
    }

    [Fact]
    public void MultipleStatuses_HaveOneDeterministicSection()
    {
        new WorkEntry { Statuses = ["작업 중", "검토 중"] }.Section.Should().Be("검토중");
        new WorkEntry { Statuses = ["검토 중", "회의필요"] }.Section.Should().Be("협의중");
        new WorkEntry { Statuses = ["완료", "회의필요"] }.Section.Should().Be("완료");
    }

    [Fact]
    public void Copy_PreservesColumnOrderDatesAndMultilineExcelCells_AndSortsByStatus()
    {
        var completed = Completed();
        completed.Request = "완료된 업무";
        var active = new WorkEntry
        {
            RequestedDate = Day, Statuses = ["요청"], RequestTypes = ["MES", "설비"],
            Clients = ["SCA"], WorkTypes = ["개발"], RequestMethods = ["유선", "현장"],
            Request = "첫 줄\n두번째 \"인용\"", Cause = "원인", Action = "조치", Requester = "요청자", Assignee = "담당자",
        };
        var text = WorkEntryClipboard.Render([completed, active]);
        text.Should().StartWith("\t요청\t2026-10-19\t\tMES, 설비\tSCA\t개발\t유선, 현장\t\"첫 줄\n두번째 \"\"인용\"\"\"\t원인\t조치\t요청자\t담당자\r\n완료\t완료");
        text.Should().Contain("2026-10-14 ~ 2026-10-16");
    }

    [Theory]
    [InlineData("미진행", "미진행")]
    [InlineData("무산", "미진행")]
    [InlineData("보류", "대기중")]
    [InlineData("대기 중", "대기중")]
    [InlineData("대기", "대기중")]
    [InlineData("협의중", "협의중")]
    public void ImportedInactiveStatuses_HaveTheirOwnSections(string status, string section)
    {
        new WorkEntry { Statuses = [status] }.Section.Should().Be(section);
    }

}

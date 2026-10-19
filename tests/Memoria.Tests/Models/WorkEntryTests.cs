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

    [Fact]
    public void CompletedWork_IsVisibleOnRequestDateAndInclusiveProcessingDates()
    {
        var entry = Completed();
        entry.IsVisibleOn(Day).Should().BeFalse();
        entry.IsVisibleOn(entry.RequestedDate!.Value).Should().BeTrue();
        entry.IsVisibleOn(entry.ProcessingStart!.Value).Should().BeTrue();
        entry.IsVisibleOn(Day.AddDays(-4)).Should().BeTrue();
        entry.IsVisibleOn(entry.ProcessingEnd!.Value).Should().BeTrue();
    }

    [Theory]
    [InlineData("요청")]
    [InlineData("작업 중")]
    [InlineData("검토 중")]
    [InlineData("회의필요")]
    public void ActiveStatuses_AreVisibleOutsideDateRange(string status)
    {
        var entry = Completed();
        entry.Statuses = ["완료", status];
        entry.IsVisibleOn(Day).Should().BeTrue();
    }

    [Fact]
    public void MissingProcessingDate_AlwaysRemainsVisible_EvenIfCompleted()
    {
        var entry = Completed();
        entry.ProcessingEnd = null;
        entry.IsVisibleOn(Day).Should().BeTrue();
        entry.ProcessingEnd = Day.AddDays(-3);
        entry.ProcessingStart = null;
        entry.IsVisibleOn(Day).Should().BeTrue();
    }

    [Fact]
    public void MultipleStatuses_HaveOneDeterministicSection()
    {
        new WorkEntry { Statuses = ["작업 중", "검토 중"] }.Section.Should().Be("검토 중");
        new WorkEntry { Statuses = ["검토 중", "회의필요"] }.Section.Should().Be("회의필요");
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
}

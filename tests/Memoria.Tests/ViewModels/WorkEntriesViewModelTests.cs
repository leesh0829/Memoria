using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Tests.ViewModels;

public class WorkEntriesViewModelTests
{
    [Fact]
    public void EditingRows_AutosavesAllFieldsAndMultipleSelections_WithoutChangingStatusOnCheck()
    {
        var repository = new Repository();
        var vm = new WorkEntriesViewModel(repository, new FakeClipboardService(), new FakeConfirmationDialogService());
        vm.SelectedDate = new(2026, 10, 19);
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Should().ContainSingle().Subject;
        row.RequestedDate.Should().Be(new DateTime(2026, 10, 19));
        row.Request = "요청\n상세";
        row.Clients.Single(c => c.Name == "SLD_파주").IsSelected = true;
        row.Clients.Single(c => c.Name == "SLD_공주").IsSelected = true;
        row.Done = true;
        repository.Saved!.Done.Should().BeTrue();
        repository.Saved.Request.Should().Be("요청\n상세");
        repository.Saved.Clients.Should().Equal("SLD_파주", "SLD_공주");
        repository.Saved.Statuses.Should().Equal("요청");
    }

    [Fact]
    public void CopyIncludesOnlyVisibleWork_AndGroupsReviewMeetingAndCompletedSeparately()
    {
        var day = new DateTime(2026, 10, 19);
        var repository = new Repository();
        repository.Items.AddRange(
        [
            new() { Request = "지난 완료", Statuses = ["완료"], RequestedDate = day.AddDays(-10), ProcessingStart = day.AddDays(-5), ProcessingEnd = day.AddDays(-3) },
            new() { Request = "회의 업무", Statuses = ["회의필요"] },
            new() { Request = "검토 업무", Statuses = ["검토 중"] },
            new() { Request = "오늘 완료", Statuses = ["완료"], RequestedDate = day, ProcessingStart = day, ProcessingEnd = day },
        ]);
        var clipboard = new FakeClipboardService();
        var vm = new WorkEntriesViewModel(repository, clipboard, new FakeConfirmationDialogService()) { SelectedDate = day };
        vm.CopyCommand.Execute(null);
        clipboard.LastText.Should().NotContain("지난 완료").And.Contain("오늘 완료");
        clipboard.LastText!.IndexOf("검토 업무", StringComparison.Ordinal).Should().BeLessThan(clipboard.LastText.IndexOf("회의 업무", StringComparison.Ordinal));
        vm.Entries.Groups.Should().HaveCount(3);
    }

    private sealed class Repository : IWorkEntryRepository
    {
        public List<WorkEntry> Items { get; } = [];
        public WorkEntry? Saved { get; private set; }
        public IReadOnlyList<WorkEntry> GetAll() => Items;
        public void Save(WorkEntry entry) => Saved = entry;
        public void Delete(string id) => Items.RemoveAll(e => e.Id == id);
    }

    [Fact]
    public void SelectingNewStatus_ReplacesPreviousStatus_AndPersistsTheNewSection()
    {
        WorkEntry? saved = null;
        var row = new WorkEntryRow(new WorkEntry { Statuses = ["완료"] }, r => saved = r.ToModel());
        row.Statuses.Single(c => c.Name == "작업 중").IsSelected = true;
        row.ToModel().Statuses.Should().Equal("작업 중");
        row.Section.Should().Be("요청 / 작업 중");
        saved!.Statuses.Should().Equal("작업 중");
        row.Statuses.Single(c => c.Name == "보류").IsSelected = true;
        row.Section.Should().Be("보류");
        saved.Statuses.Should().Equal("보류");
    }

    [Fact]
    public void CurrentStatus_CannotBeClearedByClickingItAgain()
    {
        var row = new WorkEntryRow(new WorkEntry { Statuses = ["요청"] }, _ => { });
        row.Statuses.Single(c => c.Name == "요청").IsSelected = false;
        row.ToModel().Statuses.Should().Equal("요청");
    }

    [Fact]
    public void ImportedWaitingStatus_UsesCanonicalChoice_AndCanChangeToCompleted()
    {
        var row = new WorkEntryRow(new WorkEntry { Statuses = ["대기 중"] }, _ => { });
        row.Statuses.Single(c => c.Name == "대기").IsSelected.Should().BeTrue();
        row.Statuses.Should().NotContain(c => c.Name == "대기 중");
        row.Statuses.Single(c => c.Name == "완료").IsSelected = true;
        row.ToModel().Statuses.Should().Equal("완료");
        row.Section.Should().Be("완료");
    }


    [Fact]
    public void RefreshAfterStatusSelection_MovesRowToNewGroup()
    {
        var repository = new Repository();
        var vm = new WorkEntriesViewModel(repository, new FakeClipboardService(), new FakeConfirmationDialogService());
        vm.AddCommand.Execute(null);
        vm.Rows.Single().Statuses.Single(c => c.Name == "협의중").IsSelected = true;
        vm.Refresh();
        vm.Entries.Groups.Should().ContainSingle();
        ((System.Windows.Data.CollectionViewGroup)vm.Entries.Groups![0]).Name.Should().Be("협의중");
        repository.Saved!.Statuses.Should().Equal("협의중");
    }

}

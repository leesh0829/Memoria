// tests/Memoria.Tests/ViewModels/ChecklistItemViewModelTests.cs
using System;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class ChecklistItemViewModelTests
{
    private static ChecklistItem TaskModel(string text = "SLD 작업", int? clientId = null) => new()
    {
        Id = 7,
        NoteId = 3,
        Kind = ItemKind.Task,
        Text = text,
        Done = false,
        ClientId = clientId,
        IsManual = false,
        SortOrder = 2,
        CreatedAt = new DateTimeOffset(2026, 6, 26, 9, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 6, 26, 9, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Constructor_copies_model_without_marking_dirty()
    {
        var vm = new ChecklistItemViewModel(TaskModel());

        vm.Id.Should().Be(7);
        vm.NoteId.Should().Be(3);
        vm.Kind.Should().Be(ItemKind.Task);
        vm.Text.Should().Be("SLD 작업");
        vm.SortOrder.Should().Be(2);
        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Task_shows_checkbox_issue_does_not()
    {
        new ChecklistItemViewModel(TaskModel()).ShowCheckbox.Should().BeTrue();

        var issue = TaskModel();
        issue.Kind = ItemKind.Issue;
        new ChecklistItemViewModel(issue).ShowCheckbox.Should().BeFalse();
    }

    [Fact]
    public void IsStruck_true_only_when_task_and_done()
    {
        var vm = new ChecklistItemViewModel(TaskModel());
        vm.IsStruck.Should().BeFalse();

        vm.Done = true;
        vm.IsStruck.Should().BeTrue();
    }

    [Fact]
    public void IsUnclassified_true_for_task_with_null_client()
    {
        var vm = new ChecklistItemViewModel(TaskModel(clientId: null));
        vm.IsUnclassified.Should().BeTrue();

        vm.ClientId = 5;
        vm.IsUnclassified.Should().BeFalse();
    }

    [Fact]
    public void Issue_is_never_unclassified_highlight()
    {
        var issue = TaskModel();
        issue.Kind = ItemKind.Issue;
        new ChecklistItemViewModel(issue).IsUnclassified.Should().BeFalse();
    }

    [Fact]
    public void Editing_text_marks_dirty()
    {
        var vm = new ChecklistItemViewModel(TaskModel());
        vm.Text = "코모텍 변경";
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ToModel_round_trips_all_fields()
    {
        var vm = new ChecklistItemViewModel(TaskModel());
        vm.Done = true;
        vm.DoneAt = new DateTimeOffset(2026, 6, 26, 10, 0, 0, TimeSpan.Zero);
        vm.ClientId = 9;

        var model = vm.ToModel();
        model.Id.Should().Be(7);
        model.NoteId.Should().Be(3);
        model.Kind.Should().Be(ItemKind.Task);
        model.Done.Should().BeTrue();
        model.DoneAt.Should().Be(new DateTimeOffset(2026, 6, 26, 10, 0, 0, TimeSpan.Zero));
        model.ClientId.Should().Be(9);
        model.SortOrder.Should().Be(2);
    }
}

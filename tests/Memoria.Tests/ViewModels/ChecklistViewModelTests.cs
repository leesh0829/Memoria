// tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs
using System;
using System.Linq;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.Fakes;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class ChecklistViewModelTests
{
    private readonly FakeChecklistRepository _checklist = new();
    private readonly FakeClientRepository _clients = new();
    private readonly FakeTaggingService _tagging = new();
    private readonly FakeNoteRepository _notes = new();
    private readonly FakeGroupRepository _groups = new();

    private ChecklistViewModel CreateSut() =>
        new(_checklist, _clients, _tagging, _notes, _groups);

    private Note SeedNote(int id = 1)
    {
        var note = new Note
        {
            Id = id,
            Type = NoteType.Checklist,
            LogDate = new DateOnly(2026, 6, 26),
            CreatedAt = new DateTimeOffset(2026, 6, 26, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 6, 26, 8, 0, 0, TimeSpan.Zero),
        };
        _notes.Notes.Add(note);
        return note;
    }

    [Fact]
    public void Load_reads_items_sorted_by_sort_order()
    {
        var note = SeedNote();
        _checklist.AddItem(new ChecklistItem { NoteId = 1, Kind = ItemKind.Task, Text = "B", SortOrder = 1 });
        _checklist.AddItem(new ChecklistItem { NoteId = 1, Kind = ItemKind.Issue, Text = "A", SortOrder = 0 });

        var sut = CreateSut();
        sut.Load(note);

        sut.Items.Select(i => i.Text).Should().ContainInOrder("A", "B");
        sut.LogDate.Should().Be(new DateOnly(2026, 6, 26));
    }

    [Fact]
    public void Load_populates_only_enabled_clients_in_display_order()
    {
        var note = SeedNote();
        _clients.Clients.Add(new Client { Id = 1, Name = "SLD", SortOrder = 0, Enabled = true });
        _clients.Clients.Add(new Client { Id = 2, Name = "비활성", SortOrder = 1, Enabled = false });
        _clients.Clients.Add(new Client { Id = 3, Name = "MTP", SortOrder = 2, Enabled = true });

        var sut = CreateSut();
        sut.Load(note);

        sut.AvailableClients.Select(c => c.Name).Should().ContainInOrder("SLD", "MTP");
        sut.AvailableClients.Should().HaveCount(2);
    }

    [Fact]
    public void AddTask_creates_task_item_with_checkbox_and_persists()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);

        sut.AddTask();

        sut.Items.Should().HaveCount(1);
        sut.Items[0].Kind.Should().Be(ItemKind.Task);
        sut.Items[0].ShowCheckbox.Should().BeTrue();
        _checklist.Items.Should().ContainSingle(i => i.NoteId == 1 && i.Kind == ItemKind.Task);
    }

    [Fact]
    public void AddIssue_creates_issue_item_without_checkbox()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);

        sut.AddIssue();

        sut.Items[0].Kind.Should().Be(ItemKind.Issue);
        sut.Items[0].ShowCheckbox.Should().BeFalse();
    }

    [Fact]
    public void Added_items_get_increasing_sort_order()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);

        sut.AddTask();
        sut.AddTask();

        sut.Items[0].SortOrder.Should().Be(0);
        sut.Items[1].SortOrder.Should().Be(1);
    }

    [Fact]
    public void RemoveItem_deletes_from_collection_and_repository()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var item = sut.Items[0];

        sut.RemoveItem(item);

        sut.Items.Should().BeEmpty();
        _checklist.Items.Should().NotContain(i => i.Id == item.Id);
    }

    [Fact]
    public void AddItem_bumps_parent_note_updated_at()
    {
        var note = SeedNote();
        var before = note.UpdatedAt;
        var sut = CreateSut();
        sut.Load(note);

        sut.AddTask();

        _notes.Get(1)!.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public void ToggleDone_sets_done_strikethrough_and_done_at()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var item = sut.Items[0];

        sut.ToggleDone(item);

        item.Done.Should().BeTrue();
        item.IsStruck.Should().BeTrue();
        item.DoneAt.Should().NotBeNull();
        _checklist.Items.Single(i => i.Id == item.Id).Done.Should().BeTrue();
    }

    [Fact]
    public void ToggleDone_twice_clears_done_and_done_at()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var item = sut.Items[0];

        sut.ToggleDone(item);
        sut.ToggleDone(item);

        item.Done.Should().BeFalse();
        item.DoneAt.Should().BeNull();
    }

    [Fact]
    public void ToggleDone_ignores_issue_items()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddIssue();
        var item = sut.Items[0];

        sut.ToggleDone(item);

        item.Done.Should().BeFalse();
        item.DoneAt.Should().BeNull();
    }

    [Fact]
    public void ToggleDone_bumps_parent_note_updated_at()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var before = _notes.Get(1)!.UpdatedAt;
        var item = sut.Items[0];

        sut.ToggleDone(item);

        _notes.Get(1)!.UpdatedAt.Should().BeOnOrAfter(before);
    }
}

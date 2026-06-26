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
}

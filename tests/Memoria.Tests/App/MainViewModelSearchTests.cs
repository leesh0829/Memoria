using System;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelSearchTests
{
    [Fact]
    public void Search_with_blank_text_returns_empty_results_without_calling_service()
    {
        var (vm, _, _, search) = MainViewModelEditorHostTests.Build();
        vm.SearchText = "   ";

        vm.SearchCommand.Execute(null);

        vm.SearchResults.Should().BeEmpty();
        search.LastQuery.Should().BeNull();
    }

    [Fact]
    public void Search_populates_results_from_service()
    {
        var (vm, _, _, search) = MainViewModelEditorHostTests.Build();
        search.Result.Add(new SearchHit(5, "제목", "조각"));
        vm.SearchText = "조각";

        vm.SearchCommand.Execute(null);

        search.LastQuery.Should().Be("조각");
        vm.SearchResults.Should().ContainSingle().Which.NoteId.Should().Be(5);
    }

    [Fact]
    public void OpenSearchHit_navigates_to_hit_note()
    {
        var (vm, notes, groups, _) = MainViewModelEditorHostTests.Build();
        groups.Items.Add(new Group { Name = "업무", IsSystem = false, SortOrder = 0 });
        groups.Items[0].Id = 1;
        notes.Create(new Note { Type = NoteType.Plain, GroupId = 1, Body = "b",
            CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });
        vm.LoadGroups();

        vm.OpenSearchHitCommand.Execute(new SearchHit(1, "제목", "조각"));

        vm.SelectedNote.Should().NotBeNull();
        vm.SelectedNote!.Id.Should().Be(1);
        vm.CurrentEditor.Should().BeSameAs(vm);   // plain → MainViewModel 호스팅
    }
}

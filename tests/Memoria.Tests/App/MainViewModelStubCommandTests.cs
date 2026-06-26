using System;
using FluentAssertions;
using Memoria.App;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.App.Views;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelStubCommandTests
{
    private static MainViewModel NewVm(FakeNoteRepository? notes = null, FakeTimeProvider? time = null)
    {
        time ??= new FakeTimeProvider();
        return new MainViewModel(
            new FakeGroupRepository(),
            notes ?? new FakeNoteRepository(),
            new DebounceAutosaveService(time, 500),
            new FakeRecoveryJournal(),
            time);
    }

    [Fact]
    public void Stub_commands_execute_without_throwing()
    {
        var vm = NewVm();

        // M9 stubs: NewChecklist/OpenWeeklyReport/Search/OpenSearchHit — still empty bodies.
        vm.NewChecklistCommand.Execute(null);
        vm.OpenWeeklyReportCommand.Execute(null);
        vm.SearchCommand.Execute(null);
        vm.OpenSearchHitCommand.Execute(new SearchHit(1, "title", "snippet"));

        vm.SearchResults.Should().BeEmpty();
        vm.SearchText.Should().BeEmpty();
    }

    [Fact]
    public void OpenSettingsCommand_delegates_to_ISettingsWindowService()
    {
        // AppServices에 no-op 구현을 등록하여 WPF 없이 동작 검증.
        var fake = new FakeSettingsWindowService();
        var sc = new ServiceCollection();
        sc.AddSingleton<ISettingsWindowService>(fake);
        AppServices.Initialize(sc.BuildServiceProvider());

        var vm = NewVm();
        vm.OpenSettingsCommand.Execute(null);

        fake.ShowSettingsCalled.Should().BeTrue();

        AppServices.Reset(); // 정적 상태 초기화 — 다른 테스트와 격리
    }

    [Fact]
    public void OpenNote_sets_CurrentNoteType()
    {
        var notes = new FakeNoteRepository();
        var time = new FakeTimeProvider();
        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Title = "t", Body = "b", CreatedAt = t0, UpdatedAt = t0 });
        var vm = NewVm(notes, time);

        vm.OpenNote(1);

        vm.CurrentNoteType.Should().Be(NoteType.Plain);
    }

    [Fact]
    public void Setting_SelectedNote_opens_that_note()
    {
        var notes = new FakeNoteRepository();
        var time = new FakeTimeProvider();
        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Title = "sel", Body = "body", CreatedAt = t0, UpdatedAt = t0 });
        var vm = NewVm(notes, time);

        vm.SelectedNote = new NoteListItemViewModel(1, "sel", false, t0);

        vm.IsEditorVisible.Should().BeTrue();
        vm.EditorBody.Should().Be("body");
    }
}

/// OpenSettingsCommand 위임 검증용 no-op 가짜 서비스.
file sealed class FakeSettingsWindowService : ISettingsWindowService
{
    public bool ShowSettingsCalled { get; private set; }
    public void ShowSettings() => ShowSettingsCalled = true;
}

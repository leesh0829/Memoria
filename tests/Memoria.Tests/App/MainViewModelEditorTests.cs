using System;
using FluentAssertions;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelEditorTests
{
    private static (MainViewModel vm, FakeNoteRepository notes, FakeRecoveryJournal rec, FakeTimeProvider time, IAutosaveService autosave)
        Build(int debounceMs = 500)
    {
        var groups = new FakeGroupRepository();
        var notes = new FakeNoteRepository();
        var rec = new FakeRecoveryJournal();
        var time = new FakeTimeProvider();
        var autosave = new DebounceAutosaveService(time, debounceMs);
        var vm = new MainViewModel(groups, notes, autosave, rec, time);
        return (vm, notes, rec, time, autosave);
    }

    [Fact]
    public void OpenNote_loads_fields_and_header()
    {
        var (vm, notes, _, time, _) = Build();
        var created = new DateTimeOffset(2026, 6, 22, 14, 3, 0, TimeSpan.Zero);
        notes.Create(new Note { Type = NoteType.Plain, Title = "제목", Body = "본문", CreatedAt = created, UpdatedAt = created });

        vm.OpenNote(1);

        vm.EditorTitle.Should().Be("제목");
        vm.EditorBody.Should().Be("본문");
        vm.IsEditorVisible.Should().BeTrue();
        vm.HeaderText.Should().StartWith("생성 ");
    }

    [Fact]
    public void Editing_body_appends_recovery_and_autosaves_with_updated_at()
    {
        var (vm, notes, rec, time, _) = Build();
        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Title = null, Body = "old", CreatedAt = t0, UpdatedAt = t0 });
        vm.OpenNote(1);
        time.Advance(TimeSpan.FromMinutes(1));        // updated_at 이 created 와 달라지도록

        vm.EditorBody = "new content";

        rec.Appended.Should().ContainSingle();        // §8.1 복구 저널 append
        time.Advance(TimeSpan.FromMilliseconds(500)); // 디바운스 경과 → 저장

        notes.Items[0].Body.Should().Be("new content");
        notes.Items[0].UpdatedAt.Should().Be(time.GetUtcNow());  // §7.7 콘텐츠 변경 시 갱신
        rec.Cleared.Should().Contain(1);              // 정상 저장 후 저널 삭제
    }

    [Fact]
    public void Blank_title_is_saved_as_null()
    {
        var (vm, notes, _, time, _) = Build();
        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Title = "x", Body = "b", CreatedAt = t0, UpdatedAt = t0 });
        vm.OpenNote(1);

        vm.EditorTitle = "   ";
        time.Advance(TimeSpan.FromMilliseconds(500));

        notes.Items[0].Title.Should().BeNull();
    }

    [Fact]
    public void ApplyRecovery_writes_snapshot_back_and_clears_journal()
    {
        var (vm, notes, rec, time, _) = Build();
        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Title = null, Body = "stale", CreatedAt = t0, UpdatedAt = t0 });

        vm.ApplyRecovery(new[] { new RecoverySnapshot(1, null, "recovered body", t0.AddMinutes(5)) });

        notes.Items[0].Body.Should().Be("recovered body");
        rec.Cleared.Should().Contain(1);
    }
}

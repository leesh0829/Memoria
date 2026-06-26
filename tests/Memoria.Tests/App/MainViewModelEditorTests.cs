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
        var vm = new MainViewModel(groups, notes, autosave, rec, time,
            new FakeSearchService(),
            M9EditorFakes.ChecklistFactory(notes, groups),
            M9EditorFakes.WeeklyFactory(notes, groups, time));
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

    // 교차노트 레이스 회귀: 노트1의 자동저장 콜백이 노트2로 전환된 뒤 뒤늦게(in-flight) 발화해도
    // 변경 시점 스냅샷으로 저장하므로 라이브 에디터(노트2) 상태가 노트1을 오염시키지 않아야 한다.
    [Fact]
    public void Late_autosave_for_previous_note_uses_snapshot_not_live_editor_state()
    {
        var groups = new FakeGroupRepository();
        var notes = new FakeNoteRepository();
        var rec = new FakeRecoveryJournal();
        var time = new FakeTimeProvider();
        var autosave = new CapturingAutosaveService();
        var vm = new MainViewModel(groups, notes, autosave, rec, time,
            new FakeSearchService(),
            M9EditorFakes.ChecklistFactory(notes, groups),
            M9EditorFakes.WeeklyFactory(notes, groups, time));

        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Body = "alpha", CreatedAt = t0, UpdatedAt = t0 });
        notes.Create(new Note { Type = NoteType.Plain, Body = "beta", CreatedAt = t0, UpdatedAt = t0 });

        vm.OpenNote(1);
        vm.EditorBody = "alpha-edited";   // 노트1 변경 → 노트1 스냅샷이 캡처/보류됨

        vm.OpenNote(2);                    // 노트2로 전환 → 라이브 에디터 = 'beta'
        vm.EditorBody = "beta-edited";     // 노트2 변경

        // 노트1의 보류 자동저장이 전환 후 뒤늦게 발화하는 상황.
        autosave.FirePending(1);

        notes.Get(1)!.Body.Should().Be("alpha-edited");  // 라이브(노트2) 상태로 오염되면 안 됨
        notes.Get(2)!.Body.Should().Be("beta");          // 노트2는 아직 저장 전(보류)
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

    // 보류 저장을 즉시 실행하지 않고 보관해, in-flight 타이머가 전환 후 뒤늦게 발화하는
    // 상황을 결정론적으로 재현하는 자동저장 페이크.
    private sealed class CapturingAutosaveService : IAutosaveService
    {
        private readonly System.Collections.Generic.Dictionary<int, Action<AutosaveSnapshot>> _actions = new();
        private readonly System.Collections.Generic.Dictionary<int, AutosaveSnapshot> _pending = new();

        public void Register(int noteId, Action<AutosaveSnapshot> saveAction) => _actions[noteId] = saveAction;
        public void Unregister(int noteId) { _actions.Remove(noteId); _pending.Remove(noteId); }
        public void NotifyChanged(int noteId, AutosaveSnapshot snapshot) => _pending[noteId] = snapshot;
        public void FlushAll() { /* 의도적으로 비움 — 보류 저장을 즉시 확정하지 않는다 */ }

        public void FirePending(int noteId) => _actions[noteId](_pending[noteId]);
    }
}

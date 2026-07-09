using FluentAssertions;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelMarkdownTests
{
    private static (MainViewModel vm, FakeNoteRepository notes, FakeTimeProvider time)
        Build(int debounceMs = 500)
    {
        var groups = new FakeGroupRepository();
        var notes = new FakeNoteRepository();
        var time = new FakeTimeProvider();
        var autosave = new DebounceAutosaveService(time, debounceMs);
        var vm = new MainViewModel(groups, notes, autosave, new FakeRecoveryJournal(), time,
            new FakeSearchService(),
            M9EditorFakes.ChecklistFactory(notes, groups),
            M9EditorFakes.WeeklyFactory(notes, groups, time));
        vm.LoadGroups(); // (미분류) 노드를 포함한 사이드바 구성
        return (vm, notes, time);
    }

    /// <summary>
    /// ConvertToMarkdown은 열린 후 편집된 본문을 유실하지 않아야 한다 (data-loss regression).
    ///
    /// 버그 코드: _current(열 때 값)을 그대로 Update → 자동저장으로 보존된 편집 내용 초기화.
    ///   재현: open "hello" → edit "hello world" → autosave → ConvertToMarkdown
    ///         → _current.Body == "hello" 이므로 Update가 "hello"로 덮어씀.
    ///
    /// 수정 코드: FlushAll() → 최신 DB 행 Get → BodyFormat만 변경 → Update.
    /// </summary>
    [Fact]
    public void ConvertToMarkdown_preserves_edited_body_and_sets_markdown_format()
    {
        var (vm, notes, time) = Build();
        var t0 = time.GetUtcNow();
        var noteId = notes.Create(new Note
        {
            Type = NoteType.Plain,
            Body = "hello",
            BodyFormat = "plain",
            CreatedAt = t0,
            UpdatedAt = t0,
        });

        // 노트 열기 (_current 설정, GroupId=null → (미분류) 노드)
        vm.NavigateToNote(noteId, null);
        vm.EditorBody.Should().Be("hello"); // 열 때 상태 확인

        // 라이브 편집 → autosave 큐에 보류(디바운스 미경과)
        vm.EditorBody = "hello world";

        // 마크다운으로 전환 — 수정 전 코드라면 "hello"로 퇴행함
        vm.ConvertToMarkdownCommand.Execute(null);

        // 저장된 노트: 편집 내용 보존 + BodyFormat == "markdown" (data-loss guard)
        var persisted = notes.Get(noteId)!;
        persisted.Body.Should().Be("hello world",
            "ConvertToMarkdown은 FlushAll 후 최신 DB 행을 사용해야 하며 편집 내용을 유실하면 안 된다");
        persisted.BodyFormat.Should().Be("markdown");

        // VM 상태
        vm.BodyFormat.Should().Be("markdown");
        vm.ViewMode.Should().Be(MarkdownViewMode.Edit, "전환 직후는 편집 모드여야 한다");
    }
}

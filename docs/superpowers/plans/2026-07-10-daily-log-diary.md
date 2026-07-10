# 일일업무일지 다이어리 (날짜당 1개) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 체크리스트 메모를 "날짜당 1개(다이어리)"로 만든다 — 목록 제목=날짜, "새 체크리스트"=오늘 find-or-create, DatePicker=그날로 이동(빈 날짜는 지연 생성).

**Architecture:** 날짜 이동 로직을 `MainViewModel.OpenChecklistForDate` 한 곳으로 모은다. `ChecklistViewModel`은 `_note`를 직접 바꾸지 않고 이벤트(`NavigateToDateRequested`/`NoteMaterialized`)만 발생시키며, `MainViewModel`이 flush→조회→`NavigateToNote`로 중간 패널/에디터를 일괄 동기화한다. 빈 날짜로의 이동은 노트를 만들지 않고(draft), 첫 항목 추가 시에만 생성(lazy-create)한다. 스키마 변경 없음.

**Tech Stack:** C#/.NET9, WPF(net9.0-windows), MVVM(CommunityToolkit.Mvvm), SQLite(Microsoft.Data.Sqlite + Dapper), xUnit + FluentAssertions.

## Global Constraints

- **스키마 변경 없음.** `notes.log_date`(+ `idx_notes_log_date`)는 이미 존재. TargetVersion 2 유지. UNIQUE 제약/마이그레이션 추가 금지.
- **DB `Title`은 계속 `null`.** 날짜 제목은 **표시 전용**(FTS/검색 무오염). `NoteRepository.Update`로 Title을 쓰지 않는다.
- **요일은 고정 배열** `"일월화수목금토"[(int)DayOfWeek]` (Sunday=0). ambient culture `ToString("ddd")` 금지.
- **"가장 오래된 것" = `MIN(id)` AND `deleted_at IS NULL`** 단일 규칙(`ORDER BY id LIMIT 1`).
- **날짜 이동 = 조회 전용, 생성은 지연.** DatePicker 이동은 노트를 만들지 않는다. "새 체크리스트" 버튼만 즉시 create-if-none.
- **모든 날짜 이동은 `MainViewModel` 경유**(`ChecklistViewModel`은 `_note` 직접 교체 금지).
- **빌드**: WSL→Windows `dotnet.exe` interop. 빌드 전 `taskkill.exe /IM Memoria.exe /F 2>/dev/null`. 빌드 경고 0.
- **테스트**: 기존 356 + 신규 전부 그린. 커밋 트레일러 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- 빌드/테스트 명령: `dotnet.exe build -c Release`, `dotnet.exe test tests/Memoria.Tests -c Release`.

## File Structure

| 파일 | 책임 | 변경 |
|---|---|---|
| `src/Memoria.Core/Data/INoteRepository.cs` | 노트 저장소 계약 | `FindChecklistForDate` 추가 |
| `src/Memoria.Core/Data/NoteRepository.cs` | SQLite 구현 | `FindChecklistForDate` 구현 |
| `tests/Memoria.Tests/App/Fakes/FakeNoteRepository.cs` | App VM 테스트용 fake | `FindChecklistForDate` 구현 |
| `tests/Memoria.Tests/Fakes/FakeNoteRepository.cs` | Core/VM 테스트용 fake | `FindChecklistForDate` 구현 |
| `src/Memoria.App/ViewModels/NoteTitleResolver.cs` | 목록 제목 규칙(순수) | 체크리스트 날짜 제목 + 중복 접미사 |
| `src/Memoria.App/ViewModels/ChecklistViewModel.cs` | 체크리스트 에디터 VM | 이벤트/LoadDraft/lazy AddItem/OnLogDateChanged |
| `src/Memoria.App/ViewModels/MainViewModel.cs` | 셸/네비게이션 | NewChecklist find-or-create, OpenChecklistForDate, draft/materialize, guard, LoadNotes 접미사 |
| `src/Memoria.App/Converters/DateOnlyToDateTimeConverter.cs` | DatePicker 바인딩 | 비우기 = no-op |

**테스트 파일**: `tests/Memoria.Tests/Data/NoteRepositoryTests.cs`(T1), `tests/Memoria.Tests/App/NoteTitleResolverTests.cs`(T2), `tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs`(T3), `tests/Memoria.Tests/App/MainViewModelNewChecklistTests.cs`+신규 `MainViewModelDailyLogNavTests.cs`(T4/T5), `tests/Memoria.Tests/App/DateOnlyToDateTimeConverterTests.cs`(신규, T6).

---

### Task 1: `FindChecklistForDate` 저장소 메서드 (Core + fakes)

**Files:**
- Modify: `src/Memoria.Core/Data/INoteRepository.cs:17` (인터페이스에 메서드 추가)
- Modify: `src/Memoria.Core/Data/NoteRepository.cs:176` (`GetChecklistsInWeek` 뒤에 구현)
- Modify: `tests/Memoria.Tests/App/Fakes/FakeNoteRepository.cs:43`
- Modify: `tests/Memoria.Tests/Fakes/FakeNoteRepository.cs:69`
- Test: `tests/Memoria.Tests/Data/NoteRepositoryTests.cs` (신규 테스트 추가)

**Interfaces:**
- Produces: `Note? INoteRepository.FindChecklistForDate(DateOnly date)` — 해당 날짜의 활성(미삭제) 체크리스트 중 `MIN(id)` 하나, 없으면 `null`. (Task 4·5가 사용.)

- [ ] **Step 1: 실패하는 테스트 작성** — `tests/Memoria.Tests/Data/NoteRepositoryTests.cs` 끝(마지막 `}` 앞)에 추가:

```csharp
    [Fact]
    public void FindChecklistForDate_ReturnsNull_WhenNone()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        sut.FindChecklistForDate(new DateOnly(2026, 7, 6)).Should().BeNull();
    }

    [Fact]
    public void FindChecklistForDate_ReturnsMatch_ForThatDate()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) });
        sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 7) });

        sut.FindChecklistForDate(new DateOnly(2026, 7, 6))!.Id.Should().Be(id);
    }

    [Fact]
    public void FindChecklistForDate_ReturnsLowestId_WhenDuplicates()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var first = sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) });
        sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) });

        sut.FindChecklistForDate(new DateOnly(2026, 7, 6))!.Id.Should().Be(first);
    }

    [Fact]
    public void FindChecklistForDate_IgnoresSoftDeleted()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) });
        sut.SoftDelete(id);

        sut.FindChecklistForDate(new DateOnly(2026, 7, 6)).Should().BeNull();
    }
```

- [ ] **Step 2: 컴파일 실패 확인**

Run: `dotnet.exe build tests/Memoria.Tests -c Release`
Expected: FAIL — `'INoteRepository' does not contain a definition for 'FindChecklistForDate'`

- [ ] **Step 3: 인터페이스에 메서드 추가** — `INoteRepository.cs`의 `FindWeeklyReport` 줄(L17) 뒤에:

```csharp
    Note? FindChecklistForDate(DateOnly date);    // 활성 체크리스트 중 MIN(id), 없으면 null
```

- [ ] **Step 4: `NoteRepository` 구현** — `GetChecklistsInWeek`(L163-176) 바로 뒤, `FindWeeklyReport` 앞에:

```csharp
    public Note? FindChecklistForDate(DateOnly date)
    {
        using var conn = _factory.Open();
        return conn.QuerySingleOrDefault<Note>(
            $"SELECT {SelectColumns} FROM notes " +
            "WHERE type = 'Checklist' AND deleted_at IS NULL AND log_date = @Date " +
            "ORDER BY id LIMIT 1;",
            new { Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
    }
```

- [ ] **Step 5: 두 fake 구현** — `tests/Memoria.Tests/App/Fakes/FakeNoteRepository.cs`의 `FindWeeklyReport`(L43) 뒤에:

```csharp
    public Note? FindChecklistForDate(DateOnly date) =>
        Items.Where(n => n.DeletedAt == null && n.Type == NoteType.Checklist && n.LogDate == date)
             .OrderBy(n => n.Id).FirstOrDefault();
```

그리고 `tests/Memoria.Tests/Fakes/FakeNoteRepository.cs`의 `FindWeeklyReport`(L71-73) 뒤에 동일한 메서드 본문 추가(같은 코드).

- [ ] **Step 6: 테스트 통과 확인**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~FindChecklistForDate"`
Expected: PASS (4/4)

- [ ] **Step 7: 커밋**

```bash
git add -A && git commit -m "feat(diary): NoteRepository.FindChecklistForDate (MIN id, active only)"
```

---

### Task 2: 날짜 제목 + 중복 접미사 (`NoteTitleResolver`, 순수)

**Files:**
- Modify: `src/Memoria.App/ViewModels/NoteTitleResolver.cs`
- Test: `tests/Memoria.Tests/App/NoteTitleResolverTests.cs`

**Interfaces:**
- Produces: `string NoteTitleResolver.Resolve(Note)` — 체크리스트+LogDate면 `"yyyy-MM-dd (요일)"`, 아니면 기존 규칙. `IReadOnlyList<string> NoteTitleResolver.ResolveList(IReadOnlyList<Note> notes)` — 같은 log_date 체크리스트가 2개 이상이면 2번째(id 오름차순)부터 ` (N)` 접미사. (Task 4의 `LoadNotes`가 `ResolveList` 사용.)

- [ ] **Step 1: 실패하는 테스트 작성** — `NoteTitleResolverTests.cs`의 마지막 `}` 앞에 추가:

```csharp
    [Fact]
    public void Checklist_with_log_date_shows_date_title()
    {
        var note = new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) };
        NoteTitleResolver.Resolve(note).Should().Be("2026-07-06 (월)");
    }

    [Theory]
    [InlineData(2026, 7, 5, "일")]   // Sunday
    [InlineData(2026, 7, 6, "월")]
    [InlineData(2026, 7, 11, "토")]  // Saturday
    public void Checklist_weekday_uses_fixed_korean_array(int y, int m, int d, string wd)
    {
        var note = new Note { Type = NoteType.Checklist, LogDate = new DateOnly(y, m, d) };
        NoteTitleResolver.Resolve(note).Should().Be($"{y:0000}-{m:00}-{d:00} ({wd})");
    }

    [Fact]
    public void Checklist_date_title_takes_precedence_over_body()
    {
        var note = new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6), Body = "무시" };
        NoteTitleResolver.Resolve(note).Should().Be("2026-07-06 (월)");
    }

    [Fact]
    public void Plain_note_with_log_date_is_not_date_titled()
    {
        var note = new Note { Type = NoteType.Plain, LogDate = new DateOnly(2026, 7, 6), Body = "본문" };
        NoteTitleResolver.Resolve(note).Should().Be("본문");
    }

    [Fact]
    public void Checklist_without_log_date_falls_back_to_placeholder()
    {
        var note = new Note { Type = NoteType.Checklist, LogDate = null };
        NoteTitleResolver.Resolve(note).Should().Be("(제목 없음)");
    }

    [Fact]
    public void ResolveList_suffixes_duplicate_dates_by_id_order()
    {
        var notes = new List<Note>
        {
            new() { Id = 5, Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) },
            new() { Id = 2, Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) },
            new() { Id = 9, Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) },
            new() { Id = 3, Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 7) },
        };
        var titles = NoteTitleResolver.ResolveList(notes);
        // 입력 순서 보존, id 오름차순으로 접미사(2:정본, 5:(2), 9:(3))
        titles[0].Should().Be("2026-07-06 (월) (2)");   // id 5
        titles[1].Should().Be("2026-07-06 (월)");       // id 2 (MIN → 정본)
        titles[2].Should().Be("2026-07-06 (월) (3)");   // id 9
        titles[3].Should().Be("2026-07-07 (화)");       // id 3 유일
    }
```

`NoteTitleResolverTests.cs` 상단 using에 `using System;` 와 `using System.Collections.Generic;` 가 없으면 추가.

- [ ] **Step 2: 실패 확인**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~NoteTitleResolver"`
Expected: FAIL — `Resolve`가 날짜 제목을 반환하지 않음 / `ResolveList` 미정의

- [ ] **Step 3: 구현** — `NoteTitleResolver.cs` 전체를 아래로 교체:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Memoria.Core.Models;
using Memoria.Core.Text;

namespace Memoria.App.ViewModels;

/// <summary>
/// 제목 표시 규칙: 체크리스트+log_date면 날짜 제목("2026-07-06 (월)").
/// 그 외에는 title → body 첫 비어있지 않은 줄 → "(제목 없음)".
/// markdown 노트는 선행 마커 제거. 모두 표시 전용(DB 미변경).
/// </summary>
public static class NoteTitleResolver
{
    private const string Weekdays = "일월화수목금토";   // DayOfWeek.Sunday = 0

    public static string Resolve(Note note)
    {
        // 체크리스트 다이어리: 날짜를 제목으로(Title/Body보다 우선).
        if (note.Type == NoteType.Checklist && note.LogDate is DateOnly d)
            return FormatChecklistDate(d);

        if (!string.IsNullOrWhiteSpace(note.Title))
            return note.Title!.Trim();

        if (!string.IsNullOrEmpty(note.Body))
        {
            var isMarkdown = note.BodyFormat == "markdown";
            foreach (var line in note.Body.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                return isMarkdown ? MarkdownText.StripMarkers(trimmed) : trimmed;
            }
        }
        return "(제목 없음)";
    }

    /// 목록 컨텍스트에서 같은 log_date 체크리스트가 2개 이상이면 id 오름차순으로
    /// 2번째부터 " (2)", " (3)" 접미사를 붙인다(정본=MIN id는 접미사 없음). 입력 순서 보존.
    public static IReadOnlyList<string> ResolveList(IReadOnlyList<Note> notes)
    {
        // (Checklist, log_date) 그룹별 id 오름차순 랭크 계산.
        var rank = new Dictionary<int, int>();   // noteId → 1-based rank (그 날짜 내 id 순)
        foreach (var g in notes
            .Where(n => n.Type == NoteType.Checklist && n.LogDate is not null)
            .GroupBy(n => n.LogDate!.Value))
        {
            var ordered = g.OrderBy(n => n.Id).ToList();
            for (int i = 0; i < ordered.Count; i++) rank[ordered[i].Id] = i + 1;
        }

        var result = new List<string>(notes.Count);
        foreach (var n in notes)
        {
            var title = Resolve(n);
            if (rank.TryGetValue(n.Id, out var r) && r >= 2)
                title += $" ({r})";
            result.Add(title);
        }
        return result;
    }

    internal static string FormatChecklistDate(DateOnly d)
        => $"{d:yyyy-MM-dd} ({Weekdays[(int)d.DayOfWeek]})";
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~NoteTitleResolver"`
Expected: PASS (기존 5 + 신규 전부)

- [ ] **Step 5: 커밋**

```bash
git add -A && git commit -m "feat(diary): NoteTitleResolver date title + duplicate suffix (ResolveList)"
```

---

### Task 3: `ChecklistViewModel` — 이벤트 · LoadDraft · lazy AddItem · OnLogDateChanged

**Files:**
- Modify: `src/Memoria.App/ViewModels/ChecklistViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs`

**Interfaces:**
- Consumes: `ChecklistViewModel.CreateChecklistNote(INoteRepository, IGroupRepository, DateOnly)`(기존 static, L171).
- Produces:
  - `event Action<DateOnly>? ChecklistViewModel.NavigateToDateRequested` — LogDate 변경 시 발생(로드 중 제외).
  - `event Action<int>? ChecklistViewModel.NoteMaterialized` — draft 상태에서 첫 항목 추가로 실 노트가 생겼을 때 그 id.
  - `void ChecklistViewModel.LoadDraft(DateOnly date)` — `_note=null`인 빈 상태로 로드.
  - (Task 5의 `MainViewModel`이 이 셋을 구독/호출.)

- [ ] **Step 1: 실패/교체 테스트 작성** — `ChecklistViewModelTests.cs`에서 기존 `Changing_log_date_persists_to_note`(L327-337)와 `Loading_note_does_not_repersist_log_date`(L339-350) **두 메서드를 삭제**하고, 그 자리에 아래를 추가:

```csharp
    [Fact]
    public void Changing_log_date_raises_navigate_event_and_does_not_write()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        DateOnly? raised = null;
        sut.NavigateToDateRequested += d => raised = d;

        sut.LogDate = new DateOnly(2026, 6, 27);

        raised.Should().Be(new DateOnly(2026, 6, 27));
        _notes.Updated.Should().BeEmpty();               // 재-date 쓰기 없음
        _notes.Get(1)!.LogDate.Should().Be(new DateOnly(2026, 6, 26));  // 원본 노트 날짜 불변
    }

    [Fact]
    public void Loading_note_does_not_raise_navigate_event()
    {
        var note = SeedNote();
        var sut = CreateSut();
        DateOnly? raised = null;
        sut.NavigateToDateRequested += d => raised = d;

        sut.Load(note);

        raised.Should().BeNull();
    }

    [Fact]
    public void AddTask_on_draft_materializes_note_and_raises_event()
    {
        _groups.Groups.Add(new Group { Id = 1, Name = "일일업무일지", IsSystem = true, SortOrder = 100 });
        var sut = CreateSut();
        int? materializedId = null;
        sut.NoteMaterialized += id => materializedId = id;

        sut.LoadDraft(new DateOnly(2026, 7, 8));
        sut.AddTask();

        materializedId.Should().NotBeNull();
        var created = _notes.Get(materializedId!.Value)!;
        created.Type.Should().Be(NoteType.Checklist);
        created.LogDate.Should().Be(new DateOnly(2026, 7, 8));
        created.GroupId.Should().Be(1);
        sut.Items.Should().ContainSingle();
        _checklist.Items.Should().ContainSingle(i => i.NoteId == created.Id);
    }

    [Fact]
    public void AddTask_on_loaded_note_does_not_create_new_note()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);

        sut.AddTask();

        _notes.Created.Should().BeEmpty();               // 실 노트 로드 상태 → materialize 안 함
        _checklist.Items.Should().ContainSingle(i => i.NoteId == 1);
    }
```

> 참고: `_notes`는 `Memoria.Tests.Fakes.FakeNoteRepository`(멤버 `Updated`, `Created`). `_groups`는 `FakeGroupRepository`(멤버 `Groups`).

- [ ] **Step 2: 실패 확인**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~ChecklistViewModel"`
Expected: FAIL — `NavigateToDateRequested`/`NoteMaterialized`/`LoadDraft` 미정의

- [ ] **Step 3: 이벤트 필드 추가** — `ChecklistViewModel.cs`의 `[ObservableProperty] private DateOnly _logDate;`(L35-36) 아래에:

```csharp
    /// 날짜 변경 요청(MainViewModel이 해당 날짜 체크리스트로 이동). VM은 _note를 직접 바꾸지 않는다.
    public event Action<DateOnly>? NavigateToDateRequested;
    /// draft(빈) 상태에서 첫 항목 추가로 실 노트가 생성됐을 때 그 id.
    public event Action<int>? NoteMaterialized;
```

- [ ] **Step 4: `OnLogDateChanged` 재작성** — 기존 본문(L160-167)을 교체:

```csharp
    partial void OnLogDateChanged(DateOnly value)
    {
        if (_loading) return;                    // Load/LoadDraft 초기값 설정은 무시(기존 가드)
        NavigateToDateRequested?.Invoke(value);  // 재-date 쓰기 제거 → 이동 요청만
    }
```

- [ ] **Step 5: `LoadDraft` 추가** — `Load(Note note)`(L57-77) 바로 뒤에:

```csharp
    /// 아직 노트가 없는 날짜의 빈 에디터. 첫 AddItem에서 실 노트를 생성(§lazy-create).
    public void LoadDraft(DateOnly date)
    {
        _loading = true;
        try
        {
            _note = null;
            LogDate = date;                       // _loading 가드로 이벤트 억제
            AvailableClients.Clear();
            foreach (var client in _clients.GetAll(enabledOnly: true))
                AvailableClients.Add(client);
            Items.Clear();
        }
        finally { _loading = false; }
    }
```

- [ ] **Step 6: `AddItem` lazy-create** — `private ChecklistItemViewModel AddItem(ItemKind kind)`(L85-107) 시작부에 materialize 분기 삽입. `var now = DateTimeOffset.UtcNow;` 앞에:

```csharp
        if (_note is null)   // draft → 실 노트 생성 후 진행
        {
            _note = CreateChecklistNote(_notes, _groups, LogDate);
            NoteMaterialized?.Invoke(_note.Id);
        }
```

(이후 기존 코드 `var now = ...; var model = new ChecklistItem { NoteId = _note!.Id, ... }` 그대로.)

- [ ] **Step 7: 통과 확인**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~ChecklistViewModel"`
Expected: PASS (교체 2 + 신규 2 + 기존 전부)

- [ ] **Step 8: 커밋**

```bash
git add -A && git commit -m "feat(diary): ChecklistViewModel navigate/materialize events + LoadDraft + lazy AddItem"
```

---

### Task 4: `MainViewModel` — NewChecklist find-or-create + LoadNotes 중복 접미사

**Files:**
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs` (`NewChecklist` L300-318, `LoadNotes` L267-278)
- Test: `tests/Memoria.Tests/App/MainViewModelNewChecklistTests.cs`

**Interfaces:**
- Consumes: `INoteRepository.FindChecklistForDate`(T1), `NoteTitleResolver.ResolveList`(T2), `MainViewModel.NavigateToNote(int, int?)`(기존 L321).
- Produces: `NewChecklist`가 오늘 일지 find-or-create. `LoadNotes`가 중복 접미사 반영.

- [ ] **Step 1: 실패하는 테스트 작성** — `MainViewModelNewChecklistTests.cs`의 마지막 `}` 앞에 추가:

```csharp
    [Fact]
    public void NewChecklist_twice_reuses_todays_note_no_duplicate()
    {
        var (vm, notes, groups, _) = MainViewModelEditorHostTests.Build();
        groups.Items.Add(new Group { Name = ChecklistViewModel.DailyLogGroupName, IsSystem = true, SortOrder = 100 });
        groups.Items[0].Id = 1;
        vm.LoadGroups();

        vm.NewChecklistCommand.Execute(null);
        var firstId = vm.SelectedNote!.Id;
        vm.NewChecklistCommand.Execute(null);   // 오늘 것 발견 → 재생성 안 함

        notes.Items.Count(n => n.Type == NoteType.Checklist).Should().Be(1);
        vm.SelectedNote!.Id.Should().Be(firstId);
    }

    [Fact]
    public void LoadNotes_suffixes_duplicate_dated_checklists()
    {
        var (vm, notes, groups, _) = MainViewModelEditorHostTests.Build();
        groups.Items.Add(new Group { Name = ChecklistViewModel.DailyLogGroupName, IsSystem = true, SortOrder = 100 });
        groups.Items[0].Id = 1;
        vm.LoadGroups();
        // 같은 날짜 체크리스트 2개(레거시) — App/Fakes는 Create 시 id = Items.Count+1
        notes.Create(new Note { Type = NoteType.Checklist, GroupId = 1, LogDate = new DateOnly(2026, 7, 6) });
        notes.Create(new Note { Type = NoteType.Checklist, GroupId = 1, LogDate = new DateOnly(2026, 7, 6) });

        vm.SelectedNode = vm.SystemNodes.First(n => n.GroupId == 1);   // 일일업무일지 선택 → LoadNotes

        var titles = vm.Notes.Select(n => n.DisplayTitle).ToList();
        titles.Should().Contain("2026-07-06 (월)");
        titles.Should().Contain("2026-07-06 (월) (2)");
    }
```

`using System.Linq;` 는 이미 있음(파일 L1). 필요 시 `using Memoria.Core.Models;`(있음).

- [ ] **Step 2: 실패 확인**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~MainViewModelNewChecklist"`
Expected: FAIL — 두 번째 실행이 중복 생성 / 접미사 없음

- [ ] **Step 3: `NewChecklist` find-or-create** — `MainViewModel.cs`의 `NewChecklist`(L300-318) 본문을 교체:

```csharp
    [RelayCommand]
    private void NewChecklist()
    {
        IsUndoAvailable = false;
        var today = DateOnly.FromDateTime(_time.GetUtcNow().LocalDateTime.Date);

        var existing = _noteRepo.FindChecklistForDate(today);
        if (existing is not null) { NavigateToNote(existing.Id, existing.GroupId); return; }

        var group = _groupRepo.GetAll()
            .FirstOrDefault(g => g.IsSystem && g.Name == ChecklistViewModel.DailyLogGroupName);
        var now = _time.GetUtcNow();
        var note = new Note
        {
            Type = NoteType.Checklist,
            GroupId = group?.Id,
            LogDate = today,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var id = _noteRepo.Create(note);
        NavigateToNote(id, group?.Id);
    }
```

- [ ] **Step 4: `LoadNotes` 중복 접미사** — `LoadNotes`(L267-278)의 `foreach` 루프를 `ResolveList` 사용으로 교체:

```csharp
    public void LoadNotes()
    {
        Notes.Clear();
        if (SelectedNode is null) return;

        var notes = _noteRepo.GetByGroup(SelectedNode.GroupId)
            .OrderByDescending(n => n.Pinned)
            .ThenByDescending(n => n.UpdatedAt)
            .ToList();

        var titles = NoteTitleResolver.ResolveList(notes);   // 같은 날짜 체크리스트 접미사
        for (int i = 0; i < notes.Count; i++)
            Notes.Add(new NoteListItemViewModel(notes[i].Id, titles[i], notes[i].Pinned, notes[i].UpdatedAt));
    }
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~MainViewModelNewChecklist"`
Expected: PASS (기존 1 + 신규 2)

- [ ] **Step 6: 회귀 확인 + 커밋**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~MainViewModel"`
Expected: PASS (모든 MainViewModel 테스트)

```bash
git add -A && git commit -m "feat(diary): NewChecklist find-or-create today + LoadNotes duplicate suffix"
```

---

### Task 5: `MainViewModel` — OpenChecklistForDate · draft · materialize · 선택 가드

**Files:**
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs` (`BuildEditorFor` L244-265, `OnSelectedNoteChanged` L226-241, 신규 메서드 추가)
- Test: `tests/Memoria.Tests/App/MainViewModelDailyLogNavTests.cs` (신규)

**Interfaces:**
- Consumes: `ChecklistViewModel.NavigateToDateRequested`/`NoteMaterialized`/`LoadDraft`(T3), `INoteRepository.FindChecklistForDate`(T1), `NavigateToNote`(기존).
- Produces: `void MainViewModel.OpenChecklistForDate(DateOnly date)` — flush→조회→navigate 또는 draft. 이미 호스팅 중인 노트로의 재선택은 에디터를 재생성하지 않음(포커스 보존).

- [ ] **Step 1: 신규 테스트 파일 작성** — `tests/Memoria.Tests/App/MainViewModelDailyLogNavTests.cs`:

```csharp
using System;
using System.Linq;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelDailyLogNavTests
{
    private static (MainViewModel vm, Memoria.Tests.App.Fakes.FakeNoteRepository notes) Setup()
    {
        var (vm, notes, groups, _) = MainViewModelEditorHostTests.Build();
        groups.Items.Add(new Group { Name = ChecklistViewModel.DailyLogGroupName, IsSystem = true, SortOrder = 100 });
        groups.Items[0].Id = 1;
        vm.LoadGroups();
        return (vm, notes);
    }

    [Fact]
    public void OpenChecklistForDate_navigates_to_existing_and_creates_nothing()
    {
        var (vm, notes) = Setup();
        notes.Create(new Note { Type = NoteType.Checklist, GroupId = 1, LogDate = new DateOnly(2026, 7, 6) });
        var countBefore = notes.Items.Count;

        vm.OpenChecklistForDate(new DateOnly(2026, 7, 6));

        notes.Items.Count.Should().Be(countBefore);          // 생성 없음
        vm.SelectedNote.Should().NotBeNull();
        vm.SelectedNote!.Id.Should().Be(notes.Items[0].Id);
        vm.CurrentEditor.Should().BeOfType<ChecklistViewModel>();
    }

    [Fact]
    public void OpenChecklistForDate_absent_shows_draft_without_creating()
    {
        var (vm, notes) = Setup();
        vm.NewChecklistCommand.Execute(null);                // 오늘 것 하나 열림(에디터 호스팅)
        var countBefore = notes.Items.Count;

        vm.OpenChecklistForDate(new DateOnly(2030, 1, 1));   // 없음 → draft

        notes.Items.Count.Should().Be(countBefore);          // 노트 생성 안 함
        vm.SelectedNote.Should().BeNull();                   // 목록에 해당 row 없음
        vm.CurrentEditor.Should().BeOfType<ChecklistViewModel>();  // 빈 draft 에디터
    }

    [Fact]
    public void Materialize_from_draft_adds_row_selects_and_keeps_same_editor()
    {
        var (vm, notes) = Setup();
        vm.NewChecklistCommand.Execute(null);
        vm.OpenChecklistForDate(new DateOnly(2030, 1, 1));   // draft
        var draftEditor = vm.CurrentEditor;                  // 이 인스턴스가 유지돼야 함

        ((ChecklistViewModel)vm.CurrentEditor!).AddTask();   // 첫 항목 → materialize

        var created = notes.Items.Single(n => n.LogDate == new DateOnly(2030, 1, 1));
        vm.SelectedNote.Should().NotBeNull();
        vm.SelectedNote!.Id.Should().Be(created.Id);
        vm.CurrentEditor.Should().BeSameAs(draftEditor);     // 재생성 없이 포커스 보존
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~DailyLogNav"`
Expected: FAIL — `OpenChecklistForDate` 미정의

- [ ] **Step 3: `BuildEditorFor`에서 이벤트 구독** — `BuildEditorFor`(L244-265)의 `case NoteType.Checklist:` 블록을 교체:

```csharp
            case NoteType.Checklist:
                var checklist = _checklistEditorFactory();
                checklist.NavigateToDateRequested += OpenChecklistForDate;
                checklist.NoteMaterialized += OnChecklistMaterialized;
                checklist.Load(note);
                return checklist;
```

- [ ] **Step 4: `OnSelectedNoteChanged`에 재호스팅 가드 추가** — `OnSelectedNoteChanged`(L226-241)에서 `if (value is null) { ... return; }` 블록 **직후**, `var note = _noteRepo.Get(value.Id);` 앞에 한 줄 추가:

```csharp
        // 이미 우측에 호스팅 중인 노트로의 (프로그램적) 재선택은 에디터를 다시 만들지 않는다.
        // materialize 직후 목록 강조 동기화 및 이미 열린 노트로의 navigate에서 포커스/상태 보존.
        if (value.Id == _currentEditorNoteId) return;
```

- [ ] **Step 5: 신규 메서드 3개 추가** — `NavigateToNote`(L321-330) 바로 뒤에:

```csharp
    // 날짜 선택/이동의 단일 진입점: 보류 저장 확정 → 조회 → 이동(또는 draft).
    public void OpenChecklistForDate(DateOnly date)
    {
        (CurrentEditor as ChecklistViewModel)?.FlushSaves();   // 이전 날짜 dirty 항목 확정(유실 방지)

        var found = _noteRepo.FindChecklistForDate(date);
        if (found is not null) { NavigateToNote(found.Id, found.GroupId); return; }

        LoadChecklistDraft(date);
    }

    // 노트가 없는 날짜: 생성하지 않고 빈 draft 에디터를 호스팅(첫 항목에서 실 노트 생성).
    private void LoadChecklistDraft(DateOnly date)
    {
        SelectedNote = null;                       // 이전 선택/에디터 정리(OnSelectedNoteChanged(null))
        var draft = _checklistEditorFactory();
        draft.NavigateToDateRequested += OpenChecklistForDate;
        draft.NoteMaterialized += OnChecklistMaterialized;
        draft.LoadDraft(date);
        CurrentNoteType = NoteType.Checklist;
        CurrentEditor = draft;
        IsEditorVisible = true;
        _currentEditorNoteId = null;               // 아직 실 노트 없음
    }

    // draft에서 첫 항목 추가로 실 노트가 생겼을 때: 목록 갱신 + 새 row 강조(에디터는 유지).
    private void OnChecklistMaterialized(int id)
    {
        _currentEditorNoteId = id;                 // SelectedNote 설정 전에 → 가드로 재호스팅 방지
        LoadNotes();                               // 새 날짜 row 등장
        SelectedNote = Notes.FirstOrDefault(n => n.Id == id);
    }
```

- [ ] **Step 6: 통과 확인**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~DailyLogNav"`
Expected: PASS (3/3)

- [ ] **Step 7: 회귀 확인 + 커밋**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~MainViewModel"`
Expected: PASS (전체 MainViewModel 테스트 — 특히 EditorHost/Notes/Drilldown 회귀 없음)

```bash
git add -A && git commit -m "feat(diary): MainViewModel OpenChecklistForDate + draft/materialize + rehost guard"
```

---

### Task 6: DatePicker 비우기 = no-op (`DateOnlyToDateTimeConverter`)

**Files:**
- Modify: `src/Memoria.App/Converters/DateOnlyToDateTimeConverter.cs:19-24`
- Test: `tests/Memoria.Tests/App/DateOnlyToDateTimeConverterTests.cs` (신규)

**Interfaces:**
- Produces: `ConvertBack(null,...)` → `Binding.DoNothing`(날짜 비우기가 오늘로 튀지 않음).

- [ ] **Step 1: 신규 테스트 파일 작성** — `tests/Memoria.Tests/App/DateOnlyToDateTimeConverterTests.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;
using FluentAssertions;
using Memoria.App.Converters;
using Xunit;

namespace Memoria.Tests.App;

public class DateOnlyToDateTimeConverterTests
{
    private readonly DateOnlyToDateTimeConverter _sut = new();

    [Fact]
    public void ConvertBack_datetime_returns_dateonly()
    {
        var result = _sut.ConvertBack(new DateTime(2026, 7, 6), typeof(DateOnly), null, CultureInfo.InvariantCulture);
        result.Should().Be(new DateOnly(2026, 7, 6));
    }

    [Fact]
    public void ConvertBack_null_returns_binding_donothing()
    {
        var result = _sut.ConvertBack(null, typeof(DateOnly), null, CultureInfo.InvariantCulture);
        result.Should().BeSameAs(Binding.DoNothing);   // 비우기 = no-op(오늘로 안 튐)
    }

    [Fact]
    public void Convert_dateonly_returns_datetime()
    {
        var result = _sut.Convert(new DateOnly(2026, 7, 6), typeof(DateTime?), null, CultureInfo.InvariantCulture);
        result.Should().Be(new DateTime(2026, 7, 6));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~DateOnlyToDateTimeConverter"`
Expected: FAIL — `ConvertBack(null)`이 `Binding.DoNothing`이 아닌 오늘 날짜 반환

- [ ] **Step 3: 구현** — `DateOnlyToDateTimeConverter.cs`의 `ConvertBack`(L19-24)을 교체:

```csharp
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return DateOnly.FromDateTime(dt);
        return Binding.DoNothing;   // 비우기/무효 → 바인딩 소스 미변경(오늘로 튀지 않음)
    }
```

그리고 파일 상단 using에 `using System.Windows.Data;` 가 있는지 확인(없으면 추가 — `Binding.DoNothing` 용).

- [ ] **Step 4: 통과 확인**

Run: `dotnet.exe test tests/Memoria.Tests -c Release --filter "FullyQualifiedName~DateOnlyToDateTimeConverter"`
Expected: PASS (3/3)

- [ ] **Step 5: 커밋**

```bash
git add -A && git commit -m "fix(diary): DatePicker 비우기 no-op (ConvertBack null → Binding.DoNothing)"
```

---

### Task 7: 통합 — 전체 빌드 · 테스트 · publish · GUI 검증

**Files:** (신규 없음 — 통합/검증)

- [ ] **Step 1: Memoria.exe 종료 후 전체 빌드**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build -c Release
```
Expected: 경고 0, 오류 0.

- [ ] **Step 2: 전체 테스트**

Run: `dotnet.exe test tests/Memoria.Tests -c Release`
Expected: 실패 0, 통과 356+신규(약 356+18) 전부 그린.

- [ ] **Step 3: publish (self-contained exe)**

```bash
dotnet.exe publish src/Memoria.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -o publish
```
Expected: `publish/Memoria.exe` 생성.

- [ ] **Step 4: GUI 수동 검증 체크리스트** (Windows에서 사용자 확인 — WSL 렌더 불가):
  1. 메모 목록에서 체크리스트가 `2026-07-06 (월)` 형식 제목으로 표시.
  2. "새 체크리스트" 클릭 → 오늘 일지 열림. 다시 클릭 → **중복 안 생기고** 같은 것 열림.
  3. DatePicker로 다른 날짜 선택 → 그날 일지 로드(있으면 내용, 없으면 빈 화면). 목록/에디터 동기화.
  4. 빈 날짜에서 "+ 할 일" 추가 → 목록에 그 날짜 row가 나타나고 강조, **입력 포커스 유지**.
  5. 빈 날짜로 이동만 하고 항목 추가 안 하면 → 목록에 새 노트 안 생김.
  6. 이전 날짜에서 항목 편집 직후 다른 날짜로 이동 → 편집 내용 보존(flush).
  7. DatePicker 날짜를 지우면 → 오늘로 튀지 않음(무동작).
  8. (레거시 중복 있으면) 같은 날짜가 `(월)`, `(월) (2)`로 구분 표시.
  9. 주간 리포트 생성 정상.
  10. 다크/라이트 테마 모두 정상.

- [ ] **Step 5: 최종 커밋(있으면)** — 통합 단계에서 코드 변경이 없으면 커밋 없음. publish 산출물은 커밋하지 않음(.gitignore).

---

## Self-Review (계획 작성자 체크)

**Spec coverage:**
- §3.1 자동 제목 → Task 2. §3.2 FindChecklistForDate → Task 1. §3.3 이벤트/OpenChecklistForDate → Task 3(VM 이벤트)+Task 5(MainViewModel). §3.4 draft/materialize → Task 3(LoadDraft/AddItem)+Task 5(LoadChecklistDraft/OnChecklistMaterialized/guard). §3.5 NewChecklist find-or-create → Task 4. §3.6 비우기 no-op → Task 6. §3.7 중복 접미사 → Task 2(ResolveList)+Task 4(LoadNotes). §3.8 flush → Task 5(OpenChecklistForDate FlushSaves). §7 스키마 무변경 → 전 태스크. 커버리지 완전.

**Type consistency:** `FindChecklistForDate(DateOnly)→Note?`, `Resolve(Note)→string`, `ResolveList(IReadOnlyList<Note>)→IReadOnlyList<string>`, `NavigateToDateRequested: Action<DateOnly>`, `NoteMaterialized: Action<int>`, `LoadDraft(DateOnly)`, `OpenChecklistForDate(DateOnly)` — 전 태스크에서 일관.

**Placeholder scan:** TBD/TODO 없음. 각 코드 스텝에 실제 코드 포함.

**주의(구현자):** Task 5의 `OnSelectedNoteChanged` 가드(`value.Id == _currentEditorNoteId`)는 이미 열린 노트로의 재선택을 무시한다. 재클릭-deselect(MainWindow.xaml.cs)는 `SelectedNote=null`을 먼저 거쳐 `_currentEditorNoteId`를 비우므로 가드와 충돌하지 않는다. Task 5 Step 7의 전체 MainViewModel 회귀를 반드시 확인할 것.

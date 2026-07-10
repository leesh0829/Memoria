# 일일업무일지 다이어리 (날짜당 1개) — 설계

- 날짜: 2026-07-10
- 상태: 승인 대기 → 구현 계획 전 검토
- 대상 버전: v0.7.0
- 선행: 체크리스트(M1~), v0.5.0(사이드바 드릴다운·선택동기화), v0.6.0(마크다운 3-모드)

## 1. 배경 · 목표

체크리스트(할일/이슈) 메모는 **제목 칸이 없어** 메모 목록에서 전부 `(제목 없음)`으로 보인다. 또한 "새 체크리스트"가 누를 때마다 **새 노트를 만들어** 같은 날 여러 개가 쌓인다. 사용자 요청(Model A):

> 체크리스트 페이지는 **한 페이지처럼** 쓰되, **날짜를 그날로 자동 세팅**하고, **날짜를 바꾸면 그날 쓴 내용**이 나오게. 제목은 날짜로 고정.

목표: 체크리스트를 **날짜당 하나(다이어리)** 로 만든다.
1. 목록 제목 = **날짜**(`2026-07-06 (월)`).
2. **날짜당 1개**: "새 체크리스트"는 오늘 일지를 열되(없으면 생성) 중복을 만들지 않는다.
3. **날짜 선택 = 이동**: DatePicker로 날짜를 바꾸면 그날 일지로 이동한다.

비목표(§8)는 뒤로 미룬다.

## 2. 결정 요약 (브레인스토밍 + 적대적 설계검증)

**Model A: 날짜당 체크리스트 1개.** 3가지 핵심 원칙이 전체를 관통한다.

1. **날짜 이동 = 조회 전용, 생성은 지연(lazy-create).** DatePicker로 날짜를 바꾸는 것은 "그날 뭐 썼나 보기"다. **빈 날짜로 이동해도 노트를 만들지 않는다.** 실제 노트 row는 사용자가 그 날짜에서 **첫 항목을 추가**할 때만 생성된다(빈 노트 스팸 방지). "새 체크리스트" **버튼만** 명시적 생성 의도로 즉시 create-if-none.
2. **모든 날짜 이동은 `MainViewModel`을 경유.** `ChecklistViewModel`은 자기 `_note`를 스스로 바꾸지 않는다. 대신 이벤트를 발생시키고, `MainViewModel.OpenChecklistForDate`가 **저장 flush → 조회 → `NavigateToNote`** 로 중간 패널 선택·에디터·`_currentEditorNoteId`를 일괄 동기화한다(v0.5.0 드릴다운/선택 정합성 유지).
3. **"가장 오래된 것 = `MIN(id)` AND `deleted_at IS NULL`" 단일 규칙.** 조회는 `ORDER BY id LIMIT 1`. 소프트삭제 row는 절대 매치하지 않는다. **스키마 변경·UNIQUE 제약·마이그레이션 없음**(`log_date` 컬럼+인덱스는 이미 존재).

## 3. 상세 설계

### 3.1 자동 제목 — `NoteTitleResolver.Resolve` (App, 순수)

현재 `Resolve(Note)`는 Title → Body 첫 줄 → `(제목 없음)` 순. **맨 앞에** 체크리스트 날짜 분기를 추가한다:

```csharp
public static string Resolve(Note note)
{
    // 체크리스트 다이어리: 날짜를 제목으로. Title/Body보다 우선.
    if (note.Type == NoteType.Checklist && note.LogDate is DateOnly d)
        return FormatChecklistDate(d);   // "2026-07-06 (월)"
    // ... 기존 Title / Body 첫 줄 / "(제목 없음)"
}

internal static string FormatChecklistDate(DateOnly d)
    => $"{d:yyyy-MM-dd} ({"일월화수목금토"[(int)d.DayOfWeek]})";   // 예: 2026-07-06 (월)
```

- 요일은 **고정 배열** `"일월화수목금토"[(int)d.DayOfWeek]`(Sunday=0). ambient culture `ToString("ddd")` **금지**(CI 로케일에서 "Mon" 렌더 → flaky).
- **`Type==Checklist && LogDate!=null` 로 엄격 게이트.** Plain+LogDate는 날짜 제목이 되지 않는다. **`LogDate==null` 체크리스트는 기존 fallback**(`(제목 없음)`).
- **DB Title은 계속 `null`**(표시 전용). FTS/검색 무오염.

### 3.2 날짜별 조회 — `INoteRepository.FindChecklistForDate` (Core)

`FindWeeklyReport`(NoteRepository.cs L178-190)와 동일 패턴의 신규 메서드:

```csharp
Note? FindChecklistForDate(DateOnly date);
// SELECT ... FROM notes
// WHERE type='Checklist' AND deleted_at IS NULL AND log_date = @Date
// ORDER BY id LIMIT 1;      // 가장 오래된 것(MIN id)
// @Date = date.ToString("yyyy-MM-dd", InvariantCulture)
```

`INoteRepository` 인터페이스 + `NoteRepository` 구현 + **두 Fake**(`tests/.../App/Fakes/FakeNoteRepository.cs`, `tests/.../Fakes/FakeNoteRepository.cs`)에 구현 추가(안 하면 컴파일 실패).

### 3.3 날짜 이동 — 이벤트 → `MainViewModel.OpenChecklistForDate` (App)

**`ChecklistViewModel`**(재진입/루프 방지 위해 write를 제거):

```csharp
public event Action<DateOnly>? NavigateToDateRequested;   // 날짜 변경 요청
public event Action<int>? NoteMaterialized;               // 지연 생성으로 실 노트 확정(§3.4)

partial void OnLogDateChanged(DateOnly value)
{
    if (_loading) return;                       // Load/LoadDraft 중 초기값은 무시(기존 가드)
    if (value == _note?.LogDate) return;        // 동일 날짜 → converter round-trip 억제
    NavigateToDateRequested?.Invoke(value);     // 기존 _notes.Update(재-date) 제거
}
```

**`MainViewModel`** (신규):

```csharp
public void OpenChecklistForDate(DateOnly date)
{
    // 1) 떠나기 전 현재 체크리스트의 보류 편집 확정(데이터 유실 방지) — DeleteNote L343 패턴
    (CurrentEditor as ChecklistViewModel)?.FlushSaves();

    // 2) 조회
    var found = _noteRepo.FindChecklistForDate(date);
    if (found is not null)
        NavigateToNote(found.Id, found.GroupId);     // 중간 패널·에디터 일괄 동기화(L321-330)
    else
        LoadChecklistDraft(date);                    // 생성하지 않고 빈 에디터만(§3.4)
}
```

- `BuildEditorFor`(L252-255)에서 체크리스트 팩토리 생성 직후 두 이벤트 구독:
  ```csharp
  var checklist = _checklistEditorFactory();
  checklist.NavigateToDateRequested += OpenChecklistForDate;
  checklist.NoteMaterialized += OnChecklistMaterialized;
  checklist.Load(note);
  ```
- **재진입 안전**: `found` 경로는 `NavigateToNote → BuildEditorFor → Load(note)`가 `_loading=true`로 `LogDate`를 세팅하므로 `OnLogDateChanged`가 이벤트를 다시 쏘지 않는다(루프 차단). §3.3의 "동일 날짜" 가드가 이중 안전망.
- **시스템 그룹 고정**: 체크리스트는 항상 `일일업무일지` SystemNode에 착지. 시스템 그룹이 없으면(비정상) **미분류로 fallback하지 않고 no-op**.

### 3.4 지연 생성 — draft 상태 & materialize (App)

**draft 로드**(`MainViewModel.LoadChecklistDraft(date)`): 현재 호스팅 중인 `ChecklistViewModel`을 그 날짜의 **빈 상태**로 만든다(새 VM 생성·노트 생성 없음).

```csharp
// ChecklistViewModel
public void LoadDraft(DateOnly date)
{
    _loading = true;
    try { _note = null; LogDate = date; LoadClients(); Items.Clear(); }
    finally { _loading = false; }
}
```

`MainViewModel.LoadChecklistDraft(date)`: `(CurrentEditor as ChecklistViewModel)?.LoadDraft(date)` + `SelectedNote = null`(목록에 해당 row 없음) + `_currentEditorNoteId = null`.

**첫 입력 시 materialize**(`ChecklistViewModel.AddItem`, L85 확장):

```csharp
private ChecklistItemViewModel AddItem(ItemKind kind)
{
    if (_note is null)                                  // draft → 실 노트 생성
    {
        _note = CreateChecklistNote(_notes, _groups, LogDate);   // L171 재활용
        NoteMaterialized?.Invoke(_note.Id);
    }
    // ... 기존 항목 추가 로직(NoteId = _note.Id)
}
```

**`MainViewModel.OnChecklistMaterialized(int id)`**: 목록에 새 dated row가 나타나고 강조되게 하되 **에디터를 재생성하지 않는다**(타이핑 포커스 유지):
- `_currentEditorNoteId = id;`
- `LoadNotes();`  // 새 dated row 등장
- `SelectedNote = Notes.FirstOrDefault(n => n.Id == id);`
- `OnSelectedNoteChanged`에 **가드** 추가: `if (value is not null && value.Id == _currentEditorNoteId) return;` → 이미 호스팅 중인 노트로의 선택은 에디터를 다시 만들지 않는다. (이 가드는 §3.3 `NavigateToNote`가 이미 열린 노트에 착지하는 경우도 깔끔히 처리.)
- **재클릭-deselect(v0.5.0)와의 정합**: deselect 경로가 `_currentEditorNoteId`를 비운 뒤 재선택하므로 가드와 충돌하지 않음(구현 시 확인).

> draft에서 항목을 추가하지 않고 다른 날짜로 이동/이탈하면 노트는 생성되지 않는다(원하는 동작).

### 3.5 "새 체크리스트" 버튼 = 오늘 find-or-create (App)

`MainViewModel.NewChecklist`(L300-318)를 항상-생성 → **find-or-create-today**로:

```csharp
private void NewChecklist()
{
    IsUndoAvailable = false;
    var today = DateOnly.FromDateTime(_time.GetUtcNow().LocalDateTime.Date);
    var found = _noteRepo.FindChecklistForDate(today);
    if (found is not null) { NavigateToNote(found.Id, found.GroupId); return; }

    // 없으면 즉시 생성(버튼은 명시적 생성 의도)
    var group = _groupRepo.GetAll().FirstOrDefault(g => g.IsSystem && g.Name == ChecklistViewModel.DailyLogGroupName);
    var note = /* Type=Checklist, GroupId=group?.Id, LogDate=today ... */;
    var id = _noteRepo.Create(note);
    NavigateToNote(id, group?.Id);
}
```

반복 클릭해도 오늘 일지 하나만 유지. (버튼이 이미 열린 오늘 노트를 다시 열 때 §3.4 가드로 no-op이 되지 않도록, 재-오픈이 필요하면 `SelectedNote=null` 후 재설정 — 구현 시 재클릭-deselect 로직과 함께 정리.)

### 3.6 빈 날짜(DatePicker 비우기) = no-op (App)

`DateOnlyToDateTimeConverter.ConvertBack`(L19-24)이 현재 `null → Today`로 떨군다 → 날짜 지우기가 오늘로 튀는 사고. **null-fallback 제거**: `ConvertBack`에서 `DateTime`이 아니면 `Binding.DoNothing` 반환 → 비우기는 아무 일도 안 함. §3.3의 "동일 날짜" 가드가 이중 안전망.

### 3.7 중복 표면화 (자동 병합 없이) (App)

같은 날짜에 이미 존재하는 여러 체크리스트(레거시)는 자동 제목으로 **동일 문자열**이 되어 목록에서 구분 불가. 자동 병합은 하지 않되(요청대로 "수동 삭제"), 사용자가 어느 것이 정본인지 알 수 있게 **`LoadNotes`에서 접미사** 부여:

- 정본 `MIN(id)` = `2026-07-06 (월)` (접미사 없음, navigate가 착지하는 것)
- 나머지(id 오름차순) = `2026-07-06 (월) (2)`, `(3)` …

순수 헬퍼로 분리해 테스트: `NoteTitleResolver.ResolveList(IReadOnlyList<Note>) -> IReadOnlyList<string>` (또는 `(int id,string title)`), 같은 `log_date` 체크리스트가 2개 이상일 때만 2번째부터 접미사. `LoadNotes`(L267-278)는 개별 `Resolve` 대신 이 목록 함수 사용. (모든 LogDate 변경이 navigate로 통일되어 in-place 재-date가 사라지므로 stale 제목 없음.)

### 3.8 저장 flush · 좀비 방지

- 날짜 이동 진입점(`OpenChecklistForDate`)이 navigate 전에 `FlushSaves()`를 호출(§3.3-1) → 이전 날짜의 dirty 항목 확정.
- 체크리스트 코드-비하인드 디바운스 타이머/`Unloaded` flush(ChecklistView.xaml.cs)는 **순수 forwarder 유지**. VM 레벨 FlushSaves를 진입점에서 명시 호출하는 방식으로, 지연 Unloaded flush가 엉뚱한(이전) 노트를 건드리지 않게 한다. `DeleteNote`의 기존 flush-먼저(L343) 패턴과 정합.

## 4. 컴포넌트 · 인터페이스

| 컴포넌트 | 계층 | 변경 | 비고 |
|---|---|---|---|
| `NoteTitleResolver.Resolve` | App(순수) | 체크리스트 날짜 제목 분기 + `FormatChecklistDate` | Title/Body보다 우선 |
| `NoteTitleResolver.ResolveList` | App(순수) | 신규(중복 접미사) | 목록 컨텍스트 |
| `INoteRepository.FindChecklistForDate` | Core | 신규 | `MIN(id)`, `deleted_at IS NULL` |
| `NoteRepository` | Core | 위 구현 | L178-190 패턴 |
| Fake × 2 | Test | `FindChecklistForDate` 구현 | 컴파일 필수 |
| `ChecklistViewModel` | App | `OnLogDateChanged` write제거+이벤트, `LoadDraft`, `AddItem` lazy-create, 이벤트 2개 | |
| `MainViewModel` | App | `OpenChecklistForDate`, `LoadChecklistDraft`, `OnChecklistMaterialized`, `NewChecklist` find-or-create, `OnSelectedNoteChanged` 가드, `LoadNotes` 접미사, `BuildEditorFor` 구독 | |
| `DateOnlyToDateTimeConverter` | App | `ConvertBack` null-fallback 제거 | 비우기 no-op |

## 5. 오류 처리 · 엣지 케이스

- **삭제 후 재방문**: `FindChecklistForDate`는 `deleted_at IS NULL`이라 삭제된 날짜는 못 찾음 → navigate는 draft(빈)만, 재생성 안 함. 첫 편집 시에만 새 row(부활/좀비 없음).
- **동일 날짜 2개↑**: navigate/find는 항상 `MIN(id)` 하나로 착지. §3.7 접미사로 표면화, 사용자가 수동 삭제.
- **시스템 그룹 없음(비정상)**: navigate no-op(미분류 fallback 금지).
- **타임존**: 제목은 `LogDate`(DateOnly)에서 직접 포맷, `_time.GetUtcNow().LocalDateTime.Date`로 "오늘" 산출(기존 규칙 일치). DateTime/ToLocalTime round-trip 금지.

## 6. 테스트 전략

**자동 (Core/App 순수·Fake)**
- `NoteRepository.FindChecklistForDate`(SQLite TestDb): 없음→null / 1개→그 row / 같은 날짜 2개→`MIN(id)` / 삭제된 것만→null.
- `NoteTitleResolver`: `Checklist+2026-07-06→"2026-07-06 (월)"`, 일·토요일 요일 매핑, Body 있어도 날짜 우선, **Plain+LogDate→날짜제목 아님**, **Checklist+LogDate=null→"(제목 없음)"**, `ResolveList` 중복 접미사 `(2)(3)`.
- `ChecklistViewModel`: `LogDate 변경→NavigateToDateRequested 발생 & _notes.Update 호출 안 함` / `Load(note)→이벤트 없음`(재진입 가드) / `동일 날짜 재설정→이벤트 없음` / `draft에서 AddItem→CreateChecklistNote 호출 & NoteMaterialized 발생`. **기존 `Changing_log_date_persists_to_note`·`Loading_note_does_not_repersist_log_date` 테스트는 뒤집힌 시맨틱이므로 교체.**
- `MainViewModel`(Fake): `NewChecklist` 없음→생성+선택 / 있음→생성 안 하고 그 id 선택 / 버튼 2회→동일 노트 / `OpenChecklistForDate` 없음→draft(생성 안 함)·있음→navigate.

**수동 (Windows GUI)**
- 목록 제목이 날짜로; DatePicker 날짜 변경→그날 내용 로드; 빈 날짜 이동→빈 에디터(row 미생성)·첫 항목 추가 시 목록에 dated row 등장·**포커스 유지**; 비우기→오늘로 안 튐; 재클릭-deselect와 date-navigate 상호작용; 이전 날짜 편집 후 이동 시 flush로 보존; 주간 리포트 정상.
- 목표: 빌드 경고 0, 기존 356 + 신규 테스트 그린.

## 7. 스키마 / 마이그레이션

**변경 없음. TargetVersion 2 유지.** `notes.log_date`(+ `idx_notes_log_date`)는 이미 존재, `yyyy-MM-dd` 정규 저장. UNIQUE/partial-unique/ApplyV3 **금지**(기존 중복 DB 마이그레이션 실패·소프트삭제 날짜 재사용 차단·주간리포트 오염 위험). 중복 방지는 순수 앱 레이어(그룹 사이클 방지 선례와 동일).

## 8. 비목표 (후속)

1. **주간 리포트 기존 중복 dedup** — `GetChecklistsInWeek` 무변경. 신규 데이터는 날짜당 1개라 깨끗, **기존 중복은 수동 삭제 전까지 double-count**(릴리스 노트 명시).
2. **휴지통 복원 시 같은 날짜 활성본과 병합/거부** — 미구현. 복원 후 중복은 §3.7 접미사로 표면화만.
3. **일회성 중복 정리 UI**(배너 등) — 스코프 밖. §3.7로 최소 대응.
4. **`ResolveLiveTitle`/`NoteTitleResolver` 이중 규칙 통합** — 체크리스트는 live-title 경로 미사용이라 위험 없음. 별도 리팩토링.
5. **날짜 제목 편집/커스텀** — 날짜 고정.

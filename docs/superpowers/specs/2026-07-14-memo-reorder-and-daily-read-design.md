# 설계 명세 — 메모 리스트 드래그 순서변경 + 일일업무일지 Read 탭

작성일: 2026-07-14
브랜치: `feature/memo-reorder-daily-read`
기준 상태: master, 378 테스트 그린

## 배경 / 목표

두 가지 독립 기능을 추가한다.

1. **기능 1 — 메모 리스트(가운데 패널 `NoteListBox`) 드래그 순서변경.** 현재 첫 번째 트리(그룹 트리)만 드래그 순서변경이 되고, 두 번째 트리의 메모 리스트는 안 된다. 메모를 위/아래로 드래그해 수동 순서를 지정할 수 있게 한다.
2. **기능 2 — 일일업무일지 "Read"(양식 출력) 탭.** 현재 화면(Write)에 더해, 그날의 할일/이슈를 매일 별도로 관리하는 작업 파일에 붙여넣을 양식으로 렌더링해 보여주는 Read 탭을 추가한다. 업무일자 줄 오른쪽의 버튼으로 Write/Read를 전환한다.

## 확정된 결정 (사용자 승인)

- **기능 1 정렬 동작:** 하위호환. 드래그 전까지는 지금처럼 "최근 수정순". 그룹을 한 번 드래그하면 그때부터 그 그룹은 수동 순서를 기억한다.
- **기능 2 접두어:** 조합/치환/회사필드 로직 **없음**. 할일 접두어 = 그 할일에 지정된 **고객사 이름(Client.Name) 그대로**. (`SLD`, `SLD 자율형공장` 등은 각각 별개 고객사이며 이름 그대로 출력.) 미분류(ClientId=null 또는 미확인)는 접두어 생략.
- **기능 2 포함 범위:** 완료 여부와 무관하게 전체 할일 포함. 빈/공백 항목은 제외. 출력 직전 `FlushSaves()`로 최신 텍스트+자동태깅 반영.
- **기능 2 레이아웃:** 할일/이슈 **2개 박스 분리**(읽기전용이지만 드래그 선택·복사 가능) + 각 박스에 **복사 버튼**.
- **DB 스키마 변경 없음** (`notes.sort_order`, `checklist_items` 필드 모두 존재).

## 아키텍처 개요

### 기능 1 — 노트 순서

- 정렬 기준을 `pinned DESC, sort_order ASC, updated_at DESC, id DESC`로 변경. 기존 노트는 전부 `sort_order=0`이라 동률→`updated_at DESC` 폴백 = 기존 동작 유지. 드래그 시 해당 그룹의 노트를 0..n-1로 재번호.
- 순서 저장용 신규 메서드 `INoteRepository.SetSortOrder(int id, int sortOrder)` — `sort_order`만 UPDATE, `updated_at`은 절대 건드리지 않음(메타 조작; `ChecklistViewModel.Renumber` 원칙과 동일).
- `NoteListBox`에 `AllowDrop + DragOver + Drop` 추가. `noteId` 드래그를 리스트 내부에 드롭하면 순서변경, 그룹 노드에 드롭하면 기존 그룹 이동(그대로 유지). 드롭 위치 표시선은 기존 `DropIndicatorAdorner`를 **`NoteListBox` 전용 인스턴스**로 재사용(그룹 트리용 `_dropIndicator`와 분리; adorner는 대상 컨트롤의 AdornerLayer에 바인딩되므로 분리 필수).
- `MainViewModel.ReorderNote(int noteId, int newIndex)`: `Notes.Move` 후 전체 목록을 0..n-1로 재번호 + `SetSortOrder` 영속화. `LoadNotes` 호출 안 함(깜빡임 방지), `updated_at` 갱신 안 함.

### 기능 2 — Read 탭

- **순수 렌더러** `Memoria.Core.Reporting.DailyLogRenderer` (정적 클래스, `SheetWorkParser`와 동일한 정적 유틸 관례).
  - `RenderTasks(IReadOnlyList<(string Text, int? ClientId)> tasks, IReadOnlyDictionary<int,string> clientNames)`
  - `RenderIssues(IReadOnlyList<string> issues)`
  - 규칙: 리스트 순서 유지, `IsNullOrWhiteSpace(Text)`면 건너뜀(번호는 필터 후 1부터 연속), 텍스트는 그대로(트림 안 함), `\n` join, 마지막 개행 없음, 빈 섹션은 빈 문자열.
  - 할일 라인 = `"{n}. {name} {text}"`(name은 ClientId→Name, 없거나 미확인/공백이면 접두어 생략 `"{n}. {text}"`). 이슈 라인 = `"{n}. {text}"`.
- **ChecklistViewModel**: `IClipboardService` 주입(6번째 인자; DI는 자동 해석되어 `App.xaml.cs` 변경 불필요). `IsReadMode`/`TaskOutput`/`IssueOutput` 관찰 프로퍼티, `SetWriteMode`/`SetReadMode`/`CopyTaskOutput`/`CopyIssueOutput` 커맨드. `OnIsReadModeChanged(true)` → `BuildOutputs()`. `BuildOutputs`: `FlushSaves()` 먼저 → `_clients.GetAll()`(전체, 비활성 포함)로 id→Name 맵 → `Items`(TasksView/IssuesView 아님)를 Kind로 필터해 렌더.
  - 렌더러는 필드로 `new DailyLogRenderer()` 대신 정적 호출(`DailyLogRenderer.RenderTasks(...)`).
- **ChecklistView.xaml**: 업무일자 헤더 StackPanel→DockPanel. 오른쪽에 `Write`/`Read` 버튼(항상 표시, 기존 읽기/편집/마크다운 버튼 패턴과 동일). 가운데 영역을 Grid로 감싸 Write UI(ScrollViewer, `InverseBoolToVis`)와 Read UI(Grid, `BoolToVis`)를 겹쳐 두고 가시성 토글. Read UI는 할일/이슈 각각 헤더(라벨+복사 버튼) + 읽기전용 Consolas TextBox(`WeeklyReportView` 박스 스타일 미러, `IsReadOnly=True`로 선택·복사 유지).

## 구현 순서 (TDD)

1. `DailyLogRenderer` + 테스트(정적 렌더러). 표준 예시 바이트 일치 포함.
2. `NoteRepository.GetByGroup` ORDER BY에 `sort_order ASC` 추가 + 테스트.
3. `INoteRepository.SetSortOrder` + `NoteRepository` 구현(updated_at 불변) + 테스트.
4. 두 `FakeNoteRepository`(`Fakes/`, `App/Fakes/`)에 `SetSortOrder` 추가 + GetByGroup 정렬 미러(방어).
5. `MainViewModel.LoadNotes`에 `.ThenBy(SortOrder)` 추가 + 회귀 확인.
6. `MainViewModel.ReorderNote` + 테스트(이동/재번호/updated_at 불변/무효 인덱스 no-op).
7. `MainWindow` `NoteListBox` DragOver/Drop + 전용 DropIndicator(수동 검증).
8. `ChecklistViewModel` Read 모드/출력/복사 + 테스트.
9. 3개 테스트 호출부 6번째 인자 수정(`ChecklistViewModelTests`=FakeClipboardService, `MainViewModelEditorHostTests`+`M9EditorFakes`=FakeClipboard). `App.xaml.cs`는 변경 없음.
10. `ChecklistView.xaml` Write/Read 토글 + Read UI(빌드+수동 검증).

## 테스트 / 검증

- 신규 단위테스트: `DailyLogRenderer`(≈12 케이스), `NoteRepository.SetSortOrder`/`GetByGroup` 정렬, `MainViewModel.ReorderNote`, `ChecklistViewModel` Read 출력/복사.
- 회귀: `MainViewModelNotesTests`(정렬 동률 유지), 모든 체크리스트 호스팅 MainViewModel 테스트, `ChecklistViewModelTests`, WeeklyReport 렌더/VM, `GroupDropCalculatorTests`. 전체 378 → 그린 유지 + 신규.
- WPF 드래그 글루(코드비하인드 히트테스트)는 단위테스트 불가 → 앱 실행 수동 검증.
- 빌드/테스트: `"/mnt/c/Program Files/dotnet/dotnet.exe" test Memoria.sln`(Windows 인터롭).

## 리스크 / 완화

- **컴파일 파손(최상위):** 4개 ctor 호출부 중 3개 테스트 호출부에 6번째 인자 필요. 두 `FakeNoteRepository`에 `SetSortOrder` 필요. → 4·9단계에서 처리, 테스트 프로젝트 먼저 빌드.
- DropIndicatorAdorner는 대상 컨트롤 AdornerLayer에 바인딩 → `NoteListBox` 전용 인스턴스 사용(그룹 트리와 공유 금지).
- 토글은 bool→bool 역변환 컨버터가 없으므로 RadioButton 대신 커맨드 버튼 사용.
- `BuildOutputs`는 `Items`(정렬된 원본)를 사용(필터 뷰 아님).

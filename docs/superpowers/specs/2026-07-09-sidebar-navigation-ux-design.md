# 사이드바/탐색 UX 개선 (스펙 A) — 설계

- 날짜: 2026-07-09
- 상태: 승인 대기 → 구현 계획 전 검토
- 대상 버전: v0.5.0 (phase 2 UX 개선 묶음 A)
- 범위: ① 긴 제목/그룹명 말줄임 · ② 선택 항목 재클릭 → 선택 해제(토글) · ④ 탐색기식 드릴다운 · ⑤ 열 폭 드래그 조정 · ⑥ 사이드바 접기 토글
- 별도 스펙(후속): ③ 일일업무일지 날짜당-하나(다이어리) — 스펙 B로 분리

## 1. 배경 · 목표

사이드바(그룹 트리·시스템 목록)와 가운데 메모 패널의 탐색·표시 UX를 개선한다. 셋 다 같은 영역(그룹 트리 + 가운데 패널)을 건드리므로 한 스펙으로 묶는다. **기존 동작(왼쪽 그룹 트리로 그룹 선택 → 가운데에 그 그룹 직속 메모 표시)은 유지하고 추가·개선만** 한다.

## 2. 확정된 결정 (브레인스토밍)

- **①** 긴 제목/그룹명은 `…`로 말줄임하여 삭제 버튼이 밀리지 않게. **선언적 WPF**(TextTrimming + 가로 스크롤 비활성 + 행 stretch), 수동 측정 코드 없음.
- **②** 이미 선택된 항목을 다시 클릭하면 선택 해제(토글). 적용: 사용자 그룹 트리 · 시스템 그룹 목록 · 메모 목록. 그룹 해제 → 가운데 패널 비움, 메모 해제 → 우측 에디터 닫음.
- **④** 그룹을 열면 가운데 패널에 **하위 그룹(폴더) + 직속 메모**를 함께 보여주고, 하위 그룹을 클릭하면 그 안으로 드릴다운. 상위 이동은 **브레드크럼**. 왼쪽 그룹 트리는 그대로 유지.
- **⑤** 열 사이 세로 구분선을 드래그해 폭 조정(그룹 트리 · 가운데 패널 · 에디터 3열), 각 열 **최소 폭 이하로는 안 줄고** 나머지는 자유. **열 폭을 설정에 저장해 재시작 시 복원.**
- **⑥** 상단 툴바 **맨 왼쪽('새 메모' 왼쪽 첫 번째)**에 토글 버튼(☰)으로 **사이드바(그룹 트리 + 가운데 패널)를 접었다 펴기**. **접힘 상태를 설정에 저장해 재시작 시 복원.**

## 3. 상세 설계

### 3.1 ① 긴 텍스트 말줄임 (레이아웃, 선언적)
현재 메모 행은 Grid(0열=* 제목, 1열=Auto 🗑), `ListBoxItem.HorizontalContentAlignment=Stretch`라 240px 폭에 맞춰지지만 제목 TextBlock에 `TextTrimming`이 없어 0열이 늘어나 버튼을 밀어낸다.
- **메모 목록 제목 TextBlock**: `TextTrimming="CharacterEllipsis"` 추가(또는 `ListItemText` 스타일에 세터 추가). 0열=*로 폭이 제한되므로 자동으로 `…` 처리.
- **시스템 그룹 목록 TextBlock**(`ListItemText`)도 동일 적용.
- **그룹 트리**(`ListItemTextTree`)는 이미 `TextTrimming` 있음 → 유지. 다만 항목이 트리 폭을 넘겨 가로로 밀리지 않도록:
  - `NoteListBox`·`GroupTree`·`SystemListBox`에 `ScrollViewer.HorizontalScrollBarVisibility="Disabled"` 설정(가로 확장/스크롤 금지 → 남는 폭 안에서 말줄임).
  - TreeViewItem 헤더가 트리 폭까지 stretch 되도록 확인(기존 full-row 템플릿 유지, 필요 시 `HorizontalContentAlignment=Stretch`).
- 순수 XAML 변경. 코드 없음.

### 3.2 ② 선택 항목 재클릭 → 선택 해제(토글)
WPF의 `SelectedItemChanged`/`SelectionChanged`는 **선택이 바뀔 때만** 발화하므로 "이미 선택된 것 재클릭"은 잡히지 않는다. **항목 레벨 프리뷰 입력**으로 처리하되 **기존 드래그 임계값 로직을 깨지 않도록** 한다.
- **드래그와 공존(중요)**: 선택된 메모/그룹에서 드래그(메모→그룹 이동, 그룹 재부모)를 시작할 수 있어야 하므로, **누르는 즉시 해제하지 않고 "클릭(=드래그 임계값 미만으로 뗌)"일 때만 해제**한다.
  - `PreviewMouseLeftButtonDown`에서 대상 항목이 **이미 선택 상태였는지** 기록(`_wasSelectedOnDown`) + 시작점 기록(기존 `_dragStartPoint` 재사용).
  - `PreviewMouseLeftButtonUp`(또는 클릭 완료)에서 드래그 임계값을 넘지 않았고 `_wasSelectedOnDown`이면 **선택 해제**, `e.Handled=true`.
- **대상별 해제 동작**:
  - **메모 목록**: `SelectedNote = null` → 우측 에디터 닫힘(기존 `OnSelectedNoteChanged(null)` 경로 재사용).
  - **사용자 그룹 트리**: 해당 노드 `IsSelected=false` + `ViewModel.SelectedNode = null` → 가운데 패널 비움(`LoadNotes`가 `SelectedNode==null`이면 Clear).
  - **시스템 그룹 목록**: `SystemListBox.SelectedItem = null` + `SelectedNode = null`.
- `_syncingSelection` 가드와 상호작용 주의(순환 갱신 방지). 해제도 이 가드 안에서 수행.

### 3.3 ④ 탐색기식 드릴다운 (가운데 패널)
가운데 패널을 "현재 폴더의 내용"(하위 그룹 + 직속 메모) 뷰로 확장하고, 상단에 브레드크럼을 둔다.

- **탐색 상태 = 현재 폴더**: `SelectedNode`를 "현재 폴더"로 사용(추가 상태 없이 기존 플럼빙 재사용). 
  - 왼쪽 트리에서 그룹 클릭 → `SelectedNode = 그 그룹`.
  - 가운데 하위 그룹 클릭 → `SelectedNode = 그 하위 그룹`(브레드크럼 확장). **왼쪽 트리 선택도 동기화**(기존 `SyncSidebarSelection`: 선택 + 조상 펼침) → 위치가 양쪽에 일관되게 보임.
- **가운데 패널 항목(혼합)**: 하나의 목록에 두 종류 —
  - **폴더 행(하위 그룹)**: 현재 그룹의 직속 하위 그룹들. 위쪽에 폴더 아이콘(📁)과 함께 표시. 클릭 → 그 그룹으로 드릴다운(탐색). **삭제 버튼 없음**(그룹 관리는 기존대로 왼쪽 트리 컨텍스트 메뉴). 
  - **메모 행**: 현재 그룹의 직속 메모(기존과 동일, 🗑 삭제 버튼 유지). 클릭 → 에디터 열기.
  - 순서: 폴더들(이름 정렬/기존 SortOrder) → 메모들(기존 정렬: 고정 우선, 수정일 내림차순).
  - 구현: `MiddlePanelItems` ObservableCollection에 폴더/메모 항목을 섞어 담고, DataTemplateSelector(또는 `IsFolder` 플래그 + 두 DataTemplate)로 구분 렌더.
- **브레드크럼(상단 행)**: 루트→현재까지 경로(예: `업무 > 하위A > 하위B`). 각 조각 클릭 → 그 조상으로 이동. (미분류)·시스템 그룹은 단일 조각. 텍스트는 말줄임/줄바꿈 없이 가로 스크롤 또는 축약.
- **하위 그룹 없는 경우**: (미분류), 하위 없는 그룹, 시스템 그룹 → 폴더 행 없이 기존처럼 메모만. 즉 **하위 그룹이 있을 때만 폴더 행 추가**(비파괴).
- **직속 메모만**(재귀 집계 아님) — 기존 `GetByGroup(groupId)` 규칙 유지. 하위 그룹 메모는 그 폴더로 들어가야 보임(탐색기와 동일).

### 3.5 ⑤ 열 폭 드래그 조정 + 영속화
- 최상위 3열: **그룹 트리 | 가운데 패널 | 에디터**. 사이 두 곳에 `GridSplitter`(폭 ~5px) 추가.
  - 열 정의: `[그룹트리 Width=W0, MinWidth≈150][Splitter Auto][가운데 Width=W1, MinWidth≈150][Splitter Auto][에디터 Width=*, MinWidth≈200]`.
  - MinWidth 이하로는 못 줄임(GridSplitter가 자동 존중), 에디터가 남는 폭.
- **영속화**: 설정 키 `ui.col0Width`, `ui.col1Width`에 열 폭 저장. **창 닫힘(Closing) 시 저장, 시작(Loaded) 시 복원.**
- 순수 XAML(GridSplitter) + 얇은 코드비하인드(저장/복원).

### 3.6 ⑥ 사이드바 접기 토글 + 영속화
- 상단 툴바 **첫 항목**(‘새 메모’ 왼쪽)에 ☰ 토글 버튼.
- 접기: 그룹 트리 열(W0)·가운데 패널 열(W1)·두 GridSplitter를 **접어 폭 0**(에디터만). 펴기: 저장해 둔 W0/W1로 복원.
- **영속화**: 설정 키 `ui.sidebarCollapsed`(true/false). 시작 시 복원(접힘이면 접힌 상태로 시작).
- 상태 `IsSidebarCollapsed`(VM 또는 코드비하인드 bool) + 접힘 시 열 폭 0 / 펴짐 시 복원.

### 3.4 데이터/집계 영향 없음
- 그룹 계층은 이미 `SidebarNodeViewModel.Children`(메모리 트리) + `GetByGroup`로 확보. 새 리포지토리 메서드 불필요(직속 하위 그룹 = 현재 노드의 `Children` 중 Kind=Group, 직속 메모 = `GetByGroup`).
- 주간보고·검색 등 다른 기능 무영향.

## 4. 컴포넌트 · 인터페이스

| 컴포넌트 | 계층 | 변경 | 비고 |
|---|---|---|---|
| `MainWindow.xaml` (메모 템플릿/스타일) | App | ① TextTrimming + HorizontalScroll Disabled | 선언적 |
| `Base.xaml` (ListItemText 등) | App | ① 말줄임 세터 | |
| `MainWindow.xaml.cs` (선택 핸들러) | App | ② 재클릭 해제(다운/업 + 드래그 임계값) | 3 표면 |
| `MainViewModel` | App | ④ `MiddlePanelItems` 구성(폴더+메모), 현재 폴더 = SelectedNode, 브레드크럼 경로 | LoadNotes 확장/개명 |
| `FolderEntryViewModel` (신규) | App | ④ 가운데 폴더 행(그룹 id/이름/클릭=탐색) | |
| `BreadcrumbSegmentViewModel` (신규) | App | ④ 브레드크럼 조각(그룹/이름/클릭=이동) | |
| `MainWindow.xaml` (가운데 열) | App | ④ 브레드크럼 행 추가 + 혼합 목록 템플릿 | |

## 5. 오류/경계 처리
- 선택 해제 후 재선택, 드래그 중 해제 금지(임계값), `_syncingSelection` 순환 방지.
- 드릴다운 중 그룹 삭제/이동(기존 LoadGroups 경로) 시 현재 폴더가 사라지면 → 상위(또는 루트)로 폴백.
- 브레드크럼이 좁은 폭을 넘으면 앞쪽 축약(`… > 하위A > 하위B`).

## 6. 테스트 전략
- **자동(App VM, WSL→dotnet.exe)**:
  - ④ `MiddlePanelItems` 구성: 하위 그룹 있는 그룹 선택 → 폴더 행 + 메모 행 순서/개수, 하위 없는 그룹 → 메모만, 폴더 클릭 → SelectedNode 이동 + 목록 갱신, 브레드크럼 경로/조각 클릭 이동.
  - ② 해제 로직 중 VM 부분(SelectedNode=null → 목록 비움, SelectedNote=null → 에디터 닫힘)은 VM 테스트로. (입력 이벤트/드래그 임계값은 수동.)
- **수동(Windows GUI)**: ① 말줄임+버튼 유지(긴 제목/그룹명), ② 3표면 재클릭 해제 + 드래그 여전히 동작, ④ 드릴다운·브레드크럼·왼쪽 트리 동기화, 다크/라이트.
- 목표: 빌드 경고 0, 기존 344 + 신규 테스트 그린.

## 7. 비목표 (후속)
- ③ 일일업무일지 다이어리(스펙 B).
- 가운데 폴더 행에서의 그룹 CRUD/드래그 재부모(그룹 관리는 왼쪽 트리 유지).
- 재귀 메모 집계(하위 포함) — 탐색기식 드릴다운으로 대체.
- 좌우 분할 트리 뷰(가운데는 한 단계씩 드릴다운, 펼침 트리 아님).

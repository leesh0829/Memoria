# Memoria v0.2.0 — 그룹 중첩(폴더 트리) 설계

작성일: 2026-07-02 (적대적 리뷰 반영 v2)
대상 버전: v0.2.0 (2단계 첫 서브프로젝트)
상태: 설계(구현 전)

## 1. 개요 / 목적

사용자 그룹을 **폴더처럼 중첩**할 수 있게 한다(그룹 안에 하위 그룹). 사이드바의 평면 사용자 그룹 목록을 **트리(TreeView)** 로 바꾸고, 하위 그룹 생성·재부모(드래그)·펼침/접기를 지원한다.

핵심 결정(브레인스토밍 확정):
- **메모 집계 = 직속만(폴더 방식).** 그룹 선택 시 그 그룹에 **직접** 속한 메모만 표시. 기존 `GetByGroup(groupId)` 그대로(재귀 없음).
- **삭제 = 승격.** 하위 그룹을 가진 그룹을 삭제하면 자식 그룹은 삭제된 그룹의 **부모로 한 단계 승격**, 삭제된 그룹의 메모는 (미분류)로(기존 `ON DELETE SET NULL`).
- **드래그 재부모 포함.** 트리에서 그룹을 다른 그룹의 하위/형제/최상위로 이동.

## 2. 배경 / 현재 상태

- **스키마·모델에 중첩 컬럼이 이미 존재**: `groups.parent_id INTEGER REFERENCES groups(id)`(자기참조 FK), `Group.ParentId (int?)`. v0.1.x는 항상 `parent_id=null`로 생성/평면 렌더. **DB 마이그레이션 불필요.**
- **주의(리뷰 확인)**: `PRAGMA foreign_keys=ON`이라 dangling parent_id는 못 생기지만, **자기참조 FK는 자기자신(id==parent_id)·사이클(A→B→A)을 막지 못한다**(둘 다 "존재하는 id"라 FK 충족). 따라서 사이클/자기부모 방어는 애플리케이션 코드가 책임진다(→ §4.2).
- `groups.parent_id`엔 인덱스 없음. 그룹 수가 적어 무시(의도적 트레이드오프, 마이그레이션 안 함).
- 현재 사이드바: `Grid.Column=0` = [사용자 그룹 `GroupListBox`(평면, `SidebarNodes`)] + [가로 구분선] + [시스템 그룹 `SystemListBox`(`SystemNodes`, 하단 고정)]. 선택 동기화는 code-behind(`_syncingSelection`, `GroupListBox_SelectionChanged`/`SystemListBox_SelectionChanged`/`SyncSidebarSelection` — `ListBox.SelectedItem`이 쓰기 가능이라 성립).
- `SidebarNodeViewModel`: `Name`, `GroupId(int?)`, `Kind(Group|Unclassified|System)`.
- `GroupManagementViewModel`: `Groups`, `AddGroup/RenameGroup/SetGroupColor/DeleteGroup`, `MoveGroup(from,to)`(평면 인덱스), `MoveNoteToGroup`. 드래그(code-behind): `GroupList_PreviewMouseMove/Drop`(순서변경, `groupId`), `GroupNode_DropNote`(메모→그룹, `noteId`), 드래그 임계값+버튼 제외.

## 3. 범위

### 포함 (v0.2.0)
- 사용자 그룹 무제한 중첩 + **사이클/자기부모 방어**.
- 하위 그룹 생성(컨텍스트 메뉴 "새 하위 그룹").
- 드래그 재부모: 하위로/형제 순서/최상위로. 최상위 이동은 **컨텍스트 메뉴 "최상위로 이동"** 으로도 항상 가능(빈 영역 드롭에만 의존하지 않음).
- 펼침/접기 + **세션 내 상태 유지**(LoadGroups 재구성 시 스냅샷/복원).
- 삭제 시 자식 승격, 선택/펼침 상태 보존.
- 메모를 트리의 임의 그룹 노드로 드래그 이동(기존 유지).
- **애니메이션**(§4.6): 드래그 고스트/드롭 표시자, 펼침·접기, 선택/호버 페이드, 이동 fade-in.

### 제외 (다음 사이클)
- 재귀 메모 집계, 깊이 상한, 펼침 상태의 **재시작 간(cross-restart) 영속화**.
- 시스템 그룹·(미분류)의 중첩.
- 구글드라이브 엑셀, 리치 텍스트.

## 4. 상세 설계

### 4.1 도메인 규칙
- **부모 유효성(3중)**: 새 부모는 (a) 자기 자신이 아니어야 하고(`parentId != groupId`), (b) 자기 후손이 아니어야 하며(사이클 방지), (c) 시스템 그룹이 아니어야 한다. 위반 시 **no-op**.
- **깊이**: 무제한.
- **sort_order = 형제 범위(같은 parent_id) 내 0..n 연속.** 재부모/순서변경/삭제-승격 등 **형제 집합이 바뀌는 모든 연산은 영향 받은 목적지 형제 집합의 sort_order를 한 트랜잭션에서 0..n으로 재번호**한다. `GetAll()`의 `ORDER BY sort_order, id`가 형제 정렬의 진리원천.
- **삭제(승격)**: `Delete(id)`를 단일 트랜잭션으로 — (a) 자식 승격 `UPDATE groups SET parent_id=<deleted.parent_id> WHERE parent_id=id`, (b) 목적지(=조부모, 또는 루트) 형제 집합 sort_order 재번호, (c) 행 삭제. 메모는 FK `ON DELETE SET NULL`. 루트 그룹 삭제 시 `deleted.parent_id=null`이라 자식이 루트가 됨(정상). 시스템 그룹은 삭제 불가.

### 4.2 리포지토리 계약 (`IGroupRepository` 확장)
기존 유지: `Create/Update/Get/GetAll`. **변경/추가:**
- `void SetParent(int groupId, int? parentId)` — **리포지토리가 최종 방어(backstop)**. 같은 write-lock 안에서 `parentId==groupId`거나 `parentId`가 `groupId`의 후손이거나 시스템 그룹이면 **거부(no-op)**. 통과 시 부모 변경 + 목적지 형제 sort_order 재번호. (VM은 UX용 사전검증도 하되, 무결성 보장은 여기.)
- `Delete(int id)` — **승격 로직 포함**(§4.1). 단일 트랜잭션.
- 형제 순서: `void ReorderSiblings(int? parentId, IReadOnlyList<int> orderedGroupIds)` 신설 — 해당 부모의 형제들을 주어진 순서로 0..n 재번호. (재부모+위치 지정은 `SetParent` 후 `ReorderSiblings`로 조합, 또는 `MoveGroup(groupId,newParentId,index)` 헬퍼를 VM에 둔다.)
- 후손 판정 헬퍼(리포지토리 또는 Core): `bool IsDescendant(int candidateAncestorId, int nodeId)` — 부모 체인을 **방문집합 가드**로 따라가 사이클/자기 포함 안전.
- `GetAll()` 변경 없음. 트리는 VM에서 `parent_id`로 구성(평면→그룹핑, 재귀 아님 → 사이클 있어도 무한루프 없음; 부모 체인 순회 로직만 방문집합 가드).

### 4.3 뷰모델
- `SidebarNodeViewModel` 확장: `Children (ObservableCollection<SidebarNodeViewModel>)`, `IsExpanded (observable)`, **`IsSelected (observable)`** 추가. (TreeView는 `SelectedItem`이 읽기 전용이므로 선택/펼침을 **VM 플래그로** 구동한다.)
- `MainViewModel.LoadGroups()`:
  1. **재구성 전 스냅샷**: 펼쳐진 GroupId 집합 + 현재 선택(GroupId/Kind)을 저장.
  2. 사용자 그룹을 `parent_id`로 트리 구성 → `SidebarNodes` = **루트 노드들 + 마지막에 (미분류) 루트 노드**. 형제는 `sort_order` 정렬. (미분류)는 트리 루트로 두어 선택 표면을 **2개(TreeView + SystemListBox)** 로 유지.
  3. **재구성 후 복원**: 스냅샷의 GroupId로 `IsExpanded` 재적용, 선택 노드 `IsSelected=true`(+조상 펼침). 삭제로 이전 선택이 사라졌으면 **부모(있으면) 아니면 (미분류)** 선택.
- `GroupManagementViewModel`: `AddSubGroup(parentId, name)`, `MoveGroup(groupId, newParentId, siblingIndex)`(재부모+위치; `siblingIndex`는 **이동 노드를 목적지 형제에서 제거한 뒤의 삽입 위치**), 사이클 사전검증(`IsDescendant`). 삭제는 리포지토리 승격에 위임.

### 4.4 UI (MainWindow)
- **접근: `TreeView` + `HierarchicalDataTemplate`**(대안 들여쓰기 평면리스트는 확장/재부모 부자연 → 미채택).
- 구조: [사용자 그룹 **TreeView**(루트+하위, (미분류) 루트 포함)] + [가로 구분선] + [시스템 그룹 `SystemListBox` 고정]. Grid.Column=0 폭 220 고정.
- **TreeViewItem `ItemContainerStyle`**: `IsExpanded`·`IsSelected`를 VM에 **양방향 바인딩**(선택/펼침을 컨트롤이 아니라 VM으로 구동 — 프로그램적 선택/펼침 문제 해결).
- **컨텍스트 메뉴**: 기존(새 그룹/이름변경/색상/삭제) + **"새 하위 그룹"** + **"최상위로 이동"**(nested→root 항상 가능). 시스템/(미분류)에선 해당 항목 비활성.
- **드래그(3존 모델)**: 대상 행을 세 구역으로 —
  - 상단 ~25% → 대상의 **형제로 앞에** 삽입,
  - 중앙 ~50% → 대상의 **하위(child)** 로 재부모,
  - 하단 ~25% → 대상의 **형제로 뒤에** 삽입.
  드롭 대상은 **포인터 기반 TreeViewItem 히트테스트**(VisualTreeHelper)로 찾는다(기존 `list.Items` 평면 순회/`ResolveDropTargetNode`/평면 인덱스 경로는 폐기). 결과를 `(targetParentId, siblingIndex)`로 계산해 `MoveGroup` 호출. **target==source, 사이클, 시스템/(미분류) 대상 → no-op.**
  - **메모(noteId) 드롭**: 헤더 요소에 `Drop` 유지(그룹 재부모 Drop은 TreeView 레벨). `noteId 없으면 early-return→버블링` 계약 유지. 헤더 Border는 **행 전폭**으로 늘려 드롭 히트 안정화.
  - **드래그 피드백**: 무효 대상엔 **not-allowed 커서**. 접힌 부모 위에 hover 시 **스프링로드 자동 펼침**(중첩 기능 특성상 채택).
- **선택 동기화(2표면)**: `TreeView.SelectedItemChanged`로 사용자 클릭→`VM.SelectedNode` 반영, `SystemListBox`와 배타(한쪽 선택 시 다른 쪽 VM `IsSelected`/SelectedItem 해제). 프로그램적 선택은 VM `IsSelected`+조상 `IsExpanded`로. 선택 후 **`BringIntoView`/ScrollIntoView**로 화면에 노출.
- **테마(Base.xaml, 다크 대응)**: **TreeViewItem 풀 ControlTemplate** — 펼침 화살표 글리프를 `Brush.Foreground`로 다시 그림, **행 전폭 선택**(`Brush.ListItemSelected`/`ListItemHover`), `TreeView.Background=Transparent`. 그룹명 텍스트는 선택 시 전경색 전환을 위해 **`AncestorType=TreeViewItem` 기준 DataTrigger**(기존 `ListItemText`는 `AncestorType=ListBoxItem`이라 트리에서 안 먹음 → TreeView용 변형 추가). 깊은 중첩 오버플로 대비 그룹명 `TextTrimming=CharacterEllipsis`(+툴팁).

### 4.5 데이터 흐름 (예)
1. 그룹 A 우클릭 → "새 하위 그룹" → 이름 입력 → `AddSubGroup(A.Id,name)` → LoadGroups(스냅샷/복원) → A 펼침 + 새 노드 선택 + BringIntoView.
2. 그룹 B를 C의 중앙에 드롭 → 유효성(사이클/시스템) 확인 → `MoveGroup(B.Id, C.Id, endIndex)` → LoadGroups → 재선택/펼침 유지.
3. 그룹 B를 C의 상단 25%에 드롭 → C의 부모 아래, C 앞 형제로 삽입.
4. 그룹 삭제 → 자식 승격 + 목적지 형제 재번호 + 메모 (미분류) → LoadGroups → 부모(또는 미분류) 선택.

### 4.6 애니메이션 (신규 요구)
과하지 않고 짧게(대략 100~180ms), GPU 친화적 속성(Opacity/Transform/Color)만 사용. 성능·산만함 최소화.

- **A. 드래그 고스트(어도너)** — 드래그 중 대상 그룹의 **반투명 미리보기가 커서를 따라다님**. `AdornerLayer` + `GiveFeedback`/`DragOver`로 위치 갱신. (사용자 요청의 "드래그 이동 애니메이션" 핵심.)
- **B. 드롭 위치 표시자** — 3존에 따라 실시간(`DragOver`): 형제 앞/뒤 = 대상 행 위/아래 **삽입선**(강조색 ~2px), 하위(child) = 대상 행 **배경/테두리 강조**. 무효 대상이면 표시 안 함 + not-allowed 커서.
- **C. 펼침/접기 애니메이션** — 트리 노드 확장/축소 시 자식 영역 **Height+Opacity 슬라이드**(~150ms). TreeViewItem 템플릿의 자식 호스트에 트랜지션.
- **D. 선택/호버 배경 페이드** — 항목 배경색 전환에 짧은 `ColorAnimation`(~120ms). 트리·리스트 항목 공통(과하면 트리만).
- **E. 이동 반영(경량)** — 드롭 성공 후 재구성 시, 이동된 노트/그룹 노드를 **fade-in**(전체 재정렬 슬라이드는 full-rebuild 모델과 충돌하므로 v0.2.0 제외). Undo 토스트는 **slide-up + fade**.

구현 노트: WPF는 애니메이션 중 프레임을 자주 렌더하므로 durations는 짧게, `Storyboard`는 재사용/Freeze. 드래그 어도너는 별도 렌더 계층이라 성능 영향 적음. 접근성 위해 durations는 상수로 두어 후에 0으로 끌 수 있게(선택).

## 5. 오류 처리 / 안전
- 무효 드롭(사이클/자기/시스템/미분류/ target==source): 드롭 핸들러에서 parent/sibling 해석 **이전에** no-op 처리 + not-allowed 커서.
- 무결성 백스톱은 리포지토리 `SetParent`(§4.2). VM 사전검증은 UX용.
- 삭제 승격·재번호는 단일 트랜잭션(부분 적용 방지).
- 트리 구성은 순수 조회(평면 그룹핑), 부모 체인 순회는 방문집합 가드로 사이클 안전.

## 6. 테스트
- 리포지토리(`GroupRepository`): 하위 생성/조회, `SetParent` 정상, **자기부모 거부(id==parentId)**, **사이클 거부(후손을 부모로)**, **삭제-승격**(자식 parent_id→조부모; **루트 그룹 삭제 시 자식이 루트**; 메모 SET NULL), 형제 sort_order 재번호, `IsDescendant` 사이클/자기 안전.
- 뷰모델(`GroupManagementViewModel`/`MainViewModel`): 트리 구성(부모-자식/루트 집합, (미분류) 포함), 재부모+위치(siblingIndex 제거후 삽입), 새 하위 그룹, 삭제 후 선택/펼침 복원, 드롭 3존→(parentId,index) 매핑(순수 계산 함수로 분리해 단위테스트).
- 회귀: 기존 그룹/메모/사이드바 테스트 그린.
- 수동(Windows): 트리 펼침·접기 유지, 드래그 3존 재부모(사이클 no-op+커서), 최상위 이동 메뉴, 스프링로드, 라이트/다크 렌더·선택 가독성·깊은 중첩 트리밍, 메모 드롭.
- 수동(애니메이션): 드래그 고스트가 커서 추종, 드롭 표시자(삽입선/하위 강조) 정확, 펼침·접기 슬라이드, 선택/호버 페이드 자연스러움, 이동 fade-in. (애니메이션은 자동 단위테스트 대상 아님 — 로직/스토리보드 존재 정도만.)

## 7. 버전 / 브랜치
- feature 브랜치에서 구현 → master 병합 → **v0.2.0** 태그·릴리스(기존 release.yml 재사용).

## 8. 남은 세부(구현 계획에서 확정)
- 드롭 3존 임계값 정확한 비율(25/50/25 기준값, 조정 가능).
- `MoveGroup` vs `SetParent`+`ReorderSiblings` 조합 중 최종 API 형태(계약서에 확정).
- 스프링로드 hover 지연 시간(ms).
- TreeViewItem 템플릿에서 가상화(Virtualization) on/off — 소규모라 off로 단순화 가능.

# Memoria — 설계 문서 (Design Spec)

- **문서 버전**: v1.1 (적대적 검토 5렌즈 반영)
- **작성일**: 2026-06-26
- **대상 버전**: Memoria v1 (MVP)
- **작성자**: 이승현 (manufacturing-dx@hygino.co.kr)
- **상태**: 사용자 검토 대기

> v1.1 변경 요약: 메모 삭제/휴지통, 그룹 CRUD, 자동저장 크래시 복구, 스키마 마이그레이션, 안전한 WAL 백업(VACUUM INTO), DB 위치 `%LOCALAPPDATA%` 이전, 전문검색(FTS5), 양식 B 미분류 처리, 분류 캐시/수동보호, 전역 단축키 message-only 창, named pipe 단일 인스턴스, Windows 툴체인 빌드 경로 확정. 상세 이력은 §13.

---

## 1. 개요 (Overview)

### 1.1 한 줄 정의
> 저장하지 않아도 사라지지 않는 **빠르게 켜는 메모장**에, **그룹/날짜 정리**와 **매일 체크리스트 → 금요일 주간보고 자동 생성**까지 더한 네이티브 Windows 데스크톱 앱.

### 1.2 배경 / 문제 정의
사용자는 Windows 11 기본 메모장(Notepad)을 "임시 메모장"으로 애용한다. 새 메모를 `Win+R → notepad → Enter`로 즉시 띄우고, **파일로 저장하지 않은 채** 내용을 유지한다(Win11 Notepad는 저장하지 않은 탭을 재부팅 후 자동 복원해 준다). 그러나 다음 문제가 누적되었다:

1. **창이 너무 많다** — 저장 안 한 메모장이 늘어나면서 `Alt+Tab` 전환과 관리가 어렵다.
2. **분류가 안 된다** — 메모가 어떤 큰 분류(프로젝트/고객사)에 속하는지 묶을 방법이 없다.
3. **날짜 추적이 안 된다** — 언제 만들고 언제 고쳤는지 알 수 없어 타임라인 정리가 어렵다.
4. **반복 업무(주간보고)** — 매주 금요일 주간보고를 양식 2종에 맞춰 수기로 작성해야 한다.

### 1.3 핵심 가치 (Design Principles)
- **메모장의 가벼움/즉시성 유지** — 빠른 실행, 자동 저장, 플레인 텍스트 우선.
- **저장 행위 제거** — 사용자는 "저장"을 의식하지 않는다. 모든 것은 자동 영속화된다.
- **정리는 앱이 대신한다** — 그룹, 날짜, 검색, 주간보고 자동 생성.
- **데이터 보존(현실적 무손실)** — 정상 동작에서 사용자 데이터는 사라지지 않는다. 단, 비정상 종료(전원 차단/강제 종료) 시 마지막 자동저장 이후 ~수백 ms 창의 미커밋 입력은 손실될 수 있으며, 이를 최소화하기 위한 크래시 복구 저널을 둔다(§8). "절대 무손실"이라고 과장하지 않는다.

### 1.4 v1 범위에서 제외 (Out of Scope — 2단계 이후)
- 구글 드라이브 엑셀 일일기록 참조/연동 (OAuth)
- 리치 텍스트(서식), 이미지 첨부
- 클라우드 동기화 / 멀티 디바이스 / 멀티 사용자
- 그룹 다단계 중첩(트리). v1은 **평면 그룹**이며, `parent_id` 컬럼은 향후 확장을 위해 예약만 한다.
- 모바일

---

## 2. 기능 요구사항 (확정)

| # | 요구사항 | 충족 기능 |
|---|---|---|
| R1 | 메모 작성 (메모장 기반) | 일반(plain) 메모 에디터 |
| R2 | 파일 저장 불필요 | 디바운스 자동 저장 (SQLite) + 크래시 복구 저널 |
| R3 | 재부팅 후에도 유지 | `%LOCALAPPDATA%\Memoria\memoria.db` (WAL) |
| R4 | 새 메모 즉시 생성 | 전역 단축키 `Ctrl+Alt+N` + 트레이 상주 + 자동시작 |
| R5 | 최초 생성일 / 최근 수정일 표시 | 에디터 헤더에 `created_at` / `updated_at` |
| R6 | 그룹(분류) 기능 | 사이드바 그룹 트리 + 그룹 CRUD |
| R7 | 주간보고 자동 작성 (양식 2종) | 주간보고 엔진 (양식 A/B) |
| R8 | 매일 할일/이슈 + 체크리스트(체크 시 취소선) | 체크리스트(=일일 업무일지) 메모 유형 |
| R9 | 전체 색상(테마) 변경 | 라이트/다크/시스템 모드 + 강조색 + 프리셋 팔레트 |

---

## 3. 기술 스택 / 아키텍처

### 3.1 스택
- **언어/런타임**: C#, **.NET 9**
  - `Memoria.Core` → **`net9.0`** (윈도우 비의존, 어느 SDK로든 테스트 가능)
  - `Memoria.App` → **`net9.0-windows`** + `<UseWPF>true</UseWPF>`
- **패턴**: MVVM (`CommunityToolkit.Mvvm`)
- **데이터**: **SQLite** (`Microsoft.Data.Sqlite` + `Dapper`), **WAL 모드**, **FTS5 전문검색**(번들 `e_sqlite3`에 FTS5 포함)
- **트레이 아이콘**: **`H.NotifyIcon.Wpf`** (구 `Hardcodet.NotifyIcon.Wpf`의 .NET 5+/단일파일 친화 포크)
- **전역 단축키**: Win32 `RegisterHotKey`/`UnregisterHotKey` (`MOD_NOREPEAT` 포함) — **앱 수명 내내 유지되는 message-only 창(HWND_MESSAGE)** 에 후킹 (§8)
- **자동시작**: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 레지스트리 값
- **단일 인스턴스**: 명명된 `Mutex` + **named pipe IPC**(HWND 불필요)로 단축키/새메모 신호 전달, 포그라운드 전환은 `AllowSetForegroundWindow` + `SetForegroundWindow`
- **테스트**: `xUnit` + `FluentAssertions`

### 3.2 프로젝트 구조 (3 프로젝트)
```
Memoria.sln
├─ src/
│  ├─ Memoria.Core/        # 도메인 모델 + 서비스 + 주간보고 엔진 + Repository (UI 비의존, net9.0)
│  └─ Memoria.App/         # WPF UI (net9.0-windows): View/ViewModel, 트레이, 단축키, 자동시작, 테마
└─ tests/
   └─ Memoria.Tests/       # xUnit (Memoria.Core 대상)
```
**근거**: "틀리면 치명적인" 순수 로직(주간보고 엔진·분류·주차계산·Repository)을 UI에서 분리해 TDD로 검증한다.

### 3.3 의존 방향
```
Memoria.App (WPF/Win32 통합) ──► Memoria.Core (모델/서비스/엔진/Repository) ──► SQLite
```
- `Memoria.Core`는 WPF/Win32에 의존하지 않는다.
- 자동시작·단축키·트레이·테마 등 Windows 통합 코드는 `Memoria.App`에만 둔다.

---

## 4. 화면 구조 (UI)

### 4.1 메인 윈도우 (단일 창 + 사이드바)
```
┌───────────────────────────────────────────────────────────┐
│  [+ 새 메모] [+ 체크리스트] [📋 주간보고] [🔍 검색...]      │  ← 상단 툴바
├──────────────┬────────────────────────────────────────────┤
│ ▾ 📁 업무     │  제목: SLD 자율형공장 정리           [🗑 삭제]│
│   • 메모 A    │  생성 2026-06-22 14:03 · 수정 2026-06-26 …  │  ← 에디터 헤더 (R5)
│   • 메모 B    │  그룹 [업무 ▾]   유형 [일반]                │
│ ▾ 📁 개인     │ ─────────────────────────────────────────── │
│   • 메모 C    │                                            │
│ ▾ 📁 (미분류) │   (유형별 본문 영역)                       │
│ ▾ 🔒 일일일지 │                                            │
│ ▾ 🔒 주간보고 │                                            │
│ [🗑 휴지통]    │                                            │
│ [⚙ 설정]      │                                            │
└──────────────┴────────────────────────────────────────────┘
```
- **좌측 사이드바**: 그룹 트리(평면) → 그룹 선택 시 해당 그룹 메모 목록. 기본 정렬: **고정(pinned) 우선 → 최근 수정일 내림차순**. 일일일지/주간보고 시스템 그룹은 `log_date`/`report_week_start` 내림차순.
- **(미분류) 가상 노드**: `group_id = NULL` 메모를 모아 보여주는 사이드바 가상 노드(실제 그룹 행 아님).
- **시스템 그룹 2개**: `🔒 일일업무일지`, `🔒 주간보고` — 시드로 자동 생성, **삭제/이름변경 불가**. 신규 checklist/weekly_report 메모는 기본적으로 여기에 배치(다른 그룹으로 이동 가능).
- **우측 에디터 헤더**: 제목 · **최초 생성일 · 최근 수정일** · 그룹 선택 · 유형 표시 · 삭제 버튼.
- **검색**(§4.4), **휴지통**(§4.5), **그룹 CRUD**(§4.3) 진입점.
- **첫 실행 빈 상태**: 메모가 하나도 없으면 본문 영역에 안내("Ctrl+Alt+N 또는 [+ 새 메모]로 시작하세요") 표시.

### 4.2 트레이 / 단축키 동작
- 트레이 좌클릭: 메인 창 표시/숨김 토글.
- 트레이 우클릭 메뉴: `새 메모(Ctrl+Alt+N)`, `열기`, `설정`, `종료`.
- 창 닫기(X): 종료가 아니라 **창 Hide(HWND 유지)** 로 트레이에 숨김(설정에서 "닫으면 종료"로 변경 가능). HWND를 파괴하지 않는다.
- 전역 단축키 `Ctrl+Alt+N`: 어디서든 새 일반 메모 생성 → 메인 창 표시/포그라운드 → 새 메모 포커스. 이미 실행 중이면 기존 인스턴스가 처리(§8).

### 4.3 그룹 CRUD (R6)
- 사이드바에서 그룹 **추가 / 이름변경 / 색상지정 / 순서변경(드래그) / 삭제**.
- **그룹 삭제 정책**: `notes.group_id` 는 `ON DELETE SET NULL` — 그룹을 지우면 그 안의 메모는 삭제되지 않고 **(미분류)** 로 이동. (메모 손실 방지)
- 시스템 그룹(일일업무일지/주간보고)은 삭제·이름변경 불가.

### 4.4 검색
- 상단 검색창은 **`notes.title` + `notes.body` + `checklist_items.text`** 를 대상으로 한다(체크리스트 항목도 검색됨).
- 구현: **SQLite FTS5** 가상 테이블 + 동기화 트리거(§7). 결과는 목록으로 표시하고 클릭 시 해당 메모로 이동.

### 4.5 휴지통 (소프트 삭제)
- 메모 삭제는 **소프트 삭제**: `notes.deleted_at` 설정 → 사이드바에서 숨김, **휴지통**에서 확인.
- 휴지통에서 **복원** 또는 **영구삭제**(이때 `checklist_items` CASCADE 삭제). 삭제 직후 토스트에 **실행취소(Undo)** 제공.
- 보존기간 경과(기본 30일) 시 자동 영구삭제(설정 `trash.retentionDays`).

### 4.6 테마 (R9)
- **모드(`theme.mode`)**: `light` / `dark` / `system`.
- **강조색(`theme.accent`)**: 프리셋 + 커스텀 색상 선택기.
- **프리셋 팔레트(`theme.preset`)**: 배경/전경까지 바꾸는 팔레트(예: `default`, `dark`, `sepia`, `solarized`). 프리셋은 mode/accent만으로 표현되지 않으므로 **별도 키로 저장**하며, 프리셋이 mode/accent와 함께 최종 색을 결정한다.
- **시스템 모드 구현**: 레지스트리 `HKCU\...\Themes\Personalize\AppsUseLightTheme` 읽기 + 런타임 변경 감지(`WM_SETTINGCHANGE` 후킹 또는 `SystemEvents.UserPreferenceChanged`) → message-only 창과 연계.
- **구현 규약**: 모든 색/브러시는 **`DynamicResource`만** 사용(StaticResource 금지). 전환은 최상위 `MergedDictionaries`의 테마 사전 1개만 교체해 깜빡임 최소화. 서드파티 컨트롤(트레이 컨텍스트 메뉴 등)도 테마 키 적용.

---

## 5. 메모 유형 (3종)

각 메모는 `type` 필드를 가진다: `plain` | `checklist` | `weekly_report`.

### 5.1 일반 (plain)
- 메모장과 동일한 **플레인 텍스트** 에디터. 입력 즉시 디바운스 자동 저장.
- **제목 규칙**: `title`이 비어 있으면 `body` 첫 줄을 **표시용 제목**으로 사용한다(컬럼에는 저장하지 않음). 사용자가 제목을 명시 입력하면 `title`에 저장.

### 5.2 체크리스트 = 일일 업무일지 (checklist)
- 한 메모가 하루치 업무일지에 대응. `log_date`(기본=생성일, 편집 가능). **기본 제목** = `log_date`.
- 두 종류 항목(`checklist_items.kind`):
  - **할 일 (task)**: 체크박스 + 텍스트. 체크 시 **취소선** + `done_at` 기록.
  - **이슈 (issue)**: 체크박스 없는 텍스트 항목.
- **고객사 자동 태깅은 `kind=task` 에만 적용**한다. `issue`의 `client_id`는 항상 NULL이며 양식 B 이슈 섹션에 고객사 무관하게 나열된다.
- task 항목은 자동 분류 결과를 드롭다운으로 **수동 교정** 가능(교정 시 `is_manual=1`로 보호 — §6.3).
- 신규 checklist 메모는 시스템 그룹 `일일업무일지`에 배치.

### 5.3 주간보고 (weekly_report)
- 특정 주(월~금)의 체크리스트 항목을 모아 **양식 A 또는 B로 자동 생성**(§6).
- `report_format`(A|B), `report_week_start`(그 주 월요일). **기본 제목** = `MM/DD~MM/DD`.
- 생성 결과는 편집 가능한 텍스트(`body`)로 보관 + **클립보드 복사 버튼**.
- 주차 선택 UI + 양식 A/B 토글 + "다시 생성" 버튼.
- **멱등/재생성 정책**: `report_week_start`+`report_format` 조합당 1개의 메모를 재사용(중복 누적 방지). "다시 생성" 시 기존 `body`(사용자 편집분)를 **덮어쓰기 전에 확인 다이얼로그**를 띄운다.
- 신규 weekly_report 메모는 시스템 그룹 `주간보고`에 배치.

---

## 6. 주간보고 엔진 (핵심 로직)

### 6.1 입력
선택한 주의 **월요일~금요일** 범위에 `log_date`가 포함되는, 삭제되지 않은(`deleted_at IS NULL`) 체크리스트 메모의 항목들.
- `kind=task` → 업무 내용, `kind=issue` → 이슈.
- **`includeDoneOnly`(기본 OFF)**: ON이면 `kind=task` 중 `done=1`만 포함. **issue는 이 옵션과 무관하게 항상 전부 포함**.

### 6.2 출력 양식

#### 양식 A — 할일/이슈 순
```
[업무 내용]
	* {task 1}
	* {task 2}

[이슈]
	* {issue 1}
	* {issue 2}
```
- `[업무 내용]` 블록과 `[이슈]` 머리글 사이에 **빈 줄 1개**(사용자 실제 예시 기준 — 확정).
- 머리글 기본값 `[업무 내용]`(`report.formatA.taskHeader`) / `[이슈]`(`report.formatA.issueHeader`) — 설정 변경 가능.
- 들여쓰기 = 탭 1개(`report.indent`), 글머리 = `* `.

#### 양식 B — 고객사별 분류 + 제목줄
```
[ 이승현 주간 보고 (06/23 ~ 06/27) ]:

[ SLD ]
	* {SLD 업무}

[ MTP ]

[ 코모텍 ]
	* {코모텍 업무}

[ 충북테크놀로지파크 ]

[ 자율형 공장 ]
	* {자율형공장 업무}

[ 카본센스 ]

[ 미분류 ]
	* {자동분류 실패 & 수동지정 안 한 업무 — 존재할 때만 출력}

* 이슈사항:
	* {이슈 1 (고객사 무관)}
	* {이슈 2}
```
- **제목 줄**: `[ {이름} 주간 보고 (MM/DD ~ MM/DD) ]:`
  - `{이름}` 기본값 **"이승현"**(`report.reporterName`).
  - 날짜는 **0 포함 2자리**(`06/23`), 구분자 ` ~ `, 끝에 콜론 `:`. 📢 아이콘은 **출력하지 않음**.
- **고객사 섹션**: 활성(`enabled=1`) 고객사를 `clients.sort_order` 순서대로 출력. 항목이 없어도 **빈 섹션 머리글 출력**.
- **`[ 미분류 ]` 섹션**: 미분류 task가 **1개 이상일 때만** 고객사 섹션 마지막(이슈 섹션 직전)에 출력. 미분류가 있으면 **생성 다이얼로그에 경고 배너** 표시(조용한 누락 금지). 0개면 섹션 생략.
- **이슈 섹션**: `* 이슈사항:`(`report.formatB.issueHeader`) 줄 뒤에 이슈 나열.
- 들여쓰기/글머리: 양식 A와 동일(탭 + `* `).
- 양식 B 고정 문구(`주간 보고`)도 `report.formatB.titleWord` 키로 변경 가능(기본 "주간 보고").

### 6.3 고객사 자동 분류 규칙

**규칙은 우선순위(`client_rules.priority`) 오름차순으로 평가하며, 첫 매칭 적용.** (task 텍스트에 키워드 "포함" 여부, 대소문자 무시. **비활성(`enabled=0`) 고객사의 규칙은 무시**.)

| 우선순위 | 키워드(포함 시) | → 고객사 |
|---|---|---|
| 1 | `자율형공장`, `자율형 공장` | 자율형 공장 |
| 2 | `충북`, `충북테크놀로지파크`, `DL정보기술` | 충북테크놀로지파크 |
| 3 | `코모텍` | 코모텍 |
| 4 | `MTP`, `머티리얼즈파크` | MTP |
| 5 | `카본센스` | 카본센스 |
| 6 | `SLD` | SLD |
| 7 | (위 모두 미해당) | **미분류 (client_id=NULL)** |

**확정 규칙(사용자 확인)**: task에 `자율형공장` 키워드가 있으면 → **자율형 공장**(SLD보다 우선). `SLD`만 있고 `자율형공장`이 없으면 → **SLD**.

**분류 결과의 영속화/재계산 정책(확정)**:
- 자동 분류는 **task 저장 시 `client_id`에 캐시**한다(빠른 표시·검색용).
- 사용자가 수동 교정하면 `is_manual=1`로 표시하고 **이후 자동 재분류로 덮어쓰지 않는다**.
- **주간보고 생성 시**, `is_manual=0`(auto)인 항목은 **현재 규칙으로 재계산**하여 최신 분류를 반영한다(규칙/고객사가 바뀌어도 stale 방지). `is_manual=1` 항목은 그대로 사용.

**고객사 목록 관리**:
- 기본 활성 고객사(표시순 `sort_order`): `SLD`, `MTP`, `코모텍`, `충북테크놀로지파크`, `자율형 공장`, `카본센스`.
- **두 순서는 독립**: 사이드바/양식 B **표시순 = `clients.sort_order`**, **분류 매칭순 = `client_rules.priority`**. "고객사 순서변경" UI는 표시순만 바꾸며 매칭 우선순위에 영향 없음(매칭 우선순위는 키워드 편집 화면에서만 조정).
- 설정에서 고객사 추가/삭제/순서변경/키워드 편집 가능. **고객사 삭제는 비활성화(`enabled=0`) 권장**이며, 하드 삭제 시 `checklist_items.client_id`는 `ON DELETE SET NULL`로 해당 항목을 미분류 처리.

### 6.4 주차 계산
- **월요일 시작, 금요일 종료.** 임의 날짜 → 그 주의 월/금 계산. 기본값 = 오늘이 포함된 주(금요일 퇴근 시 그 주 생성 시나리오).
- 연말/연초/주 경계 케이스는 단위테스트로 검증(§9).

---

## 7. 데이터 모델 (SQLite)

> **스키마 버전 = 1** (`PRAGMA user_version = 1`). 시작 시 `_migrations` 러너가 현재→목표 버전을 순차 적용(§7.4).

```sql
-- 7.1 핵심 테이블
CREATE TABLE groups (
  id          INTEGER PRIMARY KEY,
  name        TEXT NOT NULL,
  parent_id   INTEGER REFERENCES groups(id),   -- v1 미사용(향후 중첩용 예약)
  is_system   INTEGER NOT NULL DEFAULT 0,       -- 1 = 일일업무일지/주간보고(삭제·개명 불가)
  sort_order  INTEGER NOT NULL DEFAULT 0,
  color       TEXT,
  created_at  TEXT NOT NULL                      -- ISO-8601
);

CREATE TABLE notes (
  id                 INTEGER PRIMARY KEY,
  group_id           INTEGER REFERENCES groups(id) ON DELETE SET NULL,
  type               TEXT NOT NULL,              -- 'plain' | 'checklist' | 'weekly_report'
  title              TEXT,
  body               TEXT,                       -- plain 본문 / weekly_report 생성결과
  log_date           TEXT,                       -- checklist 전용 (YYYY-MM-DD)
  report_format      TEXT,                       -- weekly_report 전용 ('A'|'B')
  report_week_start  TEXT,                       -- weekly_report 전용 (YYYY-MM-DD, 월요일)
  pinned             INTEGER NOT NULL DEFAULT 0,
  sort_order         INTEGER NOT NULL DEFAULT 0,
  deleted_at         TEXT,                       -- 소프트 삭제(휴지통). NULL = 활성
  created_at         TEXT NOT NULL,
  updated_at         TEXT NOT NULL
);

CREATE TABLE checklist_items (
  id          INTEGER PRIMARY KEY,
  note_id     INTEGER NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
  kind        TEXT NOT NULL,                     -- 'task' | 'issue'
  text        TEXT NOT NULL,
  done        INTEGER NOT NULL DEFAULT 0,
  done_at     TEXT,
  client_id   INTEGER REFERENCES clients(id) ON DELETE SET NULL,  -- NULL = 미분류
  is_manual   INTEGER NOT NULL DEFAULT 0,        -- 1 = 수동 교정됨(자동 재분류 제외)
  sort_order  INTEGER NOT NULL DEFAULT 0,
  created_at  TEXT NOT NULL,
  updated_at  TEXT NOT NULL
);

CREATE TABLE clients (
  id             INTEGER PRIMARY KEY,
  name           TEXT NOT NULL,                  -- 내부 식별/표시 라벨(예: 'SLD'). 양식 B는 '[ ' + name + ' ]'로 렌더(대괄호·공백은 렌더러가 추가)
  sort_order     INTEGER NOT NULL,               -- 표시순(양식 B 섹션 순서)
  enabled        INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE client_rules (
  id         INTEGER PRIMARY KEY,
  client_id  INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
  keyword    TEXT NOT NULL,
  priority   INTEGER NOT NULL                    -- 전역 매칭 우선순위(작을수록 먼저)
);

CREATE TABLE settings (
  key    TEXT PRIMARY KEY,
  value  TEXT NOT NULL
);

-- 7.2 성능 인덱스
CREATE INDEX idx_notes_group_id     ON notes(group_id);
CREATE INDEX idx_notes_log_date     ON notes(log_date);
CREATE INDEX idx_notes_deleted_at   ON notes(deleted_at);
CREATE INDEX idx_notes_week         ON notes(report_week_start, report_format);
CREATE INDEX idx_items_note_id      ON checklist_items(note_id);
CREATE INDEX idx_items_client_id    ON checklist_items(client_id);

-- 7.3 전문검색 (FTS5) — 트리거로 동기화
CREATE VIRTUAL TABLE notes_fts USING fts5(title, body, items, content='');
-- notes(title/body) 및 해당 note의 checklist_items.text(공백결합)를 items 컬럼에 색인.
-- INSERT/UPDATE/DELETE 트리거로 notes / checklist_items 변경을 notes_fts에 반영.
```

> `clients.display_label` 컬럼은 제거(검토 반영): `name` 하나로 통합하고 대괄호/공백은 렌더러가 부여한다. 양식 B 출력은 `[ {name} ]`.

**7.4 마이그레이션**
- `_migrations(version INTEGER PRIMARY KEY, applied_at TEXT)` + `PRAGMA user_version`.
- 시작 시 `user_version` < 코드의 목표 버전이면 버전별 업그레이드 스크립트를 트랜잭션으로 순차 적용. 초기 스키마 = 버전 1.

**7.5 시드 데이터(첫 실행)**
- `clients` + `client_rules`: §6.3 표 그대로 INSERT.
- 시스템 그룹 2개: `일일업무일지`, `주간보고` (`is_system=1`).
- 일반 그룹은 시드하지 않음(사용자가 생성). 그룹 없는 메모는 (미분류) 가상 노드로 표시.
- `settings` 기본값(§7.6) 채움.

**7.6 settings 기본값(주요 키)**

| key | 기본값 |
|---|---|
| `theme.mode` | `system` |
| `theme.preset` | `default` |
| `theme.accent` | (시스템 강조색 또는 고정 기본) |
| `report.reporterName` | `이승현` |
| `report.formatA.taskHeader` | `[업무 내용]` |
| `report.formatA.issueHeader` | `[이슈]` |
| `report.formatB.titleWord` | `주간 보고` |
| `report.formatB.issueHeader` | `* 이슈사항:` |
| `report.indent` | `\t` (탭) |
| `report.includeDoneOnly` | `false` |
| `hotkey.newNote` | `Ctrl+Alt+N` |
| `app.autostart` | `true` |
| `app.closeToTray` | `true` |
| `backup.retentionCount` | `7` |
| `trash.retentionDays` | `30` |
| `autosave.debounceMs` | `500` |

**7.7 영속화 정책**
- 모든 본문/항목 변경은 입력 멈춤 후 **~500ms 디바운스 자동 저장**(`autosave.debounceMs`). 창 종료/숨김/`SessionEnding` 시 즉시 flush.
- SQLite **WAL 모드** + `PRAGMA busy_timeout=5000`. **쓰기는 단일 직렬 라이터(전용 연결/큐)로 통일**해 `SQLITE_BUSY` 회피. 종료 시 `wal_checkpoint`.
- **자동 백업(안전)**: 하루 1회 `SqliteConnection.BackupDatabase`(Online Backup API) 또는 `VACUUM INTO 'backups/memoria-YYYYMMDD.db'`로 일관 스냅샷 생성(단순 파일복사 금지). `backup.retentionCount`(기본 7) 개 유지.
- **`updated_at` 갱신 규칙(확정)**: 사용자 **콘텐츠** 변경(`title`, `body`, `checklist_items` 추가/편집/삭제/체크)에만 갱신. 그룹 이동·pin·sort_order 등 **메타 조작은 갱신하지 않음**.

---

## 8. 비기능 / 안전성 / Windows 통합

### 8.1 데이터 보존 / 복구
- **DB 위치**: `%LOCALAPPDATA%\Memoria\`(로밍 비대상, 로컬 디스크). Roaming(`%APPDATA%`)·네트워크 경로는 WAL 공유메모리(-shm)와 호환되지 않으므로 사용하지 않는다. 경로가 네트워크로 감지되면 경고.
- **크래시 복구 저널**: 편집 중인 미저장 본문/항목을 `%LOCALAPPDATA%\Memoria\recovery\{noteId}.json`에 디바운스보다 빠르게 append. 정상 저장 성공 시 해당 recovery 파일 삭제.
- **DB write 실패**: 사용자 알림 + recovery 파일로 현재 편집분 보존 + 재시도. 복구 성공 시 recovery → DB 머지/복원 후 정리.
- **시작 시 무결성 점검**(`PRAGMA integrity_check`). 손상 감지 시: 손상 파일 `.corrupt`로 격리 → **최근 정상 백업 자동 복원 시도 + 사용자 확인 다이얼로그**.
- **현실적 무손실 고지**: 디바운스 창(최대 ~500ms) 이내 미커밋 입력은 비정상 종료 시 손실 가능(§1.3). recovery 저널로 최소화.

### 8.2 전역 단축키
- 앱 수명 내내 유지되는 **message-only 창(HWND_MESSAGE)** 에 `RegisterHotKey`(+`MOD_NOREPEAT`) 및 `WM_HOTKEY`(0x0312) 후킹. 메인 창 표시/숨김과 분리(창을 닫아도 단축키 유지).
- 등록 실패(이미 점유): 알림 후 설정에서 다른 조합으로 변경. `Ctrl+Alt` 조합의 AltGr 충돌 가능성은 `MOD_NOREPEAT` + 재설정 흐름으로 완화.

### 8.3 단일 인스턴스 + 포그라운드
- 명명된 `Mutex` 획득 직후 **named pipe 서버** 시작. 두 번째 인스턴스는 pipe로 "새 메모/열기" 명령 전달 후 종료(연결 실패 시 짧게 재시도; Mutex/pipe 경쟁 조건 처리).
- 두 번째 인스턴스가 종료 전 `AllowSetForegroundWindow(ASFW_ANY)` 호출 → 첫 인스턴스가 `SetForegroundWindow`로 창을 앞으로(작업표시줄 깜빡임 방지).

### 8.4 시작 성능
- 트레이 상주 + message-only 창 상시 유지로 단축키→새 메모 표시 지연 최소화. 단일파일 publish에서 **`EnableCompressionInSingleFile`은 사용하지 않음**(콜드 스타트 비용↑).

---

## 9. 테스트 전략

- **TDD 핵심(Memoria.Core, 자동 테스트)**:
  - 양식 A 렌더링: 머리글, 탭 들여쓰기, `* ` 글머리, **`[업무 내용]`↔`[이슈]` 사이 빈 줄 1개**.
  - 양식 B 렌더링: 제목 줄 날짜 포맷(MM/DD 0포함, ` ~ `, 콜론), 고객사 섹션 순서·빈 섹션, `[ 미분류 ]` 조건부 출력, `* 이슈사항:` 섹션.
  - 고객사 분류 우선순위(`자율형공장`>`SLD`, `충북`, `MTP`, 미분류), 비활성 고객사 규칙 무시, auto/manual 재계산.
  - 주차 계산(월~금, 주 경계, 연말/연초).
  - `includeDoneOnly` task-only 동작.
- **통합 테스트**: 임시 파일 SQLite로 Repository CRUD, 소프트삭제/복원, 그룹삭제 SET NULL, CASCADE, 마이그레이션 러너, FTS 검색.
- **수동 검증(UI/Win32)**: 단축키/트레이/자동시작/단일 인스턴스/테마 전환/포그라운드 — 실제 Windows 실행으로 확인(검증 체크리스트는 구현 계획에서).

---

## 10. 문서화 / Git / 빌드 / 배포 (전체 라이프사이클)

### 10.1 문서
- `README.md`(소개/기능/스크린샷/설치/실행/빌드/단축키/데이터 위치), `docs/architecture.md`, `docs/weekly-report-format.md`(§6 추출), `docs/user-guide.md`(한글), `CHANGELOG.md`.

### 10.2 Git
- `master`(또는 `main`) + 기능 브랜치, 의미 있는 커밋. `.gitignore`(.NET 표준 + `*.db`).

### 10.3 빌드 (Windows 툴체인으로 통일)
- **WPF는 Linux 네이티브 `dotnet`으로 빌드 불가.** 빌드는 **Windows .NET 9 SDK(`dotnet.exe`)** 로 수행한다. WSL에서 호출 시 **Windows 절대경로**를 인자로 전달(`dotnet.exe build "C:\...\Memoria.sln"`). 권장 경로는 §10.4 CI.
- **사용자 Windows 폴백(PowerShell)**: 
  ```powershell
  cd "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria"
  dotnet publish src\Memoria.App -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
  ```
  예상 결과: `...\publish\Memoria.exe` 단일 실행 파일.
- **금지**: WPF는 트리밍 미지원 → `PublishTrimmed` 절대 사용 금지. `EnableCompressionInSingleFile` 미사용(스타트업).

### 10.4 배포 (GitHub Releases)
- 사용자가 생성한 저장소에 푸시. **GitHub Actions(`windows-latest`)**: 태그 `v*` 푸시 시 → `dotnet publish`(단일 exe) → **Release 자산 자동 첨부**. 첫 릴리스 `v0.1.0`.

---

## 11. 구현 단계 (마일스톤별 — 각 단계가 독립 검증 가능)

스펙이 크므로 **단일 계획이 아닌 마일스톤들**로 나눠 구현한다(각 마일스톤 = 별도 구현 계획 가능).

- **M1. Core 엔진 (TDD)**: 솔루션/3프로젝트 스캐폴딩, 도메인 모델, 주간보고 엔진(분류·양식 A/B·주차계산), Repository(SQLite)+마이그레이션+시드+FTS. **UI 없이 100% 자동 테스트.** ← 가장 위험한 로직 선검증.
- **M2. WPF 셸**: 메인 창/사이드바/그룹 트리/메모 목록/검색, 일반 메모 에디터, 디바운스 자동저장 + 크래시 복구.
- **M3. 체크리스트(일일 업무일지)**: 항목 CRUD, 체크/취소선, 고객사 자동 태깅 + 수동 교정 UI.
- **M4. 주간보고 뷰**: 주차 선택/양식 토글/미분류 경고/복사/재생성.
- **M5. 그룹·휴지통**: 그룹 CRUD, 소프트삭제/휴지통/복원/Undo.
- **M6. Windows 통합**: message-only 단축키, 트레이, 자동시작, 단일 인스턴스(named pipe), 포그라운드.
- **M7. 테마/설정**: 모드/강조색/프리셋, 설정 화면.
- **M8. 문서·CI·릴리스**: 문서 일습, GitHub Actions, `v0.1.0`.

---

## 12. 추후 확인/결정 항목
- 테마 커스터마이징 범위: v1은 모드+강조색+프리셋 팔레트까지. 요소별 세부 색 지정은 추후.
- 그룹 중첩(트리): v1 평면, `parent_id` 예약.

---

## 13. 적대적 검토 반영 이력 (v1.0 → v1.1)

5개 렌즈(완전성·일관성·모호성·WPF/.NET 타당성·도메인 정확성) 병렬 검토에서 도출(critical 8 / high 12 / medium 13 / low 11). 주요 반영:

- **반영(주요)**: 메모 소프트삭제·휴지통·Undo / 그룹 CRUD + `ON DELETE SET NULL` / 자동저장 실패 복구 저널(recovery JSON) / 스키마 마이그레이션(`user_version`+`_migrations`) / **WAL 안전 백업(VACUUM INTO·Online Backup)** / **DB를 `%LOCALAPPDATA%`로 이전** / FTS5 전문검색(체크리스트 항목 포함) / 양식 B `[ 미분류 ]` 섹션+경고 / 분류 캐시+`is_manual` 수동보호+생성시 auto 재계산 / 이슈는 분류 제외(client_id NULL) / `updated_at` 갱신 규칙 / message-only 창 단축키 / named pipe 단일 인스턴스+`AllowSetForegroundWindow` / 빌드는 Windows `dotnet.exe`/CI(WPF는 Linux dotnet 불가, 압축·트리밍 금지) / 성능 인덱스 / 단일 직렬 라이터+busy_timeout / 시스템 테마 감지(레지스트리+WM_SETTINGCHANGE) / `H.NotifyIcon.Wpf` 패키지명 정정 / 각종 기본값(백업 7, 휴지통 30일, autostart ON 등) / `display_label` 컬럼 제거.
- **반려(검증 후)**: "양식 A의 `[업무 내용]`↔`[이슈]` 사이에 빈 줄이 없어야 한다"는 도메인 지적 → **사용자 실제 예시에는 빈 줄이 존재**하므로 반려. 빈 줄 1개 유지(원인: 검토 프롬프트에 넣은 압축 예시에서 빈 줄이 누락된 false positive).

# Memoria — M8 문서 + CI + 릴리스 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Memoria v1의 사용자/개발자 문서 일습과 GitHub Actions(CI + Release) 파이프라인을 완성하고, 태그 `v0.1.0` 푸시로 단일 실행 파일(`Memoria.exe`)을 자동 빌드·릴리스 자산으로 첨부하는 첫 릴리스 절차까지 검증한다.

**Architecture:** M8은 코드 산출물이 아니라 **문서 + CI/CD 구성 + 릴리스 운영 절차**를 산출한다. 문서는 저장소 루트(`README.md`, `CHANGELOG.md`)와 `docs/`(architecture/weekly-report-format/user-guide)에 배치하고, 워크플로는 `.github/workflows/{ci,release}.yml`에 둔다. CI는 push/PR마다 `windows-latest`에서 `dotnet build + test`를 돌리고, Release는 `v*` 태그 푸시 시 `dotnet publish`로 self-contained 단일 exe를 만들어 `softprops/action-gh-release`로 첨부한다.

**Tech Stack:** Markdown(문서), GitHub Actions(`windows-latest`), `actions/checkout@v4`, `actions/setup-dotnet@v4`, `softprops/action-gh-release@v2`, .NET 9 SDK(`dotnet publish`), Git/`gh` CLI.

## Global Constraints
- 런타임/SDK: **.NET 9** (GitHub Actions `setup-dotnet` `dotnet-version: '9.0.x'`).
- TFM: **Core = `net9.0`**, **App = `net9.0-windows`**, **Tests = `net9.0-windows`**.
- DB 위치: **`%LOCALAPPDATA%\Memoria`** (문서에 명시; 로밍/네트워크 경로 금지).
- WPF 배포: **트리밍 금지(`PublishTrimmed=false`)**, **단일파일 압축 금지(`EnableCompressionInSingleFile=false`)**.
- 단일 exe 옵션: **self-contained, `-r win-x64`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`**.
- 빌드/테스트/퍼블리시는 **Windows .NET 9 SDK(`dotnet.exe`)** 로 수행하며, WSL에서 호출 시 **Windows 절대경로** 사용. 저장소 Windows 경로: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled`.
- 릴리스 트리거: **태그 `v*` 푸시**, 러너 **`windows-latest`**. 첫 릴리스 **`v0.1.0`**.
- 액션 버전 핀 고정: `actions/checkout@v4`, `actions/setup-dotnet@v4`, `softprops/action-gh-release@v2`.
- 전역 단축키: 새 메모 **`Ctrl+Alt+N`** (문서/단축키 표에 명시).
- 분류 우선순위: **자율형공장 > SLD** (weekly-report-format 문서에 그대로 반영).
- 양식 A: **`[업무 내용]`↔`[이슈]` 사이 빈 줄 1개** (weekly-report-format 문서에 그대로 반영).
- 테마 규약: 모든 색/브러시는 **`DynamicResource`만** 사용 (architecture 문서에 반영).
- CHANGELOG: **Keep a Changelog** 포맷, 첫 항목 **v0.1.0**.

---

### Task 1: README.md 작성 (소개/기능/스크린샷/설치/실행/빌드/단축키/데이터 위치)

**Files:**
- Create: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\README.md`
- Test(검증 스크립트, WSL): `grep` 기반 섹션 존재 점검 (아래 Step 2 명령)

**Interfaces:**
- Consumes: 전 마일스톤 산출물의 빌드 명령 규약(계약 §7) — 솔루션 경로 `Memoria.sln`, 퍼블리시 대상 `src/Memoria.App`. 단축키 `hotkey.newNote` 기본 `Ctrl+Alt+N`(계약 §6 `SettingsKeys.HotkeyNewNote`). DB 위치 `%LOCALAPPDATA%\Memoria`(스펙 §8.1).
- Produces: `README.md` — 이후 Task 8(첫 릴리스)과 저장소 첫 화면이 의존.

문서는 코드 TDD 대상이 아니므로, "failing test"는 README가 갖춰야 할 필수 섹션이 **아직 없음**을 grep으로 확인하는 것이고, "pass"는 작성 후 모든 섹션이 검출되는 것이다.

- [ ] **Step 1: Write the failing test** (필수 섹션 점검 스크립트 준비)
  아래 한 줄 검증 명령을 사용한다. README가 없으면 grep이 비정상 종료(파일 없음)하거나 매칭 0건으로 실패한다.
  ```bash
  README="/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled/README.md"
  for h in "# Memoria" "## 소개" "## 주요 기능" "## 스크린샷" "## 설치" "## 실행" "## 빌드" "## 단축키" "## 데이터 위치" "Ctrl+Alt+N" '%LOCALAPPDATA%\Memoria'; do
    grep -qF "$h" "$README" || { echo "MISSING: $h"; exit 1; }
  done
  echo "README OK"
  ```

- [ ] **Step 2: Run test to verify it fails**
  ```bash
  README="/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled/README.md"
  for h in "# Memoria" "## 소개" "## 주요 기능" "## 스크린샷" "## 설치" "## 실행" "## 빌드" "## 단축키" "## 데이터 위치" "Ctrl+Alt+N" '%LOCALAPPDATA%\Memoria'; do grep -qF "$h" "$README" 2>/dev/null || { echo "MISSING: $h"; exit 1; }; done; echo "README OK"
  ```
  예상 실패: `grep: ...README.md: No such file or directory` 후 `MISSING: # Memoria` 출력, 종료코드 1.

- [ ] **Step 3: Write minimal implementation** (README.md 작성)
  ```markdown
  # Memoria

  > 저장하지 않아도 사라지지 않는 빠르게 켜는 메모장 + 그룹/날짜 정리 + 매일 체크리스트 → 금요일 주간보고 자동 생성. 네이티브 Windows 데스크톱 앱.

  ## 소개
  Memoria는 Windows 메모장의 가벼움과 즉시성을 유지하면서, "저장" 행위를 없애고(자동 영속화) 메모를 그룹·날짜로 정리하며, 일일 업무일지를 주간보고(양식 2종)로 자동 생성하는 앱입니다. .NET 9 / WPF / SQLite(WAL, FTS5)로 구현되었습니다.

  ## 주요 기능
  - 일반 메모(plain): 입력 즉시 디바운스 자동 저장(저장 버튼 없음).
  - 체크리스트(일일 업무일지): 할 일(취소선) + 이슈, 고객사 자동 태깅 + 수동 교정.
  - 주간보고 자동 생성: 양식 A(할일/이슈 순), 양식 B(고객사별 분류 + 제목줄), 클립보드 복사.
  - 그룹(분류) 트리, (미분류) 가상 노드, 시스템 그룹(일일업무일지/주간보고).
  - 전문검색(FTS5): 제목 + 본문 + 체크리스트 항목.
  - 휴지통(소프트 삭제) + 복원/Undo.
  - 전역 단축키 `Ctrl+Alt+N`, 트레이 상주, 자동시작, 단일 인스턴스.
  - 테마: 라이트/다크/시스템 모드 + 강조색 + 프리셋 팔레트.

  ## 스크린샷
  > 스크린샷은 첫 릴리스 후 추가 예정입니다.

  - 메인 윈도우: `docs/images/main-window.png` (TBD)
  - 주간보고 생성: `docs/images/weekly-report.png` (TBD)
  - 테마 전환: `docs/images/theme.png` (TBD)

  ## 설치
  1. [Releases](../../releases) 페이지에서 최신 `Memoria.exe`를 내려받습니다.
  2. 단일 실행 파일이므로 별도 설치 과정이 없습니다. 원하는 폴더에 두고 실행하세요.
  3. .NET 런타임 사전 설치 불필요(self-contained, win-x64).

  ## 실행
  - `Memoria.exe`를 더블 클릭하면 트레이에 상주합니다.
  - 어디서든 `Ctrl+Alt+N`으로 새 메모를 즉시 생성합니다.
  - 트레이 아이콘 좌클릭으로 메인 창 표시/숨김을 토글합니다.

  ## 빌드
  WPF는 Linux 네이티브 `dotnet`으로 빌드할 수 없습니다. **Windows .NET 9 SDK(`dotnet.exe`)** 로 빌드하세요.

  WSL에서 호출(Windows 절대경로 사용):
  ```bash
  dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\Memoria.sln"
  dotnet.exe test  "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests"
  ```

  Windows PowerShell에서 단일 exe 퍼블리시:
  ```powershell
  cd "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled"
  dotnet publish src\Memoria.App -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false -p:PublishTrimmed=false
  ```
  결과: `...\publish\Memoria.exe` 단일 실행 파일.

  > 주의: WPF는 트리밍을 지원하지 않으므로 `PublishTrimmed`를 절대 사용하지 않습니다. 콜드 스타트 비용 때문에 `EnableCompressionInSingleFile`도 사용하지 않습니다.

  ## 단축키
  | 동작 | 단축키 |
  |---|---|
  | 새 메모(전역) | `Ctrl+Alt+N` |
  | 메인 창 표시/숨김 | 트레이 아이콘 좌클릭 |

  ## 데이터 위치
  - 데이터베이스: `%LOCALAPPDATA%\Memoria\memoria.db` (SQLite, WAL 모드)
  - 백업: `%LOCALAPPDATA%\Memoria\backups\`
  - 크래시 복구 저널: `%LOCALAPPDATA%\Memoria\recovery\`

  로밍(`%APPDATA%`)·네트워크 경로는 WAL 공유메모리(-shm)와 호환되지 않아 사용하지 않습니다.

  ## 문서
  - [아키텍처](docs/architecture.md)
  - [주간보고 양식 규칙](docs/weekly-report-format.md)
  - [사용자 가이드(한글)](docs/user-guide.md)
  - [변경 이력](CHANGELOG.md)

  ## 라이선스
  TBD
  ```

- [ ] **Step 4: Run test to verify it passes**
  ```bash
  README="/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled/README.md"
  for h in "# Memoria" "## 소개" "## 주요 기능" "## 스크린샷" "## 설치" "## 실행" "## 빌드" "## 단축키" "## 데이터 위치" "Ctrl+Alt+N" '%LOCALAPPDATA%\Memoria'; do grep -qF "$h" "$README" || { echo "MISSING: $h"; exit 1; }; done; echo "README OK"
  ```
  예상 통과: `README OK`, 종료코드 0.

- [ ] **Step 5: Commit**
  ```bash
  cd "/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled"
  git add README.md
  git commit -m "docs: add README with intro, features, install, build, shortcuts, data location

  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

---

### Task 2: docs/architecture.md 작성

**Files:**
- Create: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\docs\architecture.md`
- Test(검증 스크립트, WSL): 필수 섹션/경로 grep 점검

**Interfaces:**
- Consumes: 스펙 §3(프로젝트 구조/의존 방향), §7(데이터 모델), §8(Windows 통합), 계약 §1~§7(네임스페이스/인터페이스). 정확한 식별자 사용: `Memoria.Core`, `Memoria.App`, `Memoria.Tests`, `IWeeklyReportRenderer`, `IClientClassifier`, `INoteRepository`, `IDatabaseInitializer`.
- Produces: `docs/architecture.md` — README가 링크로 의존.

- [ ] **Step 1: Write the failing test**
  ```bash
  DOC="/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled/docs/architecture.md"
  for h in "# Memoria 아키텍처" "## 프로젝트 구조" "## 의존 방향" "## 데이터 모델" "## Windows 통합" "## 인터페이스 계약" "Memoria.Core" "net9.0-windows" "DynamicResource"; do
    grep -qF "$h" "$DOC" || { echo "MISSING: $h"; exit 1; }
  done; echo "ARCH OK"
  ```

- [ ] **Step 2: Run test to verify it fails**
  Step 1의 명령을 그대로 실행. 예상 실패: `grep: ...architecture.md: No such file or directory` 후 `MISSING: # Memoria 아키텍처`, 종료코드 1.

- [ ] **Step 3: Write minimal implementation** (docs/architecture.md 작성)
  ```markdown
  # Memoria 아키텍처

  ## 개요
  Memoria는 "틀리면 치명적인" 순수 로직(주간보고 엔진·분류·주차계산·Repository)을 UI에서 분리해 TDD로 검증하는 구조입니다.

  ## 프로젝트 구조
  ```
  Memoria.sln
  ├─ src/
  │  ├─ Memoria.Core/   # 도메인 모델 + 서비스 + 주간보고 엔진 + Repository (UI 비의존, net9.0)
  │  └─ Memoria.App/    # WPF UI (net9.0-windows): View/ViewModel, 트레이, 단축키, 자동시작, 테마
  └─ tests/
     └─ Memoria.Tests/  # xUnit (net9.0-windows): Core 로직 + App ViewModel 테스트
  ```

  | 프로젝트 | TFM | 역할 |
  |---|---|---|
  | Memoria.Core | net9.0 | 도메인/서비스/엔진/Repository (Windows 비의존) |
  | Memoria.App | net9.0-windows | WPF UI, Win32 통합 |
  | Memoria.Tests | net9.0-windows | xUnit + FluentAssertions |

  ## 의존 방향
  ```
  Memoria.App (WPF/Win32 통합) ──► Memoria.Core (모델/서비스/엔진/Repository) ──► SQLite
  ```
  - `Memoria.Core`는 WPF/Win32에 의존하지 않습니다.
  - 자동시작·단축키·트레이·테마 등 Windows 통합 코드는 `Memoria.App`에만 둡니다.
  - ViewModel은 `Memoria.App`에 두되 `CommunityToolkit.Mvvm`만 의존하고 WPF 타입을 피해 `Memoria.Tests`에서 테스트합니다. code-behind는 얇게 유지합니다.

  ## 데이터 모델
  - 저장소: SQLite(`Microsoft.Data.Sqlite` + `Dapper`), **WAL 모드**, `busy_timeout=5000`. 쓰기는 **`SqliteConnectionFactory`의 단일 영속 쓰기 연결 + `object WriteSync` 락**으로 직렬화(`lock (factory.WriteSync)`), 읽기는 WAL 동시읽기 허용(계약 §8).
  - 위치: `%LOCALAPPDATA%\Memoria\memoria.db` (로밍/네트워크 경로 금지).
  - 핵심 테이블: `groups`, `notes`, `checklist_items`, `clients`, `client_rules`, `settings`.
  - 전문검색: `notes_fts` (FTS5, `title + body + items`), 트리거로 동기화.
  - 스키마 버전: `PRAGMA user_version = 1` + `_migrations` 러너로 순차 적용.
  - 시드: §6.3 고객사/규칙, 시스템 그룹 2개(일일업무일지/주간보고), settings 기본값.

  ## 핵심 로직 (Memoria.Core)
  - 분류: `Memoria.Core.Classification.IClientClassifier` — 활성 고객사 규칙을 `Priority` 오름차순 평가, 첫 매칭(대소문자 무시). 우선순위 `자율형공장` > `SLD`.
  - 주차: `Memoria.Core.Classification.IWeekCalculator` — 임의 날짜 → (월요일, 금요일).
  - 렌더: `Memoria.Core.Reporting.IWeeklyReportRenderer` — 양식 A/B 텍스트 생성.
  - 오케스트레이션: `Memoria.Core.Services.ITaggingService`, `IWeeklyReportService`.
  - 영속성: `Memoria.Core.Data.IDatabaseInitializer`, `IGroupRepository`, `INoteRepository`, `IChecklistRepository`, `IClientRepository`, `ISettingsRepository`, `ISearchService`, `IBackupService`.
  - 검색: `Memoria.Core.Data.ISearchService`(FTS5, `title+body+items`)는 Core가 제공하고, **검색 UI(검색창/결과 패널/이동)는 M9(`Memoria.App`)에서 이 서비스를 소비해 구현**된다.

  ## Windows 통합 (Memoria.App)
  - 전역 단축키: Win32 `RegisterHotKey`(+`MOD_NOREPEAT`)를 message-only 창(HWND_MESSAGE)에 후킹. 메인 창을 닫아도 단축키 유지.
  - 트레이: `H.NotifyIcon.Wpf`. 창 닫기(X)는 종료가 아니라 Hide(HWND 유지).
  - 단일 인스턴스: 명명된 `Mutex` + named pipe IPC, 두 번째 인스턴스는 `AllowSetForegroundWindow`로 포그라운드 전환 신호.
  - 자동시작: `HKCU\...\Run` 레지스트리 값.
  - 테마: 모든 색/브러시는 **`DynamicResource`만** 사용(StaticResource 금지). 최상위 `MergedDictionaries`의 테마 사전 1개만 교체. 시스템 모드는 레지스트리 `AppsUseLightTheme` + `WM_SETTINGCHANGE` 감지.

  ## 영속화/안전성
  - 디바운스 자동저장(~500ms), 종료/숨김/`SessionEnding` 시 즉시 flush.
  - 크래시 복구 저널(`recovery\{noteId}.json`), 정상 저장 시 삭제.
  - 자동 백업/무결성: `Memoria.Core.Data.IBackupService`(계약 §8)가 `BackupIfDue(retentionCount)`(`VACUUM INTO` 또는 `BackupDatabase` 스냅샷, `backup.retentionCount` 기본 7 유지), `IsDatabaseHealthy()`(`PRAGMA integrity_check == 'ok'`), `TryRestoreFromLatestBackup()`(손상 시 `*.corrupt` 격리 후 최신 정상 백업 복원)를 **Core에서 제공**.
  - 앱 시작 시 이 서비스의 **배선은 M9 부트스트랩(계약 §9.4 5)무결성 점검·복원 → 6)일일 백업)** 에서 수행. 종료 시 `wal_checkpoint(TRUNCATE)` 후 Dispose.

  ## 인터페이스 계약
  모든 마일스톤은 `docs/superpowers/plans/2026-06-26-memoria-interface-contracts.md`를 단일 진리원천으로 공유합니다.

  ## 빌드/배포
  - 빌드/테스트/퍼블리시는 Windows .NET 9 SDK(`dotnet.exe`).
  - 단일 exe: self-contained, `win-x64`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`. 트리밍/압축 금지.
  - CI: push/PR → `dotnet build + test`. Release: 태그 `v*` → publish → GitHub Release 자산 첨부.
  ```

- [ ] **Step 4: Run test to verify it passes**
  Step 1의 명령 실행. 예상 통과: `ARCH OK`, 종료코드 0.

- [ ] **Step 5: Commit**
  ```bash
  cd "/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled"
  git add docs/architecture.md
  git commit -m "docs: add architecture overview (projects, dependencies, data model, Windows integration)

  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

---

### Task 3: docs/weekly-report-format.md 작성 (스펙 §6 추출)

**Files:**
- Create: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\docs\weekly-report-format.md`
- Test(검증 스크립트, WSL): 양식 A 빈 줄 규칙 + 분류 우선순위 + 양식 B 제목 형식 grep 점검

**Interfaces:**
- Consumes: 스펙 §6(주간보고 엔진), 계약 §3(`ReportRenderOptions`, `IWeeklyReportRenderer`). 정확한 값: 양식 A `[업무 내용]`↔`[이슈]` 사이 빈 줄 1개, 제목줄 `[ {이름} 주간 보고 (MM/DD ~ MM/DD) ]:`, 분류 우선순위 `자율형공장` > `SLD`.
- Produces: `docs/weekly-report-format.md` — README/architecture가 링크로 의존.

- [ ] **Step 1: Write the failing test**
  ```bash
  DOC="/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled/docs/weekly-report-format.md"
  for h in "# 주간보고 양식 규칙" "## 양식 A" "## 양식 B" "[업무 내용]" "[이슈]" "주간 보고 (MM/DD ~ MM/DD)" "자율형공장" "SLD" "미분류" "* 이슈사항:" "빈 줄 1개"; do
    grep -qF "$h" "$DOC" || { echo "MISSING: $h"; exit 1; }
  done; echo "REPORT-FMT OK"
  ```

- [ ] **Step 2: Run test to verify it fails**
  Step 1 명령 실행. 예상 실패: 파일 없음 + `MISSING: # 주간보고 양식 규칙`, 종료코드 1.

- [ ] **Step 3: Write minimal implementation** (docs/weekly-report-format.md 작성)
  ```markdown
  # 주간보고 양식 규칙

  > 본 문서는 설계 스펙 §6(주간보고 엔진)에서 추출한 단일 참조 규칙입니다. 구현은 계약 §3 `IWeeklyReportRenderer.Render(...)`를 따릅니다.

  ## 입력
  선택한 주의 **월요일~금요일** 범위에 `log_date`가 포함되는, 삭제되지 않은(`deleted_at IS NULL`) 체크리스트 메모의 항목들.
  - `kind=task` → 업무 내용, `kind=issue` → 이슈.
  - `IncludeDoneOnly`(기본 OFF): ON이면 `kind=task` 중 `done=1`만 포함. **issue는 이 옵션과 무관하게 항상 전부 포함**.

  ## 양식 A — 할일/이슈 순
  ```
  [업무 내용]
  	* {task 1}
  	* {task 2}

  [이슈]
  	* {issue 1}
  	* {issue 2}
  ```
  - `[업무 내용]` 블록과 `[이슈]` 머리글 사이에 **빈 줄 1개**(사용자 실제 예시 기준 — 확정).
  - 머리글 기본값: `[업무 내용]`(`report.formatA.taskHeader`) / `[이슈]`(`report.formatA.issueHeader`).
  - 들여쓰기 = 탭 1개(`report.indent`), 글머리 = `* `.

  ## 양식 B — 고객사별 분류 + 제목줄
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
  - 제목 줄: `[ {이름} 주간 보고 (MM/DD ~ MM/DD) ]:`
    - `{이름}` 기본값 **"이승현"**(`report.reporterName`).
    - 날짜는 **0 포함 2자리**(`06/23`), 구분자 ` ~ `, 끝에 콜론 `:`. 아이콘 출력 안 함.
  - 고객사 섹션: 활성(`enabled=1`) 고객사를 `clients.sort_order` 순서대로 출력. **항목이 없어도 빈 섹션 머리글 출력**.
  - `[ 미분류 ]` 섹션: 미분류 task가 **1개 이상일 때만** 이슈 섹션 직전에 출력. 미분류가 있으면 생성 다이얼로그에 경고 배너 표시. 0개면 섹션 생략.
  - 이슈 섹션: `* 이슈사항:`(`report.formatB.issueHeader`) 줄 뒤 이슈 나열.
  - 들여쓰기/글머리: 양식 A와 동일(탭 + `* `).
  - 고정 문구 `주간 보고`는 `report.formatB.titleWord`로 변경 가능.

  ## 고객사 자동 분류 규칙
  규칙은 우선순위(`client_rules.priority`) 오름차순으로 평가하며, 첫 매칭 적용(키워드 "포함" 여부, 대소문자 무시, 비활성 고객사 규칙 무시).

  | 우선순위 | 키워드(포함 시) | → 고객사 |
  |---|---|---|
  | 1 | `자율형공장`, `자율형 공장` | 자율형 공장 |
  | 2 | `충북`, `충북테크놀로지파크`, `DL정보기술` | 충북테크놀로지파크 |
  | 3 | `코모텍` | 코모텍 |
  | 4 | `MTP`, `머티리얼즈파크` | MTP |
  | 5 | `카본센스` | 카본센스 |
  | 6 | `SLD` | SLD |
  | 7 | (위 모두 미해당) | 미분류 (client_id=NULL) |

  확정 규칙: task에 `자율형공장` 키워드가 있으면 → **자율형 공장**(SLD보다 우선). `SLD`만 있고 `자율형공장`이 없으면 → SLD.

  ## 분류 영속화/재계산
  - 자동 분류는 task 저장 시 `client_id`에 캐시.
  - 수동 교정 시 `is_manual=1`로 보호, 이후 자동 재분류로 덮어쓰지 않음.
  - 주간보고 생성 시 `is_manual=0` 항목은 현재 규칙으로 재계산, `is_manual=1` 항목은 그대로 사용.

  ## 표시순 vs 매칭순 (독립)
  - 표시순(양식 B 섹션 순서) = `clients.sort_order`.
  - 매칭 우선순위 = `client_rules.priority`. "고객사 순서변경" UI는 표시순만 바꾸며 매칭 우선순위에 영향 없음.

  ## 주차 계산
  월요일 시작, 금요일 종료. 임의 날짜 → 그 주의 월/금. 기본값 = 오늘이 포함된 주.
  ```

- [ ] **Step 4: Run test to verify it passes**
  Step 1 명령 실행. 예상 통과: `REPORT-FMT OK`, 종료코드 0.

- [ ] **Step 5: Commit**
  ```bash
  cd "/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled"
  git add docs/weekly-report-format.md
  git commit -m "docs: extract weekly report format rules (forms A/B, classification priority)

  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

---

### Task 4: docs/user-guide.md 작성 (한글)

**Files:**
- Create: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\docs\user-guide.md`
- Test(검증 스크립트, WSL): 필수 사용 시나리오 섹션 grep 점검

**Interfaces:**
- Consumes: 스펙 §4(화면), §5(메모 유형), §4.4~4.6(검색/휴지통/테마), 단축키 `Ctrl+Alt+N`, DB 위치 `%LOCALAPPDATA%\Memoria`.
- Produces: `docs/user-guide.md` — README가 링크로 의존.

- [ ] **Step 1: Write the failing test**
  ```bash
  DOC="/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled/docs/user-guide.md"
  for h in "# Memoria 사용자 가이드" "## 시작하기" "## 메모 작성" "## 체크리스트" "## 주간보고" "## 그룹 관리" "## 검색" "## 휴지통" "## 테마 변경" "## 데이터 위치와 백업" "Ctrl+Alt+N"; do
    grep -qF "$h" "$DOC" || { echo "MISSING: $h"; exit 1; }
  done; echo "USER-GUIDE OK"
  ```

- [ ] **Step 2: Run test to verify it fails**
  Step 1 명령 실행. 예상 실패: 파일 없음 + `MISSING: # Memoria 사용자 가이드`, 종료코드 1.

- [ ] **Step 3: Write minimal implementation** (docs/user-guide.md 작성)
  ```markdown
  # Memoria 사용자 가이드

  ## 시작하기
  1. `Memoria.exe`를 실행하면 트레이에 상주합니다.
  2. 어디서든 `Ctrl+Alt+N`을 누르면 새 일반 메모가 즉시 만들어지고 메인 창이 앞으로 나옵니다.
  3. 트레이 아이콘 좌클릭으로 메인 창을 표시/숨김 토글합니다.
  4. 창 닫기(X)는 종료가 아니라 트레이로 숨김입니다(설정에서 "닫으면 종료"로 변경 가능).
  5. 메모가 하나도 없으면 본문 영역에 "Ctrl+Alt+N 또는 [+ 새 메모]로 시작하세요" 안내가 보입니다.

  ## 메모 작성
  - [+ 새 메모]를 누르면 일반(plain) 메모가 생성됩니다.
  - 저장 버튼이 없습니다. 입력을 멈추면 약 0.5초 후 자동 저장됩니다.
  - 제목을 비워두면 본문 첫 줄이 표시용 제목으로 쓰입니다.
  - 에디터 헤더에 최초 생성일과 최근 수정일이 표시됩니다.

  ## 체크리스트 (일일 업무일지)
  - [+ 체크리스트]를 누르면 하루치 업무일지 메모가 만들어집니다(기본 제목 = 날짜).
  - 항목 두 종류:
    - 할 일(task): 체크박스 + 텍스트. 체크하면 취소선이 그어지고 완료 시각이 기록됩니다.
    - 이슈(issue): 체크박스 없는 텍스트.
  - 할 일 항목은 고객사가 자동 태깅되며, 드롭다운으로 직접 교정할 수 있습니다(교정하면 자동 재분류에서 보호됩니다).

  ## 주간보고
  - [주간보고]에서 주차를 선택하고 양식 A/B를 토글합니다.
  - 양식 A: 할 일/이슈 순. 양식 B: 고객사별 분류 + 제목줄.
  - 미분류 할 일이 있으면 경고 배너가 표시됩니다.
  - 생성 결과는 편집 가능하며 클립보드 복사 버튼이 있습니다.
  - "다시 생성" 시 같은 주/양식 메모를 재사용하며, 편집분을 덮어쓰기 전에 확인을 받습니다.
  - 자세한 양식 규칙은 [주간보고 양식 규칙](weekly-report-format.md)을 참고하세요.

  ## 그룹 관리
  - 사이드바에서 그룹을 추가/이름변경/색상지정/순서변경(드래그)/삭제할 수 있습니다.
  - 그룹을 삭제해도 그 안의 메모는 삭제되지 않고 (미분류)로 이동합니다.
  - 시스템 그룹(일일업무일지/주간보고)은 삭제/이름변경할 수 없습니다.

  ## 검색
  - 상단 검색창은 제목 + 본문 + 체크리스트 항목을 대상으로 합니다.
  - 결과를 클릭하면 해당 메모로 이동합니다.

  ## 휴지통
  - 메모 삭제는 소프트 삭제입니다. 휴지통에서 복원하거나 영구삭제할 수 있습니다.
  - 삭제 직후 토스트에서 실행취소(Undo)할 수 있습니다.
  - 기본 30일이 지나면 자동으로 영구삭제됩니다(설정에서 변경 가능).

  ## 테마 변경
  - 설정에서 모드(라이트/다크/시스템), 강조색, 프리셋 팔레트를 변경합니다.
  - 시스템 모드는 Windows 테마 변경을 자동 감지합니다.

  ## 데이터 위치와 백업
  - 데이터: `%LOCALAPPDATA%\Memoria\memoria.db`
  - 백업: `%LOCALAPPDATA%\Memoria\backups\` (하루 1회, 기본 7개 보관)
  - 복구 저널: `%LOCALAPPDATA%\Memoria\recovery\`
  - 클라우드/네트워크 드라이브에 두지 마세요(WAL 호환성 문제).
  ```

- [ ] **Step 4: Run test to verify it passes**
  Step 1 명령 실행. 예상 통과: `USER-GUIDE OK`, 종료코드 0.

- [ ] **Step 5: Commit**
  ```bash
  cd "/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled"
  git add docs/user-guide.md
  git commit -m "docs: add Korean user guide

  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

---

### Task 5: CHANGELOG.md 작성 (Keep a Changelog, v0.1.0)

**Files:**
- Create: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\CHANGELOG.md`
- Test(검증 스크립트, WSL): Keep a Changelog 포맷 + v0.1.0 항목 grep 점검

**Interfaces:**
- Consumes: 스펙 §2(R1~R9 기능), §10.1. v0.1.0 = MVP 전체 기능 집합.
- Produces: `CHANGELOG.md` — Task 8(릴리스 노트 근거), README가 링크로 의존.

- [ ] **Step 1: Write the failing test**
  ```bash
  DOC="/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled/CHANGELOG.md"
  for h in "# Changelog" "Keep a Changelog" "Semantic Versioning" "[Unreleased]" "## [0.1.0]" "### Added"; do
    grep -qF "$h" "$DOC" || { echo "MISSING: $h"; exit 1; }
  done; echo "CHANGELOG OK"
  ```

- [ ] **Step 2: Run test to verify it fails**
  Step 1 명령 실행. 예상 실패: 파일 없음 + `MISSING: # Changelog`, 종료코드 1.

- [ ] **Step 3: Write minimal implementation** (CHANGELOG.md 작성)
  ```markdown
  # Changelog

  All notable changes to this project will be documented in this file.

  The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
  and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

  ## [Unreleased]

  ## [0.1.0] - 2026-06-26
  ### Added
  - 일반 메모(plain) 에디터: 디바운스 자동 저장(약 500ms), 저장 버튼 없음.
  - 체크리스트(일일 업무일지): 할 일(취소선)/이슈 항목, 고객사 자동 태깅 + 수동 교정.
  - 주간보고 자동 생성: 양식 A(할일/이슈 순), 양식 B(고객사별 분류 + 제목줄), 클립보드 복사, 멱등 재생성.
  - 고객사 자동 분류 규칙(우선순위 기반, 자율형공장 > SLD), 주차 계산(월~금).
  - 그룹(분류) 트리, (미분류) 가상 노드, 시스템 그룹(일일업무일지/주간보고), 그룹 CRUD(ON DELETE SET NULL).
  - 전문검색(FTS5): 제목 + 본문 + 체크리스트 항목.
  - 휴지통(소프트 삭제) + 복원/영구삭제/Undo, 보존기간 자동 정리(기본 30일).
  - 전역 단축키 `Ctrl+Alt+N`(message-only 창), 트레이 상주, 자동시작, 단일 인스턴스(named pipe).
  - 테마: 라이트/다크/시스템 모드 + 강조색 + 프리셋 팔레트(모든 색 DynamicResource).
  - 데이터 영속화: SQLite WAL, `%LOCALAPPDATA%\Memoria`, 크래시 복구 저널, 자동 백업(VACUUM INTO/Online Backup), 스키마 마이그레이션.
  - 문서: README, 아키텍처, 주간보고 양식 규칙, 사용자 가이드(한글).
  - CI/CD: GitHub Actions(build+test, 태그 릴리스 단일 exe 자산 첨부).

  [Unreleased]: https://github.com/OWNER/REPO/compare/v0.1.0...HEAD
  [0.1.0]: https://github.com/OWNER/REPO/releases/tag/v0.1.0
  ```
  > 주: `OWNER/REPO`는 Task 8에서 실제 저장소 슬러그로 치환한다(저장소 생성 후 확정).

- [ ] **Step 4: Run test to verify it passes**
  Step 1 명령 실행. 예상 통과: `CHANGELOG OK`, 종료코드 0.

- [ ] **Step 5: Commit**
  ```bash
  cd "/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled"
  git add CHANGELOG.md
  git commit -m "docs: add CHANGELOG (Keep a Changelog) with v0.1.0 entry

  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

---

### Task 6: CI 워크플로 작성 (.github/workflows/ci.yml — build + test)

**Files:**
- Create: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\.github\workflows\ci.yml`
- Test(검증 스크립트, WSL): YAML 문법 파싱 + 액션 핀 버전 grep 점검

**Interfaces:**
- Consumes: 솔루션 `Memoria.sln`, 테스트 프로젝트 `tests/Memoria.Tests`(계약 §7), .NET 9.
- Produces: `.github/workflows/ci.yml` — push/PR CI 게이트.

CI/yml은 코드 TDD 대상이 아니므로, "test"는 (a) YAML 문법 파싱과 (b) 핀 고정 액션 버전 검출이다.

- [ ] **Step 1: Write the failing test**
  ```bash
  YML="/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled/.github/workflows/ci.yml"
  python3 -c "import sys,yaml; yaml.safe_load(open(sys.argv[1])); print('YAML OK')" "$YML" || exit 1
  for h in "runs-on: windows-latest" "actions/checkout@v4" "actions/setup-dotnet@v4" "9.0.x" "dotnet build" "dotnet test"; do
    grep -qF "$h" "$YML" || { echo "MISSING: $h"; exit 1; }
  done; echo "CI YML OK"
  ```

- [ ] **Step 2: Run test to verify it fails**
  Step 1 명령 실행. 예상 실패: `FileNotFoundError: ...ci.yml`(python) → 종료코드 1.

- [ ] **Step 3: Write minimal implementation** (.github/workflows/ci.yml 작성)
  ```yaml
  name: CI

  on:
    push:
      branches: [ main, master ]
    pull_request:
      branches: [ main, master ]

  jobs:
    build-and-test:
      runs-on: windows-latest
      steps:
        - name: Checkout
          uses: actions/checkout@v4

        - name: Setup .NET 9
          uses: actions/setup-dotnet@v4
          with:
            dotnet-version: '9.0.x'

        - name: Restore
          run: dotnet restore Memoria.sln

        - name: Build
          run: dotnet build Memoria.sln -c Release --no-restore

        - name: Test
          run: dotnet test tests/Memoria.Tests -c Release --no-build --verbosity normal
  ```

- [ ] **Step 4: Run test to verify it passes**
  Step 1 명령 실행. 예상 통과: `YAML OK` 후 `CI YML OK`, 종료코드 0.
  - **수동 검증 체크포인트(선택, 저장소 푸시 후)**: GitHub Actions 탭에서 `CI` 워크플로가 push/PR에 트리거되어 `windows-latest`에서 build/test가 녹색으로 끝나는지 눈으로 확인.

- [ ] **Step 5: Commit**
  ```bash
  cd "/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled"
  git add .github/workflows/ci.yml
  git commit -m "ci: add build and test workflow on push/PR (windows-latest, .NET 9)

  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

---

### Task 7: Release 워크플로 작성 (.github/workflows/release.yml — 태그 v* → 단일 exe → Release 자산)

**Files:**
- Create: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\.github\workflows\release.yml`
- Test(검증 스크립트, WSL): YAML 파싱 + publish 옵션/핀 액션 grep 점검
- Manual: 로컬 Windows `dotnet.exe publish` 1회 실행으로 단일 exe 생성 확인

**Interfaces:**
- Consumes: 퍼블리시 대상 `src/Memoria.App`, 단일 exe 옵션(스펙 §10.3), `softprops/action-gh-release@v2`.
- Produces: `.github/workflows/release.yml` — Task 8 첫 릴리스가 의존.

- [ ] **Step 1: Write the failing test**
  ```bash
  YML="/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled/.github/workflows/release.yml"
  python3 -c "import sys,yaml; yaml.safe_load(open(sys.argv[1])); print('YAML OK')" "$YML" || exit 1
  for h in "tags:" "'v*'" "runs-on: windows-latest" "actions/checkout@v4" "actions/setup-dotnet@v4" "src/Memoria.App" "win-x64" "--self-contained" "PublishSingleFile=true" "IncludeNativeLibrariesForSelfExtract=true" "EnableCompressionInSingleFile=false" "PublishTrimmed=false" "softprops/action-gh-release@v2" "contents: write"; do
    grep -qF "$h" "$YML" || { echo "MISSING: $h"; exit 1; }
  done; echo "RELEASE YML OK"
  ```

- [ ] **Step 2: Run test to verify it fails**
  Step 1 명령 실행. 예상 실패: `FileNotFoundError: ...release.yml`(python) → 종료코드 1.

- [ ] **Step 3: Write minimal implementation** (.github/workflows/release.yml 작성)
  ```yaml
  name: Release

  on:
    push:
      tags:
        - 'v*'

  permissions:
    contents: write

  jobs:
    publish:
      runs-on: windows-latest
      steps:
        - name: Checkout
          uses: actions/checkout@v4

        - name: Setup .NET 9
          uses: actions/setup-dotnet@v4
          with:
            dotnet-version: '9.0.x'

        - name: Publish single-file (self-contained, win-x64, no trim / no compression)
          run: >
            dotnet publish src/Memoria.App
            -c Release
            -r win-x64
            --self-contained
            -p:PublishSingleFile=true
            -p:IncludeNativeLibrariesForSelfExtract=true
            -p:EnableCompressionInSingleFile=false
            -p:PublishTrimmed=false
            -o publish

        - name: Create GitHub Release and upload asset
          uses: softprops/action-gh-release@v2
          with:
            files: publish/Memoria.exe
            generate_release_notes: true
          env:
            GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
  ```

- [ ] **Step 4: Run test to verify it passes**
  Step 1 명령 실행. 예상 통과: `YAML OK` 후 `RELEASE YML OK`, 종료코드 0.

  - [ ] **수동 검증 체크포인트(필수): 로컬 단일 exe 생성 확인** — Windows 툴체인으로 release.yml과 동일한 옵션을 1회 실행해 단일 exe가 만들어지는지 눈으로 확인한다. (전 마일스톤이 끝나 솔루션이 빌드 가능한 상태여야 함.)
    ```bash
    dotnet.exe publish "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:PublishTrimmed=false -o "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\publish"
    ls -la "/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled/publish/Memoria.exe"
    ```
    확인 항목: (1) 명령이 0 종료코드로 성공, (2) `publish/Memoria.exe` 단일 파일 존재, (3) 더블 클릭 시 트레이 상주 + `Ctrl+Alt+N` 동작. `publish/`는 `.gitignore`로 추적 제외됨(커밋 금지).

- [ ] **Step 5: Commit**
  ```bash
  cd "/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled"
  git add .github/workflows/release.yml
  git commit -m "ci: add release workflow (tag v* -> single-file publish -> gh-release asset)

  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

---

### Task 8: 첫 릴리스 절차 (git tag v0.1.0, push, 검증)

**Files:**
- Modify: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\CHANGELOG.md` (`OWNER/REPO` 슬러그 치환)
- Test/검증: 태그 생성·푸시 후 GitHub Actions Release 실행 + 자산 첨부 확인(수동)

**Interfaces:**
- Consumes: Task 5(CHANGELOG v0.1.0), Task 6/7(ci.yml/release.yml), 빌드 가능한 솔루션(전 마일스톤).
- Produces: `v0.1.0` 태그 + GitHub Release(`Memoria.exe` 자산) — 사용자 배포 산출물.

이 Task는 운영 절차이므로 "test"는 (a) 로컬 태그 무결성과 (b) 원격 Actions 결과를 눈으로 확인하는 것이다.

- [ ] **Step 1: Write the failing test** (릴리스 전 사전 상태 점검 스크립트)
  ```bash
  REPO="/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled"
  cd "$REPO"
  # 워킹 트리 클린 + 워크플로/체인지로그 존재 확인 + 아직 v0.1.0 태그 없음
  test -f .github/workflows/ci.yml && test -f .github/workflows/release.yml && test -f CHANGELOG.md || { echo "MISSING release prerequisites"; exit 1; }
  git tag | grep -qx "v0.1.0" && { echo "v0.1.0 already exists"; exit 1; }
  echo "PRE-RELEASE OK"
  ```

- [ ] **Step 2: Run test to verify it fails**
  Step 1 명령 실행. 아직 원격/저장소 설정 전이거나 전제 미충족이면 `MISSING release prerequisites` 또는 (태그 선존재 시) `v0.1.0 already exists`로 비정상 종료. Task 1~7 완료 후에는 `PRE-RELEASE OK`가 나와야 다음 단계로 진행.

- [ ] **Step 3: Write minimal implementation** (저장소 슬러그 확정 + 원격 연결 + 태그 생성/푸시)
  1) CHANGELOG의 비교/릴리스 링크에서 `OWNER/REPO`를 실제 슬러그로 치환한다(예: 사용자 GitHub 계정/저장소명). 저장소가 아직 없으면 `gh`로 생성한다.
  ```bash
  REPO="/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled"
  cd "$REPO"
  # (원격 없으면) GitHub 저장소 생성 + origin 연결 + 첫 푸시. SLUG는 실제 값으로 대체.
  # gh repo create <OWNER>/Memoria --private --source=. --remote=origin --push
  git remote -v   # origin 확인
  ```
  2) CHANGELOG의 `OWNER/REPO`를 실제 슬러그로 바꾼 뒤 커밋한다(Edit 도구 또는 sed).
  ```bash
  SLUG="OWNER/Memoria"   # 실제 슬러그로 대체
  sed -i "s|OWNER/REPO|$SLUG|g" "$REPO/CHANGELOG.md"
  git add CHANGELOG.md && git commit -m "docs: set CHANGELOG repo slug for release links

  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  git push origin HEAD
  ```
  3) 주석 태그 `v0.1.0` 생성 후 푸시(태그 푸시가 release.yml 트리거).
  ```bash
  cd "$REPO"
  git tag -a v0.1.0 -m "Memoria v0.1.0 (MVP)"
  git push origin v0.1.0
  ```

- [ ] **Step 4: Run test to verify it passes** (릴리스 결과 검증)
  ```bash
  cd "/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled"
  git tag | grep -qx "v0.1.0" && echo "TAG OK"
  # 원격 태그 확인
  git ls-remote --tags origin | grep -q "refs/tags/v0.1.0" && echo "REMOTE TAG OK"
  # Actions/Release 상태(설치된 경우 gh)
  gh run list --workflow=release.yml --limit 1 || true
  gh release view v0.1.0 || true
  ```
  - [ ] **수동 검증 체크포인트(필수)**: GitHub → Actions에서 `Release` 워크플로가 `v0.1.0` 태그로 트리거되어 `windows-latest`에서 성공(녹색)했는지 확인. → Releases 페이지에 `v0.1.0` 릴리스가 생성되고 **`Memoria.exe` 자산이 첨부**되었는지 확인. 자산을 내려받아 더블 클릭 시 트레이 상주 + `Ctrl+Alt+N`이 동작하는지 확인.
  - 실패 시(systematic-debugging): 로그에서 publish 실패(트리밍/압축 옵션, TFM)인지 또는 `GITHUB_TOKEN`/`permissions: contents: write` 누락인지 식별 → release.yml 수정 → 태그 삭제 후 재푸시(`git push origin :refs/tags/v0.1.0` → 재태그 → 재푸시).

- [ ] **Step 5: Commit** (절차 자체는 위에서 커밋·태그로 기록됨)
  추가 변경이 없으면 별도 커밋 불필요. release.yml 수정이 있었다면:
  ```bash
  cd "/mnt/c/Users/adelie/Desktop/ToyProject/15_Untitled/1_PROJECT_FILE/Untitled"
  git add .github/workflows/release.yml
  git commit -m "ci: fix release workflow after first v0.1.0 run

  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  git push origin HEAD
  ```

---

## 완료 기준 (M8)
- `README.md`, `docs/architecture.md`, `docs/weekly-report-format.md`, `docs/user-guide.md`, `CHANGELOG.md` 작성 및 섹션 검증 통과.
- `.github/workflows/ci.yml`(build+test), `.github/workflows/release.yml`(tag v* → 단일 exe → gh-release) 작성 및 YAML 파싱/핀 버전 검증 통과.
- 로컬 Windows `dotnet.exe publish`로 단일 `Memoria.exe` 생성 확인(수동 체크포인트).
- 태그 `v0.1.0` 푸시 → GitHub Release에 `Memoria.exe` 자산 첨부 확인(수동 체크포인트).

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

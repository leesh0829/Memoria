# Memoria — 정식 인터페이스 계약 (Interface Contracts)

> 이 문서는 모든 마일스톤 계획(M1~M8)이 공유하는 **단일 진리 원천**이다.
> M1이 이 타입/시그니처를 **구현(Produces)** 하고, M2~M8은 이를 **소비(Consumes)** 한다.
> 어떤 마일스톤도 여기 정의된 이름/시그니처를 임의로 바꾸지 않는다. 변경이 필요하면 이 문서를 먼저 고친다.

- **네임스페이스 루트**: `Memoria.Core`
- **대상 TFM**: `Memoria.Core` = `net9.0`, `Memoria.App` = `net9.0-windows`, `Memoria.Tests` = `net9.0-windows` (Core 로직 + App ViewModel 모두 테스트 가능; Windows `dotnet.exe`에서 실행)
- **ViewModel 위치**: ViewModel은 `Memoria.App`에 두되 WPF 타입 의존을 피하고 `CommunityToolkit.Mvvm`만 사용 → `Memoria.Tests`(net9.0-windows)에서 참조해 테스트한다. code-behind는 얇게 유지.
- 날짜는 `DateOnly`(달력일) / `DateTimeOffset`(타임스탬프, UTC 저장·로컬 표시). DB에는 ISO-8601 문자열로 저장.

---

## 1. 모델 — `Memoria.Core.Models`

```csharp
namespace Memoria.Core.Models;

public enum NoteType { Plain, Checklist, WeeklyReport }
public enum ItemKind { Task, Issue }
public enum ReportFormatKind { A, B }            // 'ReportFormat'는 Note 속성명과 충돌 방지 위해 Kind 접미사
public enum ThemeMode { Light, Dark, System }

public sealed class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }           // v1 미사용(예약)
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public string? Color { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Note
{
    public int Id { get; set; }
    public int? GroupId { get; set; }
    public NoteType Type { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public DateOnly? LogDate { get; set; }                 // checklist
    public ReportFormatKind? ReportFormat { get; set; }    // weekly_report
    public DateOnly? ReportWeekStart { get; set; }         // weekly_report (Monday)
    public bool Pinned { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }         // null = active
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ChecklistItem
{
    public int Id { get; set; }
    public int NoteId { get; set; }
    public ItemKind Kind { get; set; }
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public int? ClientId { get; set; }            // null = unclassified
    public bool IsManual { get; set; }            // true = 수동 교정됨(자동 재분류 제외)
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = "";        // 양식 B는 "[ " + Name + " ]" 로 렌더
    public int SortOrder { get; set; }            // 표시순(양식 B 섹션 순서)
    public bool Enabled { get; set; } = true;
}

public sealed class ClientRule
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Keyword { get; set; } = "";
    public int Priority { get; set; }             // 전역 매칭 우선순위(작을수록 먼저)
}
```

---

## 2. 분류 / 주차 — `Memoria.Core.Classification`

```csharp
namespace Memoria.Core.Classification;
using Memoria.Core.Models;

public interface IClientClassifier
{
    /// 활성 고객사의 규칙만 대상으로, Priority 오름차순으로 평가하여
    /// 첫 키워드 포함(대소문자 무시) 매칭의 ClientId 반환. 없으면 null(미분류).
    int? Classify(string taskText, IEnumerable<ClientRule> rules, ISet<int> enabledClientIds);
}

public interface IWeekCalculator
{
    /// 임의 날짜가 속한 주의 (월요일, 금요일) 반환.
    (DateOnly Monday, DateOnly Friday) GetWorkWeek(DateOnly anyDate);
}
```

---

## 3. 주간보고 — `Memoria.Core.Reporting`

```csharp
namespace Memoria.Core.Reporting;
using Memoria.Core.Models;

public sealed record ReportTask(string Text, int? ClientId, bool Done);
public sealed record ReportIssue(string Text);

public sealed record WeeklyReportData(
    IReadOnlyList<ReportTask> Tasks,
    IReadOnlyList<ReportIssue> Issues);

public sealed record ReportRenderOptions
{
    public string ReporterName { get; init; } = "이승현";
    public DateOnly WeekStart { get; init; }      // Monday
    public DateOnly WeekEnd { get; init; }        // Friday
    public string TaskHeaderA { get; init; } = "[업무 내용]";
    public string IssueHeaderA { get; init; } = "[이슈]";
    public string TitleWordB { get; init; } = "주간 보고";
    public string IssueHeaderB { get; init; } = "* 이슈사항:";
    public string Indent { get; init; } = "\t";
    public bool IncludeDoneOnly { get; init; } = false;
    public IReadOnlyList<Client> Clients { get; init; } = new List<Client>();  // 표시순(SortOrder 정렬된 활성 고객사)
    public string UnclassifiedLabel { get; init; } = "미분류";
}

public interface IWeeklyReportRenderer
{
    /// 양식 A 또는 B의 최종 텍스트를 반환.
    /// - IncludeDoneOnly=true면 Kind=Task 중 Done=true만 포함(Issue는 항상 전부).
    /// - 양식 A: [업무 내용] 블록과 [이슈] 머리글 사이 빈 줄 1개.
    /// - 양식 B: 제목줄 "[ {ReporterName} {TitleWordB} (MM/dd ~ MM/dd) ]:", 고객사 섹션은 Clients 순서대로(빈 섹션 머리글도 출력),
    ///           미분류 task가 있을 때만 "[ {UnclassifiedLabel} ]" 섹션을 이슈 섹션 직전에 출력, 이슈는 IssueHeaderB 뒤 나열.
    string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options);
}
```

---

## 4. 영속성 — `Memoria.Core.Data`

```csharp
namespace Memoria.Core.Data;
using Memoria.Core.Models;

public interface IDatabaseInitializer
{
    /// 파일 없으면 생성, PRAGMA(WAL/foreign_keys/busy_timeout) 설정,
    /// 마이그레이션 적용(user_version), 첫 실행 시드(clients/client_rules/시스템 그룹/settings 기본값).
    void EnsureReady();
    /// PRAGMA integrity_check 결과(true=정상).
    bool CheckIntegrity();
}

public interface IGroupRepository
{
    int Create(Group group);
    void Update(Group group);
    void Delete(int id);                          // notes.group_id ON DELETE SET NULL
    Group? Get(int id);
    IReadOnlyList<Group> GetAll();                // 시스템 그룹 포함, SortOrder 정렬
}

public interface INoteRepository
{
    int Create(Note note);                        // created_at/updated_at 채움, Id 반환
    void Update(Note note);                       // 전달된 Note 그대로 저장(updated_at 갱신은 호출자 정책)
    void SoftDelete(int id);                       // deleted_at 설정
    void Restore(int id);                          // deleted_at = null
    void Purge(int id);                            // 영구삭제(checklist_items CASCADE)
    void PurgeExpiredTrash(int retentionDays);     // deleted_at 경과분 영구삭제
    Note? Get(int id);
    IReadOnlyList<Note> GetByGroup(int? groupId);  // 활성(deleted_at IS NULL), groupId=null → 미분류
    IReadOnlyList<Note> GetTrash();                // deleted_at NOT NULL
    IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday);  // type=checklist, log_date 범위, 활성
    Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format);        // 멱등 재사용용
}

public interface IChecklistRepository
{
    int AddItem(ChecklistItem item);
    void UpdateItem(ChecklistItem item);
    void DeleteItem(int id);
    IReadOnlyList<ChecklistItem> GetByNote(int noteId);  // SortOrder 정렬
}

public interface IClientRepository
{
    int Create(Client client);
    void Update(Client client);
    void Delete(int id);                          // checklist_items.client_id ON DELETE SET NULL
    IReadOnlyList<Client> GetAll(bool enabledOnly = false);  // SortOrder 정렬
    IReadOnlyList<ClientRule> GetRules();         // 전체 규칙
    void ReplaceRules(int clientId, IEnumerable<ClientRule> rules);
}

public interface ISettingsRepository
{
    string? Get(string key);
    string GetOrDefault(string key, string fallback);
    void Set(string key, string value);
    IReadOnlyDictionary<string, string> GetAll();
}

public sealed record SearchHit(int NoteId, string TitlePreview, string Snippet);

public interface ISearchService
{
    /// FTS5로 title+body+items 검색. 빈 쿼리는 빈 결과.
    IReadOnlyList<SearchHit> Search(string query);
}
```

---

## 5. 오케스트레이션 서비스 — `Memoria.Core.Services`

```csharp
namespace Memoria.Core.Services;
using Memoria.Core.Models;
using Memoria.Core.Reporting;

/// task 텍스트 변경 시 자동 분류를 적용(수동 교정 항목은 보호).
public interface ITaggingService
{
    /// item이 Task이고 IsManual=false이면 현재 규칙으로 ClientId 재계산하여 반환(변경된 item).
    /// Issue이거나 IsManual=true이면 그대로 반환.
    ChecklistItem ApplyAutoTag(ChecklistItem item);
}

public sealed record WeeklyReportBuildResult(
    WeeklyReportData Data,
    int UnclassifiedTaskCount,
    DateOnly Monday,
    DateOnly Friday);

public interface IWeeklyReportService
{
    /// 주간 데이터 수집 + auto 항목 재분류 + 미분류 카운트.
    WeeklyReportBuildResult Build(DateOnly anyDateInWeek, ReportRenderOptions options);
    /// 렌더(IWeeklyReportRenderer 위임).
    string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options);
}
```

---

## 6. 설정 키 (정식 목록) — `Memoria.Core` 상수

```csharp
namespace Memoria.Core;
public static class SettingsKeys
{
    public const string ThemeMode = "theme.mode";                 // light|dark|system  (기본 system)
    public const string ThemePreset = "theme.preset";             // default|dark|sepia|solarized (기본 default)
    public const string ThemeAccent = "theme.accent";             // #RRGGBB
    public const string ReporterName = "report.reporterName";     // 기본 이승현
    public const string FormatATaskHeader = "report.formatA.taskHeader";   // 기본 [업무 내용]
    public const string FormatAIssueHeader = "report.formatA.issueHeader"; // 기본 [이슈]
    public const string FormatBTitleWord = "report.formatB.titleWord";     // 기본 주간 보고
    public const string FormatBIssueHeader = "report.formatB.issueHeader"; // 기본 * 이슈사항:
    public const string ReportIndent = "report.indent";           // 기본 "\t"
    public const string IncludeDoneOnly = "report.includeDoneOnly";  // 기본 false
    public const string HotkeyNewNote = "hotkey.newNote";         // 기본 Ctrl+Alt+N
    public const string Autostart = "app.autostart";              // 기본 true
    public const string CloseToTray = "app.closeToTray";          // 기본 true
    public const string BackupRetentionCount = "backup.retentionCount";  // 기본 7
    public const string TrashRetentionDays = "trash.retentionDays";      // 기본 30
    public const string AutosaveDebounceMs = "autosave.debounceMs";      // 기본 500
}
```

---

## 7. 빌드/테스트 명령 규약 (모든 계획 공통)

- 빌드/테스트는 **Windows .NET 9 SDK(`dotnet.exe`)** 로 수행한다(WPF는 Linux dotnet 불가).
- WSL에서 호출 시 **Windows 절대경로** 사용. 저장소 Windows 경로:
  `C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria`
- 표준 명령:
  - 솔루션 빌드: `dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"`
  - 테스트: `dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests"`
  - 단일 테스트: `dotnet.exe test ... --filter "FullyQualifiedName~ClassName.TestName"`
- `Memoria.Core`는 `net9.0`, `Memoria.Tests`는 `net9.0-windows`. 자동 테스트는 Windows 툴체인(`dotnet.exe`)에서 정상 동작.
- **UI/Win32(M2~M9)** 의 시각·전역동작은 자동 테스트 불가 → 각 계획에 **수동 검증 체크포인트**를 둔다. 단, 로직은 ViewModel/서비스로 분리해 자동 테스트한다(code-behind는 얇게).

---

## 8. 백업/무결성 — `Memoria.Core.Data` (추가)

```csharp
namespace Memoria.Core.Data;

public interface IBackupService
{
    /// 마지막 백업이 오늘이 아니면 backups/memoria-yyyyMMdd.db 로 일관 스냅샷(VACUUM INTO 또는 BackupDatabase) 생성 후
    /// retentionCount 개만 남기고 오래된 것 삭제. 백업했으면 true.
    bool BackupIfDue(int retentionCount);
    /// PRAGMA integrity_check == 'ok' 이면 true.
    bool IsDatabaseHealthy();
    /// 손상 시: 현재 DB를 *.corrupt 로 격리 후 최신 정상 백업을 복원. 복원 성공 시 true(복원본 없으면 false).
    bool TryRestoreFromLatestBackup();
}
```
- `SqliteConnectionFactory`는 **단일 영속 쓰기 연결 + `object WriteSync` 락**을 노출한다. 모든 쓰기는 `lock (factory.WriteSync)` 안에서 수행(직렬 라이터, §7.7). 읽기는 WAL 동시읽기 허용. 백업도 동일 락 하에 수행.

---

## 9. App 합성/부트스트랩 — DI·서비스 로케이터·명령명 (단일 진리원천)

### 9.1 Core DI 등록 (M1이 Produces)
```csharp
namespace Memoria.Core;
public static class CoreServiceRegistration
{
    /// SqliteConnectionFactory(databaseFilePath) + 모든 Repository/Service/Renderer/Classifier/
    /// WeekCalculator/TaggingService/WeeklyReportService/SearchService/IBackupService/IDatabaseInitializer 등록.
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddMemoriaCore(
        this Microsoft.Extensions.DependencyInjection.IServiceCollection services, string databaseFilePath);
}
```

### 9.2 App 서비스 로케이터 (M2가 Produces)
```csharp
namespace Memoria.App;
public static class AppServices
{
    public static System.IServiceProvider Provider { get; }
    public static T Resolve<T>() where T : notnull;     // Provider.GetRequiredService<T>()
    internal static void Initialize(System.IServiceProvider provider);
}
```

### 9.3 MainViewModel 정식 명령/멤버 (M2가 Produces, 이후가 Consumes)
- `public MainViewModel ViewModel => (MainViewModel)DataContext;` ← MainWindow 공개 접근자.
- 명령(모두 `[RelayCommand]`로 생성, 접미사 `Command`):
  - `NewPlainNoteCommand` (메서드 `NewPlainNote`)  ← **`NewNoteCommand` 아님**
  - `NewChecklistCommand` (M9에서 본문 채움; M2는 스텁 명령으로 먼저 선언)
  - `OpenWeeklyReportCommand` (M9에서 채움; M2 스텁)
  - `OpenSettingsCommand` (M2 스텁으로 선언 → M7이 본문 채움; M6는 이 스텁을 안전하게 Consumes)
  - `SearchCommand` + `string SearchText` + `ObservableCollection<SearchHit> SearchResults` + `OpenSearchHitCommand(SearchHit)` (M9에서 채움; M2 스텁)
- `SelectedNote`(NoteListItemViewModel?) 및 현재 편집 NoteType 노출 → M9 뷰 호스팅이 사용.

### 9.4 App.xaml.cs 부트스트랩 순서 (누적 패치 — 각 마일스톤은 기존 호출 보존 + 자기 배선만 추가)
```
OnStartup:
  1) AppPaths.EnsureDirectories()                         (M2)
  2) SingleInstance: 두 번째면 pipe 전송 후 Shutdown      (M6)
  3) services.AddMemoriaCore(AppPaths.DatabaseFile) + App 서비스 등록 → AppServices.Initialize  (M2; M6/M7 항목 추가)
  4) IDatabaseInitializer.EnsureReady()                   (M2)
  5) if(!IBackupService.IsDatabaseHealthy()) TryRestoreFromLatestBackup() + 사용자 확인  (M9)
  6) IBackupService.BackupIfDue(retentionCount)           (M9)
  7) IThemeService.Initialize()                           (M7)
  8) RecoveryJournal 적용(있으면 복구 다이얼로그)         (M2)
  9) INoteRepository.PurgeExpiredTrash(trashRetentionDays) (M5)
 10) MainWindow 생성 + TrayService.Start + GlobalHotkeyService.Register + SystemThemeSource 구독 (M6/M7)
 11) closeToTray/autostart 정책에 따라 Show 또는 트레이로 시작
OnExit:
  - IAutosaveService.FlushAll()  (M2)
  - SqliteConnectionFactory: PRAGMA wal_checkpoint(TRUNCATE) 후 Dispose  (M2/M9)
  - Tray/Hotkey/Pipe Dispose
```

---

## 10. WPF 테마 브러시 키 (단일 진리원천 — 모든 View는 이 키만 DynamicResource로 사용)

M7 팔레트(light/dark/sepia/solarized 등)는 **아래 키를 모두 정의**하고, M2/M3/M4/M5/M9 View는 **아래 키만** 참조한다(StaticResource 금지).

| 키 | 용도 |
|---|---|
| `Brush.WindowBackground` | 창 배경 |
| `Brush.Surface` | 카드/패널 표면 |
| `Brush.SidebarBackground` | 사이드바 배경 |
| `Brush.ToolbarBackground` | 상단 툴바 배경 |
| `Brush.EditorBackground` | 에디터 본문 배경 |
| `Brush.Foreground` | 기본 전경(텍스트) |
| `Brush.SecondaryForeground` | 보조/흐린 텍스트(날짜 등) |
| `Brush.Border` | 경계선 |
| `Brush.ListItemHover` | 목록 항목 hover |
| `Brush.ListItemSelected` | 목록 항목 선택 |
| `Brush.Accent` | 강조색(버튼/선택 강조) |
| `Brush.AccentForeground` | 강조 위 텍스트 |
| `Brush.StrikethroughForeground` | 완료(취소선) 텍스트 |
| `Brush.UnclassifiedHighlight` | 미분류 항목 강조 |
| `Brush.WarningBackground` | 경고 배너 배경 |
| `Brush.WarningBorder` | 경고 배너 경계 |
| `Brush.WarningForeground` | 경고 배너 텍스트 |

---

## 11. 마일스톤 목록 (M9 추가)

- M1 Core 엔진 / M2 WPF 셸+에디터+자동저장 / M3 체크리스트 / M4 주간보고 뷰 / M5 그룹·휴지통 / M6 Windows 통합 / M7 테마·설정 / M8 문서·CI·릴리스
- **M9 — 셸 통합 & 데이터 안전 배선**(capstone): MainWindow의 NoteType별 View 호스팅(ContentControl+DataTemplate), 툴바 진입점([+ 체크리스트]/[📋 주간보고]) 명령 본문, **검색 UI**(SearchViewModel/툴바 검색창/결과 패널/이동, ISearchService 소비), 부트스트랩 §9.4 최종 통합(무결성 점검·복원·일일 백업·wal_checkpoint), 백업 서비스 시작 배선. M2~M8 산출물을 소비해 사용자 흐름을 완성.

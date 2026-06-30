# Memoria M9 — Shell Integration & Data-Safety Wiring (Capstone) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** M2~M8이 따로 만든 산출물(plain 에디터·`ChecklistView`·`WeeklyReportView`·`ISearchService`·`IBackupService`)을 MainWindow 한 화면에서 묶어 사용자 흐름을 완성한다. 에디터 영역을 **NoteType별 `ContentControl`+`DataTemplate`** 로 전환하고, 툴바 진입점([+ 체크리스트]/[📋 주간보고]) 명령과 **검색 UI** 본문을 채우며, 부트스트랩(계약 §9.4)에 **무결성 점검·백업 복원·일일 백업·종료 시 wal_checkpoint** 를 배선한다.

**Architecture:** 모든 분기/매핑 로직은 `MainViewModel`(에디터 선택 매핑·검색·노트 이동)과 App 서비스 `StartupSafetyCoordinator`(무결성→복원→백업 due 판정)에 두어 `Memoria.Tests`(net9.0-windows, xUnit + FluentAssertions)에서 자동 테스트한다. 실제 View 호스팅(`ContentControl`+`DataTemplate`)·툴바·검색 패널·부트스트랩 다이얼로그·종료 체크포인트 등 시각/통합 동작은 **수동 검증 체크포인트**로 확인한다. MainViewModel은 하위 에디터 VM을 직접 `new` 하지 않고 DI가 주입하는 `Func<ChecklistViewModel>`/`Func<WeeklyReportViewModel>` 팩토리로 생성한다(테스트 시 페이크 주입).

**Tech Stack:** C# / .NET 9, WPF(net9.0-windows, UseWPF), CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, System.TimeProvider(테스트는 `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider`), xUnit + FluentAssertions.

## Global Constraints
- 런타임: **.NET 9**. TFM: **Core=net9.0, App=net9.0-windows(+`<UseWPF>true</UseWPF>`), Tests=net9.0-windows**.
- 빌드/테스트: **Windows `dotnet.exe` + Windows 절대경로**(WSL에서 호출, WPF는 Linux dotnet 불가).
  - 솔루션 빌드: `dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"`
  - 테스트: `dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests"`
- 의존 방향: `Memoria.App` → `Memoria.Core`(역방향 금지). ViewModel/App 서비스는 WPF 타입에 의존하지 않고 `CommunityToolkit.Mvvm`만 사용한다(코디네이터/검색/매핑 로직은 모두 자동 테스트 가능). code-behind는 얇게 유지.
- 색상: **모든 색/브러시는 `DynamicResource`만 사용(StaticResource 금지)**. M9가 새로 추가하는 모든 View 마크업은 **계약 §10의 정식 브러시 키만** 참조한다: `Brush.WindowBackground`, `Brush.Surface`, `Brush.SidebarBackground`, `Brush.ToolbarBackground`, `Brush.EditorBackground`, `Brush.Foreground`, `Brush.SecondaryForeground`, `Brush.Border`, `Brush.ListItemHover`, `Brush.ListItemSelected`, `Brush.Accent`, `Brush.AccentForeground`, `Brush.StrikethroughForeground`, `Brush.UnclassifiedHighlight`, `Brush.WarningBackground`, `Brush.WarningBorder`, `Brush.WarningForeground`. (이 사전은 M7이 정의/교체한다.)
- 명령명(계약 §9.3, 단일 진리원천): `NewPlainNoteCommand`(M2), `NewChecklistCommand`, `OpenWeeklyReportCommand`, `OpenSettingsCommand`(M7 본문), `SearchCommand` + `string SearchText` + `ObservableCollection<SearchHit> SearchResults` + `OpenSearchHitCommand(SearchHit)`. **M9는 `NewChecklistCommand`/`OpenWeeklyReportCommand`/`SearchCommand`/`OpenSearchHitCommand` 의 본문을 채운다.**
- 백업/무결성(계약 §8): `IBackupService.{BackupIfDue(int), IsDatabaseHealthy(), TryRestoreFromLatestBackup()}` 와 `IDatabaseInitializer.CheckIntegrity()` 는 **M1 산출물**이며 M9는 소비만 한다.
- 시스템 그룹명(설계 §5.2/§5.3): 일일업무일지 = `"일일업무일지"`(= `Memoria.App.ViewModels.ChecklistViewModel.DailyLogGroupName`), 주간보고 = `"주간보고"`(주간보고 노트 생성은 `WeeklyReportViewModel` 책임).
- **NO PLACEHOLDERS**: 미사용 인터페이스 멤버를 페이크로 구현할 때도 `NotSupportedException`을 던지는 실코드로 둔다(주석 stub 금지).

### 소비(Consumes) 가정 — 선행 마일스톤 산출물
이 마일스톤은 다음이 이미 존재한다고 가정한다(계약 §1·§4·§5·§8·§9):
- **M1**: `Memoria.Core.Data.{ISearchService(+SearchHit), IBackupService, IDatabaseInitializer, SqliteConnectionFactory, INoteRepository, IGroupRepository, ISettingsRepository}`; `Memoria.Core.SettingsKeys.BackupRetentionCount`; DI 합성 진입점 `Microsoft.Extensions.DependencyInjection.IServiceCollection.AddMemoriaCore(string databaseFilePath)`(계약 §9.1). `AddMemoriaCore`가 `ISearchService`/`IBackupService`를 등록한다고 가정한다. **만약 M1이 `IBackupService`(또는 `AddMemoriaCore`)를 노출/등록하지 않았다면**, 계약 §8/§9.1이 단일 진리원천이므로 그 시그니처대로 concrete를 `Memoria.Core.Data`에 추가하고 `AddMemoriaCore`에 등록한 뒤 진행한다(인터페이스 이름은 변경 금지). 이 보강은 본 마일스톤 Task 5의 DI 배선에서 처리한다.
- **M2**: `Memoria.App.AppServices`, `Memoria.App.AppPaths`, `Memoria.App.ViewModels.{MainViewModel, SidebarNodeViewModel, SidebarNodeKind, NoteListItemViewModel}`, `Memoria.App.Services.{IAutosaveService, DebounceAutosaveService, IRecoveryJournal, RecoveryJournal}`, App.xaml.cs DI 컴포지션 루트(계약 §9.4 step1/3/4/8 배선). `MainViewModel`은 이미 `SidebarNodes`/`SelectedNode`/`Notes`/`LoadGroups()`/`LoadNotes()`/`OpenNote(int)`/`EditorTitle`/`EditorBody`/`HeaderText`/`IsEditorVisible`/`NewPlainNoteCommand`를 가진다.
  - **M2가 스텁으로 선언해 두는 멤버/명령(계약 §9.3) — M9는 이를 CONSUMES(재선언 금지, 본문만 채움)**: `MainViewModel.SelectedNote`(`NoteListItemViewModel?`), `MainViewModel.CurrentNoteType`(`NoteType`), `MainViewModel.SearchText`(`string`), `MainViewModel.SearchResults`(`ObservableCollection<SearchHit>`), 부분 메서드 `OnSelectedNoteChanged`(M2 본문은 OpenNote 호출), 그리고 스텁 명령 `NewChecklistCommand`/`OpenWeeklyReportCommand`/`SearchCommand`/`OpenSearchHitCommand`. **M9는 이들 본문(빈 스텁)을 채운다.** M9가 새로 추가(PRODUCES)하는 멤버는 `CurrentEditor` 필드, 에디터 팩토리 필드(`_search`/`_checklistEditorFactory`/`_weeklyReportEditorFactory`), 확장 생성자, `BuildEditorFor`, `NavigateToNote` 뿐이다(이들만 "추가"이고 위 §9.3 멤버는 재선언하면 CS0102/CS0111/CS8793).
- **M3**: `Memoria.App.ViewModels.ChecklistViewModel`(생성자 `(IChecklistRepository, IClientRepository, ITaggingService, INoteRepository, IGroupRepository)`, 메서드 `Load(Note)`, 상수 `DailyLogGroupName`), `Memoria.App.Views.ChecklistView`(UserControl).
- **M4**: `Memoria.App.ViewModels.WeeklyReportViewModel`(생성자 `(IWeeklyReportService, IWeekCalculator, INoteRepository, IClientRepository, IGroupRepository, ISettingsRepository, IClipboardService, IConfirmationDialogService, TimeProvider)`, observable `SelectedDate`/`SelectedFormat`, `GenerateCommand`), `Memoria.App.Views.WeeklyReportView`(UserControl), `Memoria.App.Services.{IClipboardService, WpfClipboardService, IConfirmationDialogService, MessageBoxConfirmationDialogService}`.

> **생성자 확장 주의(누적 코드베이스):** M3~M8에서 `MainViewModel` 생성자가 이미 늘어났을 수 있다. M9는 **기존 파라미터를 모두 보존**하고 본 계획의 신규 파라미터(`ISearchService`, `Func<ChecklistViewModel>`, `Func<WeeklyReportViewModel>`)만 **뒤에 추가**한다. 본 계획의 테스트 `Build()` 헬퍼/기존 모든 `new MainViewModel(...)` 호출부는 현재 시그니처에 맞춰 갱신한다(M2가 확립한 호출부 갱신 규약과 동일).

---

### Task 1: 에디터 호스트 선택 매핑 (CurrentEditor + SelectedNote 라우팅)
**Files:**
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs`
- Test: `tests/Memoria.Tests/App/MainViewModelEditorHostTests.cs`
- Create(테스트 전용 페이크): `tests/Memoria.Tests/App/Fakes/M9EditorFakes.cs`

**Interfaces:**
- Consumes: `INoteRepository.Get(int) -> Note?`; `NoteType.{Plain, Checklist, WeeklyReport}`; `ChecklistViewModel.Load(Note)`; `WeeklyReportViewModel.{SelectedDate, SelectedFormat, GenerateCommand}`; `Note.{Type, ReportWeekStart, ReportFormat}`.
- Produces: M9 신규 `MainViewModel.CurrentEditor(object?)` + 확장 생성자(`ISearchService`, `Func<ChecklistViewModel>`, `Func<WeeklyReportViewModel>`) + `BuildEditorFor`. **M2 스텁 `SelectedNote(NoteListItemViewModel?)`/`CurrentNoteType(NoteType)`/`OnSelectedNoteChanged` 의 본문을 채움(재선언 금지).** NoteType→View 매핑 진리원천.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/Fakes/M9EditorFakes.cs` — 하위 에디터 VM 생성에 필요한 최소 페이크(미사용 멤버는 `NotSupportedException`):
```csharp
using System;
using System.Collections.Generic;
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;

namespace Memoria.Tests.App.Fakes;

// ChecklistViewModel 생성/Load 용 최소 페이크.
internal sealed class FakeChecklistRepo : IChecklistRepository
{
    public List<ChecklistItem> Items { get; } = new();
    public int AddItem(ChecklistItem item) { item.Id = Items.Count + 1; Items.Add(item); return item.Id; }
    public void UpdateItem(ChecklistItem item) { }
    public void DeleteItem(int id) => Items.RemoveAll(i => i.Id == id);
    public IReadOnlyList<ChecklistItem> GetByNote(int noteId) =>
        Items.FindAll(i => i.NoteId == noteId);
}

internal sealed class FakeClientRepo : IClientRepository
{
    public int Create(Client client) => throw new NotSupportedException();
    public void Update(Client client) => throw new NotSupportedException();
    public void Delete(int id) => throw new NotSupportedException();
    public IReadOnlyList<Client> GetAll(bool enabledOnly = false) => new List<Client>();
    public IReadOnlyList<ClientRule> GetRules() => new List<ClientRule>();
    public void ReplaceRules(int clientId, IEnumerable<ClientRule> rules) => throw new NotSupportedException();
}

internal sealed class FakeTagging : ITaggingService
{
    public ChecklistItem ApplyAutoTag(ChecklistItem item) => item;
}

internal sealed class FakeWeekCalc : IWeekCalculator
{
    public (DateOnly Monday, DateOnly Friday) GetWorkWeek(DateOnly anyDate)
    {
        int delta = ((int)anyDate.DayOfWeek + 6) % 7; // Monday=0
        var monday = anyDate.AddDays(-delta);
        return (monday, monday.AddDays(4));
    }
}

internal sealed class FakeWeeklyReportService : IWeeklyReportService
{
    public WeeklyReportBuildResult Build(DateOnly anyDateInWeek, ReportRenderOptions options) =>
        new(new WeeklyReportData(new List<ReportTask>(), new List<ReportIssue>()), 0,
            options.WeekStart, options.WeekEnd);
    public string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options) => "";
}

internal sealed class FakeClipboard : Memoria.App.Services.IClipboardService
{
    public void SetText(string text) { }
}

internal sealed class FakeConfirm : Memoria.App.Services.IConfirmationDialogService
{
    public bool Confirm(string message) => true;
}

internal sealed class FakeSettings : ISettingsRepository
{
    public string? Get(string key) => null;
    public string GetOrDefault(string key, string fallback) => fallback;
    public void Set(string key, string value) { }
    public IReadOnlyDictionary<string, string> GetAll() => new Dictionary<string, string>();
}

internal sealed class FakeSearchService : ISearchService
{
    public List<SearchHit> Result { get; } = new();
    public string? LastQuery { get; private set; }
    public IReadOnlyList<SearchHit> Search(string query) { LastQuery = query; return Result; }
}
```

`tests/Memoria.Tests/App/MainViewModelEditorHostTests.cs`:
```csharp
using System;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelEditorHostTests
{
    // M9 신규 파라미터를 포함해 MainViewModel을 구성한다.
    // (M3~M8에서 생성자가 더 늘었다면, 기존 파라미터를 보존한 현재 시그니처에 맞춰 이 헬퍼를 갱신한다.)
    internal static (MainViewModel vm, FakeNoteRepository notes, FakeGroupRepository groups, FakeSearchService search)
        Build()
    {
        var groups = new FakeGroupRepository();
        var notes = new FakeNoteRepository();
        var search = new FakeSearchService();
        var time = new FakeTimeProvider();
        var autosave = new Memoria.App.Services.DebounceAutosaveService(time, 500);
        var recovery = new FakeRecoveryJournal();

        Func<ChecklistViewModel> checklistFactory = () =>
            new ChecklistViewModel(new FakeChecklistRepo(), new FakeClientRepo(),
                new FakeTagging(), notes, groups);
        Func<WeeklyReportViewModel> weeklyFactory = () =>
            new WeeklyReportViewModel(new FakeWeeklyReportService(), new FakeWeekCalc(), notes,
                new FakeClientRepo(), groups, new FakeSettings(), new FakeClipboard(),
                new FakeConfirm(), time);

        var vm = new MainViewModel(groups, notes, autosave, recovery, time,
            search, checklistFactory, weeklyFactory);
        return (vm, notes, groups, search);
    }

    private static NoteListItemViewModel Item(int id) =>
        new NoteListItemViewModel(id, "t", false, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Selecting_plain_note_hosts_main_view_model_as_editor()
    {
        var (vm, notes, _, _) = Build();
        notes.Create(new Note { Type = NoteType.Plain, Body = "b",
            CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });

        vm.SelectedNote = Item(1);

        vm.CurrentNoteType.Should().Be(NoteType.Plain);
        vm.CurrentEditor.Should().BeSameAs(vm);     // plain 템플릿은 MainViewModel 자신에 바인딩
        vm.IsEditorVisible.Should().BeTrue();
    }

    [Fact]
    public void Selecting_checklist_note_hosts_checklist_view_model()
    {
        var (vm, notes, _, _) = Build();
        notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 26),
            CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });

        vm.SelectedNote = Item(1);

        vm.CurrentNoteType.Should().Be(NoteType.Checklist);
        vm.CurrentEditor.Should().BeOfType<ChecklistViewModel>();
    }

    [Fact]
    public void Selecting_weekly_report_note_hosts_weekly_report_view_model()
    {
        var (vm, notes, _, _) = Build();
        notes.Create(new Note { Type = NoteType.WeeklyReport, ReportFormat = ReportFormatKind.B,
            ReportWeekStart = new DateOnly(2026, 6, 22),
            CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });

        vm.SelectedNote = Item(1);

        vm.CurrentNoteType.Should().Be(NoteType.WeeklyReport);
        vm.CurrentEditor.Should().BeOfType<WeeklyReportViewModel>();
    }

    [Fact]
    public void Clearing_selection_clears_editor()
    {
        var (vm, notes, _, _) = Build();
        notes.Create(new Note { Type = NoteType.Plain, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });
        vm.SelectedNote = Item(1);

        vm.SelectedNote = null;

        vm.CurrentEditor.Should().BeNull();
        vm.IsEditorVisible.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelEditorHostTests"
```
예상 실패: `MainViewModel` 생성자가 아직 M9 신규 파라미터(`ISearchService`/`Func<ChecklistViewModel>`/`Func<WeeklyReportViewModel>`)를 받지 않아 인자 불일치(`CS1729`), 그리고 신규 멤버 `CurrentEditor` 미존재(`CS1061`). `SelectedNote`/`CurrentNoteType`는 M2 스텁으로 **이미 존재**하지만 선택해도 에디터 호스팅이 일어나지 않으므로(M2 `OnSelectedNoteChanged` 스텁이 OpenNote만 호출) `CurrentEditor`/`IsEditorVisible` 동작 어서션이 실패한다.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.App/ViewModels/MainViewModel.cs` 수정 — using/신규 필드/확장 생성자 추가 + M2 스텁 `OnSelectedNoteChanged` 본문 교체(신규 partial 메서드를 또 선언하지 말 것):
```csharp
// 상단 using 추가:
using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Memoria.Core.Models;
```
```csharp
// 신규 필드만 추가 — `selectedNote`(→SelectedNote)·`currentNoteType`(→CurrentNoteType)는
// M2가 이미 [ObservableProperty]로 선언했으므로 여기서 재선언하면 CS0102(중복 필드). 추가 금지.
    private readonly ISearchService _search;
    private readonly Func<ChecklistViewModel> _checklistEditorFactory;
    private readonly Func<WeeklyReportViewModel> _weeklyReportEditorFactory;

    [ObservableProperty] private object? currentEditor;   // 신규: 현재 호스팅 중인 에디터 VM
```
```csharp
// 생성자 교체(기존 파라미터 보존 + M9 신규 파라미터 추가):
    public MainViewModel(
        IGroupRepository groupRepo,
        INoteRepository noteRepo,
        IAutosaveService autosave,
        IRecoveryJournal recovery,
        TimeProvider time,
        ISearchService search,
        Func<ChecklistViewModel> checklistEditorFactory,
        Func<WeeklyReportViewModel> weeklyReportEditorFactory)
    {
        _groupRepo = groupRepo;
        _noteRepo = noteRepo;
        _autosave = autosave;
        _recovery = recovery;
        _time = time;
        _search = search;
        _checklistEditorFactory = checklistEditorFactory;
        _weeklyReportEditorFactory = weeklyReportEditorFactory;
    }
```
```csharp
// M2 `OnSelectedNoteChanged` 스텁 **본문을 교체**(M2 본문은 OpenNote만 호출) — 새 partial 메서드를
// 또 선언하면 CS8793(중복 partial 본문). 기존 메서드의 본문을 아래로 채운다. `BuildEditorFor`는 신규 추가.
    partial void OnSelectedNoteChanged(NoteListItemViewModel? value)
    {
        if (value is null)
        {
            CurrentEditor = null;
            IsEditorVisible = false;
            return;
        }

        var note = _noteRepo.Get(value.Id);
        if (note is null) return;

        CurrentNoteType = note.Type;
        CurrentEditor = BuildEditorFor(note);
    }

    // NoteType → 에디터 VM 매핑(계약 §11: plain/checklist/weekly_report → 각 View 호스팅).
    private object? BuildEditorFor(Note note)
    {
        switch (note.Type)
        {
            case NoteType.Plain:
                OpenNote(note.Id);          // 기존 M2 plain 에디터 로직(헤더/본문/IsEditorVisible) 재사용
                return this;                // plain DataTemplate은 MainViewModel 자신에 바인딩
            case NoteType.Checklist:
                var checklist = _checklistEditorFactory();
                checklist.Load(note);
                return checklist;
            case NoteType.WeeklyReport:
                var weekly = _weeklyReportEditorFactory();
                if (note.ReportWeekStart is DateOnly ws) weekly.SelectedDate = ws;
                if (note.ReportFormat is ReportFormatKind fmt) weekly.SelectedFormat = fmt;
                weekly.GenerateCommand.Execute(null);   // 멱등 로드(M4: 기존 body 재사용)
                return weekly;
            default:
                return null;
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelEditorHostTests"
```
예상: `Passed!  - Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(app): route SelectedNote to NoteType-specific editor host (CurrentEditor)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `NewChecklistCommand` 본문 + 노트 이동 헬퍼(NavigateToNote)
**Files:**
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs`
- Test: `tests/Memoria.Tests/App/MainViewModelNewChecklistTests.cs`

**Interfaces:**
- Consumes: `IGroupRepository.GetAll() -> IReadOnlyList<Group>`; `INoteRepository.Create(Note) -> int`; `Group.{Id, Name, IsSystem}`; `NoteType.Checklist`; `ChecklistViewModel.DailyLogGroupName`; `TimeProvider.GetUtcNow()`; `SidebarNodeViewModel.{GroupId, Kind}`; `SidebarNodeKind.Unclassified`.
- Produces: **M2 스텁 `MainViewModel.NewChecklistCommand` 본문 채움** + 신규 `MainViewModel.NavigateToNote(int noteId, int? groupId)`.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/MainViewModelNewChecklistTests.cs`:
```csharp
using System.Linq;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelNewChecklistTests
{
    [Fact]
    public void NewChecklist_creates_checklist_note_in_daily_log_system_group_and_selects_it()
    {
        var (vm, notes, groups, _) = MainViewModelEditorHostTests.Build();
        groups.Items.Add(new Group { Name = ChecklistViewModel.DailyLogGroupName, IsSystem = true, SortOrder = 100 });
        groups.Items[0].Id = 1;          // FakeGroupRepository.Items은 명시 Id로 검증
        vm.LoadGroups();

        vm.NewChecklistCommand.Execute(null);

        notes.Items.Should().ContainSingle();
        var created = notes.Items[0];
        created.Type.Should().Be(NoteType.Checklist);
        created.GroupId.Should().Be(1);                       // 시스템 그룹 '일일업무일지'
        created.LogDate.Should().NotBeNull();                 // 기본 log_date = 오늘

        vm.SelectedNote.Should().NotBeNull();
        vm.SelectedNote!.Id.Should().Be(created.Id);
        vm.CurrentEditor.Should().BeOfType<ChecklistViewModel>();
    }
}
```
> `FakeGroupRepository.Create`가 Id를 `Items.Count + 1`로 매기므로, 위 테스트는 시드를 `Items`에 직접 넣고 `Id`를 명시한다(`GetAll`은 SortOrder 정렬 그대로 반환).

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelNewChecklistTests"
```
예상 실패: M2 `NewChecklistCommand` 스텁 본문이 아직 비어 있어(no-op) 노트가 생성·선택되지 않으므로 `ContainSingle`/`SelectedNote`/`CurrentEditor` 어서션 실패(명령 자체는 M2 스텁으로 이미 존재).

- [ ] **Step 3: Write minimal implementation**

`MainViewModel.cs` — **M2 `NewChecklist` 스텁 본문을 채우고**(새 메서드를 또 선언하면 CS0111) `NavigateToNote` 헬퍼를 신규 추가:
```csharp
    [RelayCommand]
    private void NewChecklist()
    {
        var group = _groupRepo.GetAll()
            .FirstOrDefault(g => g.IsSystem && g.Name == ChecklistViewModel.DailyLogGroupName);

        var now = _time.GetUtcNow();
        var note = new Note
        {
            Type = NoteType.Checklist,
            GroupId = group?.Id,
            LogDate = DateOnly.FromDateTime(now.LocalDateTime.Date),
            CreatedAt = now,
            UpdatedAt = now,
        };
        var id = _noteRepo.Create(note);
        NavigateToNote(id, group?.Id);
    }

    // 사이드바 노드 선택 → 목록 재로드 → 해당 노트 선택(에디터 호스팅 트리거).
    public void NavigateToNote(int noteId, int? groupId)
    {
        var node = SidebarNodes.FirstOrDefault(n => n.GroupId == groupId)
                   ?? SidebarNodes.FirstOrDefault(n => n.Kind == SidebarNodeKind.Unclassified);
        if (node is not null) SelectedNode = node;

        LoadNotes();   // SelectedNode가 이미 동일하면 OnSelectedNodeChanged가 안 울리므로 명시 재로드
        SelectedNote = Notes.FirstOrDefault(n => n.Id == noteId);
    }
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelNewChecklistTests"
```
예상: `Passed!  - Failed: 0, Passed: 1`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(app): implement NewChecklistCommand (create+select daily-log checklist)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: `OpenWeeklyReportCommand` 본문 (주간보고 뷰 열기/생성)
**Files:**
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs`
- Test: `tests/Memoria.Tests/App/MainViewModelWeeklyReportTests.cs`

**Interfaces:**
- Consumes: `Func<WeeklyReportViewModel>`(Task 1 주입); `WeeklyReportViewModel.GenerateCommand`.
- Produces: **M2 스텁 `MainViewModel.OpenWeeklyReportCommand` 본문 채움**(재선언 금지).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/MainViewModelWeeklyReportTests.cs`:
```csharp
using FluentAssertions;
using Memoria.App.ViewModels;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelWeeklyReportTests
{
    [Fact]
    public void OpenWeeklyReport_hosts_a_weekly_report_view_model()
    {
        var (vm, _, _, _) = MainViewModelEditorHostTests.Build();

        vm.OpenWeeklyReportCommand.Execute(null);

        vm.CurrentEditor.Should().BeOfType<WeeklyReportViewModel>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelWeeklyReportTests"
```
예상 실패: M2 `OpenWeeklyReportCommand` 스텁 본문이 아직 비어 있어(no-op) `CurrentEditor`가 `WeeklyReportViewModel`로 설정되지 않아 어서션 실패(명령 자체는 M2 스텁으로 이미 존재).

- [ ] **Step 3: Write minimal implementation**

`MainViewModel.cs` — **M2 `OpenWeeklyReport` 스텁 본문을 채운다**(새 메서드를 또 선언하면 CS0111):
```csharp
    [RelayCommand]
    private void OpenWeeklyReport()
    {
        var weekly = _weeklyReportEditorFactory();   // 기본 = 오늘이 포함된 주(M4 생성자)
        weekly.GenerateCommand.Execute(null);        // 멱등 로드/생성(필요 시 새 주간보고 노트 생성)
        CurrentNoteType = NoteType.WeeklyReport;
        CurrentEditor = weekly;
    }
```
> 툴바 진입점은 특정 노트 선택 없이 주간 뷰를 직접 띄우므로 `SelectedNote`를 건드리지 않고 `CurrentEditor`만 설정한다. 사용자가 사이드바의 기존 주간보고 노트를 클릭하면 Task 1의 `SelectedNote` 경로가 동일 VM을 띄운다.

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelWeeklyReportTests"
```
예상: `Passed!  - Failed: 0, Passed: 1`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(app): implement OpenWeeklyReportCommand (host weekly report editor)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: 검색 — `SearchCommand` / `SearchResults` / `OpenSearchHitCommand`
**Files:**
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs`
- Test: `tests/Memoria.Tests/App/MainViewModelSearchTests.cs`

**Interfaces:**
- Consumes: `ISearchService.Search(string) -> IReadOnlyList<SearchHit>`; `Memoria.Core.Data.SearchHit(int NoteId, string TitlePreview, string Snippet)`; `INoteRepository.Get(int) -> Note?`; Task 2의 `NavigateToNote`.
- Produces: **M2 스텁 `MainViewModel.{SearchCommand, OpenSearchHitCommand(SearchHit)}` 본문 채움** — `SearchText`/`SearchResults(ObservableCollection<SearchHit>)`는 M2 선언을 그대로 소비(재선언 금지).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/MainViewModelSearchTests.cs`:
```csharp
using System;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelSearchTests
{
    [Fact]
    public void Search_with_blank_text_returns_empty_results_without_calling_service()
    {
        var (vm, _, _, search) = MainViewModelEditorHostTests.Build();
        vm.SearchText = "   ";

        vm.SearchCommand.Execute(null);

        vm.SearchResults.Should().BeEmpty();
        search.LastQuery.Should().BeNull();
    }

    [Fact]
    public void Search_populates_results_from_service()
    {
        var (vm, _, _, search) = MainViewModelEditorHostTests.Build();
        search.Result.Add(new SearchHit(5, "제목", "조각"));
        vm.SearchText = "조각";

        vm.SearchCommand.Execute(null);

        search.LastQuery.Should().Be("조각");
        vm.SearchResults.Should().ContainSingle().Which.NoteId.Should().Be(5);
    }

    [Fact]
    public void OpenSearchHit_navigates_to_hit_note()
    {
        var (vm, notes, groups, _) = MainViewModelEditorHostTests.Build();
        groups.Items.Add(new Group { Name = "업무", IsSystem = false, SortOrder = 0 });
        groups.Items[0].Id = 1;
        notes.Create(new Note { Type = NoteType.Plain, GroupId = 1, Body = "b",
            CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });
        vm.LoadGroups();

        vm.OpenSearchHitCommand.Execute(new SearchHit(1, "제목", "조각"));

        vm.SelectedNote.Should().NotBeNull();
        vm.SelectedNote!.Id.Should().Be(1);
        vm.CurrentEditor.Should().BeSameAs(vm);   // plain → MainViewModel 호스팅
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelSearchTests"
```
예상 실패: M2 `SearchCommand`/`OpenSearchHitCommand` 스텁 본문이 아직 비어 있어(no-op) `SearchResults`가 채워지지 않고 hit 이동도 일어나지 않아 어서션 실패(`SearchText`/`SearchResults`/명령 자체는 M2 스텁으로 이미 존재).

- [ ] **Step 3: Write minimal implementation**

`MainViewModel.cs` — `SearchText`(→searchText)·`SearchResults`는 **M2가 이미 선언**했으므로 재선언하지 말고(재선언 시 CS0102), **M2 `Search`/`OpenSearchHit` 스텁 본문만 채운다**(새 메서드를 또 선언하면 CS0111):
```csharp
    // searchText(→SearchText) / SearchResults 는 M2 스텁을 그대로 사용 — 여기서 재선언하지 않는다.

    [RelayCommand]
    private void Search()
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        foreach (var hit in _search.Search(SearchText))
            SearchResults.Add(hit);
    }

    [RelayCommand]
    private void OpenSearchHit(SearchHit hit)    // 계약 §9.3 / M2 시그니처: non-nullable SearchHit
    {
        if (hit is null) return;                 // 내부 null 가드(외부에서 null 전달 방지)
        var note = _noteRepo.Get(hit.NoteId);
        if (note is null) return;
        NavigateToNote(hit.NoteId, note.GroupId);
    }
```
> `SearchHit`/`ISearchService`는 `Memoria.Core.Data`이므로 파일 상단 `using Memoria.Core.Data;`가 이미 있다(M2 기존). 없으면 추가.

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelSearchTests"
```
예상: `Passed!  - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(app): implement search command, results, and open-hit navigation

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: 시작 데이터 안전 코디네이터(StartupSafetyCoordinator)
**Files:**
- Create: `src/Memoria.App/Services/IStartupSafetyCoordinator.cs`, `src/Memoria.App/Services/StartupSafetyCoordinator.cs`
- Test: `tests/Memoria.Tests/App/StartupSafetyCoordinatorTests.cs`, `tests/Memoria.Tests/App/Fakes/FakeBackupService.cs`

**Interfaces:**
- Consumes: `Memoria.Core.Data.IBackupService.{IsDatabaseHealthy(), TryRestoreFromLatestBackup(), BackupIfDue(int)}`(계약 §8).
- Produces: `Memoria.App.Services.StartupSafetyOutcome(bool DatabaseWasHealthy, bool RestoreAttempted, bool RestoreSucceeded, bool BackupCreated)`, `IStartupSafetyCoordinator.Run(int retentionCount) -> StartupSafetyOutcome`. 무결성→복원→백업 due 판정 순서의 진리원천(계약 §9.4 step5/6의 순수 분기).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/Fakes/FakeBackupService.cs`:
```csharp
using Memoria.Core.Data;

namespace Memoria.Tests.App.Fakes;

internal sealed class FakeBackupService : IBackupService
{
    public bool Healthy { get; set; } = true;
    public bool RestoreSucceeds { get; set; }
    public bool BackupReturns { get; set; } = true;

    public int RestoreCalls { get; private set; }
    public int BackupCalls { get; private set; }
    public int? LastRetentionCount { get; private set; }

    public bool IsDatabaseHealthy() => Healthy;
    public bool TryRestoreFromLatestBackup() { RestoreCalls++; return RestoreSucceeds; }
    public bool BackupIfDue(int retentionCount)
    {
        BackupCalls++;
        LastRetentionCount = retentionCount;
        return BackupReturns;
    }
}
```

`tests/Memoria.Tests/App/StartupSafetyCoordinatorTests.cs`:
```csharp
using FluentAssertions;
using Memoria.App.Services;
using Memoria.Tests.App.Fakes;
using Xunit;

namespace Memoria.Tests.App;

public class StartupSafetyCoordinatorTests
{
    [Fact]
    public void Healthy_db_skips_restore_and_runs_backup()
    {
        var backup = new FakeBackupService { Healthy = true };
        var outcome = new StartupSafetyCoordinator(backup).Run(7);

        backup.RestoreCalls.Should().Be(0);
        backup.BackupCalls.Should().Be(1);
        backup.LastRetentionCount.Should().Be(7);
        outcome.DatabaseWasHealthy.Should().BeTrue();
        outcome.RestoreAttempted.Should().BeFalse();
    }

    [Fact]
    public void Unhealthy_db_restored_then_backs_up()
    {
        var backup = new FakeBackupService { Healthy = false, RestoreSucceeds = true };
        var outcome = new StartupSafetyCoordinator(backup).Run(7);

        backup.RestoreCalls.Should().Be(1);
        backup.BackupCalls.Should().Be(1);
        outcome.RestoreAttempted.Should().BeTrue();
        outcome.RestoreSucceeded.Should().BeTrue();
    }

    [Fact]
    public void Unhealthy_db_with_failed_restore_does_not_back_up()
    {
        var backup = new FakeBackupService { Healthy = false, RestoreSucceeds = false };
        var outcome = new StartupSafetyCoordinator(backup).Run(7);

        backup.RestoreCalls.Should().Be(1);
        backup.BackupCalls.Should().Be(0);          // 복구 실패 시 손상 DB를 백업하지 않음
        outcome.RestoreSucceeded.Should().BeFalse();
        outcome.BackupCreated.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~StartupSafetyCoordinatorTests"
```
예상 실패: `StartupSafetyCoordinator`/`StartupSafetyOutcome` 미존재(`CS0246`).

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.App/Services/IStartupSafetyCoordinator.cs`:
```csharp
namespace Memoria.App.Services;

/// 계약 §9.4 step5/6: 무결성 점검 → (손상 시) 백업 복원 → (정상/복원 성공 시) 일일 백업.
public sealed record StartupSafetyOutcome(
    bool DatabaseWasHealthy,
    bool RestoreAttempted,
    bool RestoreSucceeded,
    bool BackupCreated);

public interface IStartupSafetyCoordinator
{
    StartupSafetyOutcome Run(int retentionCount);
}
```

`src/Memoria.App/Services/StartupSafetyCoordinator.cs`:
```csharp
using Memoria.Core.Data;

namespace Memoria.App.Services;

public sealed class StartupSafetyCoordinator : IStartupSafetyCoordinator
{
    private readonly IBackupService _backup;

    public StartupSafetyCoordinator(IBackupService backup) => _backup = backup;

    public StartupSafetyOutcome Run(int retentionCount)
    {
        var healthy = _backup.IsDatabaseHealthy();

        var restoreAttempted = false;
        var restoreSucceeded = false;
        if (!healthy)
        {
            restoreAttempted = true;
            restoreSucceeded = _backup.TryRestoreFromLatestBackup();
        }

        var backupCreated = false;
        if (healthy || restoreSucceeded)
            backupCreated = _backup.BackupIfDue(retentionCount);

        return new StartupSafetyOutcome(healthy, restoreAttempted, restoreSucceeded, backupCreated);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~StartupSafetyCoordinatorTests"
```
예상: `Passed!  - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(app): add StartupSafetyCoordinator (integrity/restore/daily-backup decision)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: 셸 통합 — MainWindow 호스팅·툴바·검색 UI + 부트스트랩 §9.4 배선 (수동 검증)
**Files:**
- Modify: `src/Memoria.App/MainWindow.xaml`, `src/Memoria.App/MainWindow.xaml.cs`, `src/Memoria.App/App.xaml.cs`
- Test: (자동 테스트 없음 — UI 호스팅/창/부트스트랩 다이얼로그/종료 체크포인트는 수동 검증. 로직은 Task 1~5에서 자동 테스트 완료.)

**Interfaces:**
- Consumes: `MainViewModel.{NewPlainNoteCommand, NewChecklistCommand, OpenWeeklyReportCommand, SearchText, SearchCommand, SearchResults, OpenSearchHitCommand, SidebarNodes, SelectedNode, Notes, SelectedNote, CurrentEditor, IsEditorVisible, EditorTitle, HeaderText, EditorBody}`; `ChecklistViewModel`/`WeeklyReportViewModel`/`ChecklistView`/`WeeklyReportView`; `IServiceCollection.AddMemoriaCore(string)`; `IDatabaseInitializer.EnsureReady()`; `ISettingsRepository.GetOrDefault(string,string)`; `SettingsKeys.BackupRetentionCount`; `IStartupSafetyCoordinator.Run(int)`; `SqliteConnectionFactory.Dispose()`(종료 시 §8 `WriteSync` 락 하 `wal_checkpoint(TRUNCATE)`; M2의 `_services.Dispose()`가 트리거) — 종료 경로에서 raw `Open()` 연결은 사용하지 않는다.
- Produces: 통합 실행 셸(NoteType별 View 호스팅 + 툴바 진입점 + 검색 패널), 계약 §9.4 step5/6 배선. OnExit 체크포인트는 raw 연결을 추가하지 않고 §8 락 하 `SqliteConnectionFactory.Dispose()`(M2의 `_services.Dispose()`가 트리거)에 위임.

- [ ] **Step 1: MainWindow 호스팅 + 툴바 + 검색 UI(XAML)**

`src/Memoria.App/MainWindow.xaml` — 에디터 컬럼을 `ContentControl`+`DataTemplate`로 교체하고, 툴바에 진입점/검색을 추가한다. **색은 모두 계약 §10 키를 `DynamicResource`로** 참조한다.
```xml
<Window x:Class="Memoria.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:Memoria.App.ViewModels"
        xmlns:views="clr-namespace:Memoria.App.Views"
        Title="Memoria" Height="640" Width="980"
        Background="{DynamicResource Brush.WindowBackground}"
        Foreground="{DynamicResource Brush.Foreground}">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- 상단 툴바 -->
        <DockPanel Grid.Row="0" Background="{DynamicResource Brush.ToolbarBackground}" LastChildFill="True">
            <Button Content="+ 새 메모"   Command="{Binding NewPlainNoteCommand}"      Margin="8,6,4,6" Padding="10,4" />
            <Button Content="+ 체크리스트" Command="{Binding NewChecklistCommand}"      Margin="0,6,4,6" Padding="10,4" />
            <Button Content="📋 주간보고"  Command="{Binding OpenWeeklyReportCommand}"   Margin="0,6,12,6" Padding="10,4" />
            <Button DockPanel.Dock="Right" Content="🔍" Command="{Binding SearchCommand}" Margin="4,6,8,6" Padding="8,4" />
            <TextBox DockPanel.Dock="Right" Width="220" Margin="0,6,0,6" VerticalContentAlignment="Center"
                     Background="{DynamicResource Brush.Surface}"
                     Foreground="{DynamicResource Brush.Foreground}"
                     BorderBrush="{DynamicResource Brush.Border}"
                     Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}">
                <TextBox.InputBindings>
                    <KeyBinding Key="Return" Command="{Binding SearchCommand}" />
                </TextBox.InputBindings>
            </TextBox>
            <TextBlock /> <!-- LastChild filler -->
        </DockPanel>

        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="220" />
                <ColumnDefinition Width="240" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <!-- 사이드바: 그룹 트리 -->
            <ListBox Grid.Column="0"
                     Background="{DynamicResource Brush.SidebarBackground}"
                     Foreground="{DynamicResource Brush.Foreground}"
                     BorderBrush="{DynamicResource Brush.Border}"
                     ItemsSource="{Binding SidebarNodes}"
                     SelectedItem="{Binding SelectedNode, Mode=TwoWay}"
                     DisplayMemberPath="Name" />

            <!-- 메모 목록 + 검색 결과 패널 -->
            <Grid Grid.Column="1">
                <Grid.RowDefinitions>
                    <RowDefinition Height="*" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>
                <ListBox Grid.Row="0"
                         Background="{DynamicResource Brush.Surface}"
                         Foreground="{DynamicResource Brush.Foreground}"
                         BorderBrush="{DynamicResource Brush.Border}"
                         ItemsSource="{Binding Notes}"
                         SelectedItem="{Binding SelectedNote, Mode=TwoWay}"
                         DisplayMemberPath="DisplayTitle" />

                <!-- 검색 결과: 결과가 있을 때만 표시 -->
                <Border Grid.Row="1" Margin="0,4,0,0"
                        Background="{DynamicResource Brush.Surface}"
                        BorderBrush="{DynamicResource Brush.Border}" BorderThickness="1"
                        Visibility="{Binding SearchResults.Count, Converter={StaticResource CountToVisibility}}">
                    <ListBox x:Name="SearchResultsList" MaxHeight="200"
                             ItemsSource="{Binding SearchResults}"
                             Background="Transparent"
                             SelectionChanged="SearchResultsList_SelectionChanged">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <StackPanel Margin="2">
                                    <TextBlock Text="{Binding TitlePreview}" FontWeight="SemiBold"
                                               Foreground="{DynamicResource Brush.Foreground}" />
                                    <TextBlock Text="{Binding Snippet}" TextTrimming="CharacterEllipsis"
                                               Foreground="{DynamicResource Brush.SecondaryForeground}" />
                                </StackPanel>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </Border>
            </Grid>

            <!-- 에디터 영역: NoteType별 DataTemplate -->
            <ContentControl Grid.Column="2" Margin="12" Content="{Binding CurrentEditor}"
                            Background="{DynamicResource Brush.EditorBackground}">
                <ContentControl.Resources>
                    <!-- plain: CurrentEditor == MainViewModel 자신 -->
                    <DataTemplate DataType="{x:Type vm:MainViewModel}">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="*" />
                            </Grid.RowDefinitions>
                            <TextBox Grid.Row="0" Text="{Binding EditorTitle, UpdateSourceTrigger=PropertyChanged}"
                                     FontSize="18" BorderThickness="0"
                                     Background="{DynamicResource Brush.EditorBackground}"
                                     Foreground="{DynamicResource Brush.Foreground}" />
                            <TextBlock Grid.Row="1" Text="{Binding HeaderText}" Margin="0,4,0,8"
                                       Foreground="{DynamicResource Brush.SecondaryForeground}" />
                            <TextBox Grid.Row="2" Text="{Binding EditorBody, UpdateSourceTrigger=PropertyChanged}"
                                     AcceptsReturn="True" AcceptsTab="True" TextWrapping="Wrap"
                                     VerticalScrollBarVisibility="Auto"
                                     Background="{DynamicResource Brush.EditorBackground}"
                                     Foreground="{DynamicResource Brush.Foreground}" />
                        </Grid>
                    </DataTemplate>
                    <!-- checklist -->
                    <DataTemplate DataType="{x:Type vm:ChecklistViewModel}">
                        <views:ChecklistView />
                    </DataTemplate>
                    <!-- weekly_report -->
                    <DataTemplate DataType="{x:Type vm:WeeklyReportViewModel}">
                        <views:WeeklyReportView />
                    </DataTemplate>
                </ContentControl.Resources>
            </ContentControl>
        </Grid>
    </Grid>
</Window>
```
> **변환기:** `CountToVisibility`(int>0 → Visible, else Collapsed)는 색이 아니므로 `StaticResource` 허용. `App.xaml`에 등록한다(없으면 추가):
> ```xml
> <!-- App.xaml <Application.Resources> 안 -->
> <views:CountToVisibilityConverter x:Key="CountToVisibility" />
> ```
> 와 함께 `src/Memoria.App/Converters/CountToVisibilityConverter.cs`(`IValueConverter`, `int>0 ? Visibility.Visible : Visibility.Collapsed`)를 추가하고 `App.xaml`에 `xmlns:views="clr-namespace:Memoria.App.Converters"` 네임스페이스를 매핑한다(또는 기존 컨버터 네임스페이스 재사용). `BooleanToVisibilityConverter`로 대체해도 무방(그 경우 VM에 `bool HasSearchResults` 노출).

- [ ] **Step 2: MainWindow code-behind(얇게) — 검색 결과 선택 → 이동**

`src/Memoria.App/MainWindow.xaml.cs` — M2의 `NotesList_SelectionChanged`는 `SelectedNote` 바인딩으로 대체되어 제거하고, 검색 결과 선택 핸들러만 둔다(코드비하인드는 VM 명령 위임만):
```csharp
using System.Windows;
using System.Windows.Controls;
using Memoria.App.ViewModels;
using Memoria.Core.Data;

namespace Memoria.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && SearchResultsList.SelectedItem is SearchHit hit)
        {
            vm.OpenSearchHitCommand.Execute(hit);
            SearchResultsList.SelectedItem = null;   // 다음 검색을 위해 선택 해제
        }
    }
}
```

- [ ] **Step 3: App.xaml.cs 부트스트랩 §9.4 누적 패치(step5/6 + OnExit wal_checkpoint)**

기존 호출(step1=`EnsureDirectories`, step3=`AddMemoriaCore`+App 등록, step4=`EnsureReady`, step8=복구, 및 M5~M7이 추가한 step2/7/9/10/11)은 **모두 보존**한다. M9는 아래만 추가한다.

(3-1) DI 등록 추가 — `BuildServiceProvider()` 호출 이전, App 서비스 등록부에:
```csharp
// M9: 에디터 호스트 팩토리(NoteType별 View 호스팅용) + 시작 안전 코디네이터.
sc.AddTransient<ChecklistViewModel>();
sc.AddTransient<WeeklyReportViewModel>();
sc.AddSingleton<Func<ChecklistViewModel>>(sp => () => sp.GetRequiredService<ChecklistViewModel>());
sc.AddSingleton<Func<WeeklyReportViewModel>>(sp => () => sp.GetRequiredService<WeeklyReportViewModel>());
sc.AddSingleton<IStartupSafetyCoordinator, StartupSafetyCoordinator>();
```
> `ISearchService`/`IBackupService`는 `AddMemoriaCore`(M1)가 등록한다고 가정한다(소비 가정 참조). `IClipboardService`/`IConfirmationDialogService`는 M4가 등록한다. `MainViewModel`은 위 팩토리·`ISearchService`를 생성자로 주입받는다(M2 등록 그대로 두면 DI가 신규 파라미터를 자동 해결).

(3-2) `OnStartup` — `IDatabaseInitializer.EnsureReady()`(step4) **직후**, 테마 초기화(step7) **이전**에 무결성/백업 배선(step5/6) 추가:
```csharp
// step5 + step6 (계약 §9.4): 무결성 점검 → 손상 시 복원 → 정상/복원 시 일일 백업.
var settings = _services.GetRequiredService<ISettingsRepository>();
var retention = int.Parse(settings.GetOrDefault(SettingsKeys.BackupRetentionCount, "7"));
var safety = _services.GetRequiredService<IStartupSafetyCoordinator>().Run(retention);
if (!safety.DatabaseWasHealthy)
{
    var msg = safety.RestoreSucceeded
        ? "데이터베이스 손상을 감지하여 최근 정상 백업에서 복원했습니다."
        : "데이터베이스 손상을 감지했으나 복원할 백업이 없습니다. 손상 파일은 격리되었습니다.";
    MessageBox.Show(msg, "Memoria 데이터 복구",
        MessageBoxButton.OK,
        safety.RestoreSucceeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
}
```
> 배치 순서 주의: step5/6은 DB 파일이 준비된 뒤(EnsureReady 이후) 수행해야 하며, MainWindow 표시(step10/11)·테마 초기화(step7)보다 앞서야 한다. `IBackupService`는 손상 격리(`*.corrupt`)·복원을 §8대로 자체 수행한다.

(3-3) `OnExit` — **raw 체크포인트 추가 금지**: 체크포인트는 계약 §8의 단일 쓰기 락(`WriteSync`) **안에서만** 수행해야 하므로, M9는 `factory.Open()` + raw `PRAGMA wal_checkpoint`를 추가하지 않는다(락 밖의 별도 쓰기 연결은 직렬 라이터 위반). 대신 M2가 이미 OnExit에서 호출하는 ServiceProvider 폐기(`_services.Dispose()`)에 위임한다 — DI가 싱글턴 `SqliteConnectionFactory`(`IDisposable`)를 폐기하고, `SqliteConnectionFactory.Dispose()`가 §8대로 락 하에 체크포인트한다. 따라서 OnExit에 **M9 추가 코드는 필요 없다**(계약 §9.4 OnExit는 M2/M9 공동; 중복 `Dispose()` 호출 금지):
```csharp
// 참고: SqliteConnectionFactory.Dispose() 내부(M1/M2 책임) — §8 WriteSync 락 하 체크포인트 후 종료.
//   public void Dispose()
//   {
//       lock (WriteSync) { _writeConn.Execute("PRAGMA wal_checkpoint(TRUNCATE);"); _writeConn.Dispose(); }
//   }
// OnExit 경로: IAutosaveService.FlushAll()(M2) → _services.Dispose()(M2; 위 Dispose 트리거) → Tray/Hotkey/Pipe Dispose.
```
> 명시적 체크포인트가 필요하면 raw `Open()` 대신 §8 락으로 보호된 `SqliteConnectionFactory.Checkpoint()`(있으면)를 호출한다. M1의 `SqliteConnectionFactory`가 Dispose 시 체크포인트하지 않거나 `Checkpoint()`를 노출하지 않으면, §8을 단일 진리원천으로 삼아 위 예시처럼 `Dispose()`(또는 락 보호 `Checkpoint()`)에 `lock (WriteSync)` 체크포인트 로직을 추가한다. 어떤 경우에도 종료 경로에서 락 밖의 별도 연결을 열지 않는다.

(3-4) 시작 시 첫 화면이 비어 있지 않도록(선택): `vm.LoadGroups()` 후 `vm.SelectedNode`를 첫 노드로 설정하는 기존 동작은 그대로 둔다. M9는 별도 강제 선택을 추가하지 않는다(설계 §4.1 빈 상태 안내 유지).

- [ ] **Step 4: 빌드 검증**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: 전체 자동 테스트 회귀 검증**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests"
```
예상: 전체 통과(`Failed: 0`). M9 신규: EditorHost 4 + NewChecklist 1 + WeeklyReport 1 + Search 3 + StartupSafety 3 = 12 (+ M1~M8 누적).

- [ ] **Step 6: 수동 검증 체크포인트(Windows 실제 실행)**
```bash
dotnet.exe run --project "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\src\Memoria.App"
```
다음을 눈으로 확인한다:
- [ ] **MV9-1 plain 호스팅**: plain 메모를 선택하면 우측에 제목/헤더/본문 에디터가 뜬다(M2 동작 유지).
- [ ] **MV9-2 checklist 호스팅**: 사이드바 `일일업무일지`의 체크리스트 노트를 선택하면 우측에 `ChecklistView`(할일/이슈 + 체크박스/취소선)가 뜬다.
- [ ] **MV9-3 weekly 호스팅**: 사이드바 `주간보고`의 주간보고 노트를 선택하면 우측에 `WeeklyReportView`(주차/양식 토글/본문/복사)가 뜬다.
- [ ] **MV9-4 [+ 체크리스트]**: 툴바 `+ 체크리스트` 클릭 시 `일일업무일지` 그룹에 오늘자 체크리스트 노트가 생성·선택되고 `ChecklistView`가 열린다.
- [ ] **MV9-5 [📋 주간보고]**: 툴바 `📋 주간보고` 클릭 시 이번 주 주간보고 뷰가 열리고(필요 시 노트 생성), 본문이 생성된다.
- [ ] **MV9-6 검색**: 검색창에 키워드 입력 후 Enter/🔍 → 결과 패널에 `제목/스니펫`이 뜨고(노트 제목·본문·체크리스트 항목 대상), 결과 클릭 시 해당 노트로 이동하며 알맞은 View가 호스팅된다.
- [ ] **MV9-7 빈 검색**: 빈/공백 검색은 결과가 없고 패널이 사라진다.
- [ ] **MV9-8 일일 백업**: 정상 실행 후 `%LOCALAPPDATA%\Memoria\backups\memoria-yyyyMMdd.db`가 생성되고, 같은 날 재실행 시 중복 생성되지 않는다(`retentionCount` 초과분은 삭제).
- [ ] **MV9-9 무결성 복원**: (테스트) `memoria.db`를 의도적으로 손상시킨 뒤 실행 → "손상 감지/복원" 다이얼로그가 뜨고, 백업이 있으면 복원되어 정상 기동한다. 손상 원본은 `*.corrupt`로 격리된다.
- [ ] **MV9-10 종료 체크포인트**: 정상 종료 후 `memoria.db-wal` 크기가 0이거나 사라진다(`wal_checkpoint(TRUNCATE)` 적용).
- [ ] **MV9-11 테마**: 모든 영역(툴바/사이드바/목록/검색결과/에디터)이 §10 브러시 키를 따르며, 테마 전환(M7) 시 일괄 변경된다.

위 11개 체크포인트가 모두 통과하면 M9 완료.

- [ ] **Step 7: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(app): integrate shell (NoteType view hosting, toolbar entries, search UI) and wire data-safety bootstrap

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 완료 기준 (Definition of Done)
- `dotnet.exe build "...\Memoria.sln"` 성공(0 Error).
- `dotnet.exe test "...\tests\Memoria.Tests"` 전체 통과(Task 1~5 자동 테스트 12건 + M1~M8 누적 회귀).
- Task 6 수동 검증 체크포인트 MV9-1~MV9-11 모두 통과.
- Produces 산출물 확정: M9 신규 — `MainViewModel.{CurrentEditor, NavigateToNote}`(+ `BuildEditorFor`, 에디터 팩토리 필드, 확장 생성자); M9가 본문을 채운 M2 스텁 — `MainViewModel.{SelectedNote, CurrentNoteType, SearchText, SearchResults, NewChecklistCommand, OpenWeeklyReportCommand, SearchCommand, OpenSearchHitCommand}`(+ `OnSelectedNoteChanged`); `Memoria.App.Services.{IStartupSafetyCoordinator, StartupSafetyCoordinator, StartupSafetyOutcome}`; MainWindow NoteType별 `ContentControl`+`DataTemplate` 호스팅; App.xaml.cs §9.4 step5/6 배선 + OnExit 체크포인트는 §8 락 하 `SqliteConnectionFactory.Dispose()`에 위임(raw 체크포인트 미추가).
- 계약 준수: 명령명(§9.3)·브러시 키(§10)·백업/무결성 시그니처(§8)·부트스트랩 순서(§9.4)를 임의 변경 없이 사용.

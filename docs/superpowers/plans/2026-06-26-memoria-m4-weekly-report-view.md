# Weekly Report View (M4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 주차 선택·양식 A/B 토글·미분류 경고·텍스트 생성/표시·클립보드 복사·멱등 재생성을 갖춘 주간보고 화면을 `WeeklyReportViewModel`(테스트 가능 로직)과 얇은 WPF 뷰로 구현한다.

**Architecture:** 모든 결정 로직(옵션 구성, 주차 계산 호출, 미분류 경고 조건, 멱등 재사용/덮어쓰기 분기, 시스템 그룹 배치)은 `Memoria.App.ViewModels.WeeklyReportViewModel`에 두고 `CommunityToolkit.Mvvm`만 의존하여 `Memoria.Tests`(net9.0-windows)에서 xUnit으로 자동 테스트한다. WPF 의존(클립보드/확인 다이얼로그)은 App 전용 얇은 인터페이스(`IClipboardService`, `IConfirmationDialogService`)로 추상화해 VM에서 분리하고, 실제 구현과 시각 동작은 수동 검증한다.

**Tech Stack:** C# / .NET 9, WPF(`net9.0-windows`), `CommunityToolkit.Mvvm`(`ObservableObject`/`[ObservableProperty]`/`[RelayCommand]`), `System.TimeProvider`(오늘 날짜 주입), xUnit + FluentAssertions.

## Global Constraints
- 런타임: .NET 9.
- TFM: `Memoria.Core`=`net9.0`, `Memoria.App`=`net9.0-windows`, `Memoria.Tests`=`net9.0-windows`.
- ViewModel은 `Memoria.App`에 두되 WPF 타입 의존 금지, `CommunityToolkit.Mvvm`만 사용. code-behind는 얇게 유지.
- DB는 `%LOCALAPPDATA%\Memoria` (이 마일스톤은 Repository 계약만 소비; 위치는 M1/M2 책임).
- WPF publish는 트리밍/압축 금지(`PublishTrimmed`, `EnableCompressionInSingleFile` 금지).
- 빌드/테스트는 Windows `dotnet.exe` + Windows 절대경로(계약 §7).
- 분류 우선순위: 자율형공장 > SLD (M1 렌더러/서비스 책임; 본 마일스톤은 결과 소비만).
- 양식 A는 `[업무 내용]`↔`[이슈]` 사이 빈 줄 1개(M1 렌더러 책임; VM은 `IWeeklyReportService.Render` 위임).
- 미분류 task 카운트 > 0이면 경고 배너 표시(조용한 누락 금지).
- 멱등: `(report_week_start, report_format)` 조합당 1개 메모 재사용. "다시 생성"은 기존 사용자 편집 `body` 덮어쓰기 전에 확인 다이얼로그.
- 신규 `weekly_report`는 시스템 그룹 `주간보고`(`is_system=1`)에 배치.
- 모든 색상/브러시는 `DynamicResource`만 사용(StaticResource 금지).

---

### Task 1: WeeklyReportViewModel 스캐폴드 + 주차 선택(기본 이번주) + 테스트 페이크

**Files:**
- Create: `src/Memoria.App/Services/IClipboardService.cs`
- Create: `src/Memoria.App/Services/IConfirmationDialogService.cs`
- Create: `src/Memoria.App/ViewModels/WeeklyReportViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/WeeklyReportFakes.cs`
- Test: `tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs`

**Interfaces:**
- Consumes: `Memoria.Core.Classification.IWeekCalculator.GetWorkWeek(DateOnly anyDate) -> (DateOnly Monday, DateOnly Friday)`; `Memoria.Core.Data.INoteRepository.FindWeeklyReport(DateOnly weekStart, ReportFormatKind format) -> Note?`; `Memoria.Core.Data.ISettingsRepository`; `Memoria.Core.Data.IClientRepository.GetAll(bool enabledOnly = false)`; `Memoria.Core.Data.IGroupRepository.GetAll()`; `Memoria.Core.Services.IWeeklyReportService`; `Memoria.Core.Models.ReportFormatKind`; `System.TimeProvider`.
- Produces: `Memoria.App.Services.IClipboardService`, `Memoria.App.Services.IConfirmationDialogService`, `Memoria.App.ViewModels.WeeklyReportViewModel`(속성 `SelectedDate`, `SelectedFormat`, `WeekStart`, `WeekEnd`, `WeekRangeLabel`).

- [ ] **Step 1: Write the failing test**

먼저 모든 후속 테스트가 공유할 페이크를 작성한다(`WeeklyReportFakes.cs`). 사용하지 않는 인터페이스 멤버는 `NotSupportedException`을 던지는 실코드로 둔다(placeholder 아님).

```csharp
// tests/Memoria.Tests/ViewModels/WeeklyReportFakes.cs
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Memoria.App.Services;

namespace Memoria.Tests.ViewModels;

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class FakeWeekCalculator : IWeekCalculator
{
    public (DateOnly Monday, DateOnly Friday) GetWorkWeek(DateOnly anyDate)
    {
        int diff = ((int)anyDate.DayOfWeek + 6) % 7; // Monday = 0
        var monday = anyDate.AddDays(-diff);
        return (monday, monday.AddDays(4));
    }
}

internal sealed class FakeWeeklyReportService : IWeeklyReportService
{
    public DateOnly LastAnyDate { get; private set; }
    public ReportRenderOptions? LastOptions { get; private set; }
    public ReportFormatKind LastRenderFormat { get; private set; }
    public int BuildCallCount { get; private set; }
    public Func<DateOnly, ReportRenderOptions, WeeklyReportBuildResult> BuildImpl { get; set; }
        = (d, o) => new WeeklyReportBuildResult(
            new WeeklyReportData(new List<ReportTask>(), new List<ReportIssue>()), 0, o.WeekStart, o.WeekEnd);
    public string RenderResult { get; set; } = "RENDERED-TEXT";

    public WeeklyReportBuildResult Build(DateOnly anyDateInWeek, ReportRenderOptions options)
    {
        BuildCallCount++;
        LastAnyDate = anyDateInWeek;
        LastOptions = options;
        return BuildImpl(anyDateInWeek, options);
    }

    public string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options)
    {
        LastRenderFormat = format;
        return RenderResult;
    }
}

internal sealed class FakeNoteRepository : INoteRepository
{
    public List<Note> Notes { get; } = new();
    public List<Note> Created { get; } = new();
    public List<Note> Updated { get; } = new();
    private int _nextId = 100;

    public int Create(Note note)
    {
        note.Id = _nextId++;
        Notes.Add(note);
        Created.Add(note);
        return note.Id;
    }

    public void Update(Note note)
    {
        Updated.Add(note);
        var idx = Notes.FindIndex(n => n.Id == note.Id);
        if (idx >= 0) Notes[idx] = note;
    }

    public Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format)
        => Notes.FirstOrDefault(n => n.Type == NoteType.WeeklyReport
            && n.ReportWeekStart == weekStart && n.ReportFormat == format);

    public Note? Get(int id) => Notes.FirstOrDefault(n => n.Id == id);

    public void SoftDelete(int id) => throw new NotSupportedException();
    public void Restore(int id) => throw new NotSupportedException();
    public void Purge(int id) => throw new NotSupportedException();
    public void PurgeExpiredTrash(int retentionDays) => throw new NotSupportedException();
    public IReadOnlyList<Note> GetByGroup(int? groupId) => throw new NotSupportedException();
    public IReadOnlyList<Note> GetTrash() => throw new NotSupportedException();
    public IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday) => throw new NotSupportedException();
}

internal sealed class FakeClientRepository : IClientRepository
{
    public List<Client> Clients { get; } = new();
    public bool LastEnabledOnly { get; private set; }

    public IReadOnlyList<Client> GetAll(bool enabledOnly = false)
    {
        LastEnabledOnly = enabledOnly;
        var src = enabledOnly ? Clients.Where(c => c.Enabled) : Clients;
        return src.OrderBy(c => c.SortOrder).ToList();
    }

    public int Create(Client client) => throw new NotSupportedException();
    public void Update(Client client) => throw new NotSupportedException();
    public void Delete(int id) => throw new NotSupportedException();
    public IReadOnlyList<ClientRule> GetRules() => throw new NotSupportedException();
    public void ReplaceRules(int clientId, IEnumerable<ClientRule> rules) => throw new NotSupportedException();
}

internal sealed class FakeGroupRepository : IGroupRepository
{
    public List<Group> Groups { get; } = new();

    public IReadOnlyList<Group> GetAll() => Groups.OrderBy(g => g.SortOrder).ToList();

    public int Create(Group group) => throw new NotSupportedException();
    public void Update(Group group) => throw new NotSupportedException();
    public void Delete(int id) => throw new NotSupportedException();
    public Group? Get(int id) => Groups.FirstOrDefault(g => g.Id == id);
}

internal sealed class FakeSettingsRepository : ISettingsRepository
{
    public Dictionary<string, string> Values { get; } = new();
    public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
    public string GetOrDefault(string key, string fallback) => Values.TryGetValue(key, out var v) ? v : fallback;
    public void Set(string key, string value) => Values[key] = value;
    public IReadOnlyDictionary<string, string> GetAll() => Values;
}

internal sealed class FakeClipboardService : IClipboardService
{
    public string? LastText { get; private set; }
    public int SetCount { get; private set; }
    public void SetText(string text) { LastText = text; SetCount++; }
}

internal sealed class FakeConfirmationDialogService : IConfirmationDialogService
{
    public bool Result { get; set; } = true;
    public int CallCount { get; private set; }
    public string? LastMessage { get; private set; }
    public bool Confirm(string message) { CallCount++; LastMessage = message; return Result; }
}
```

이어서 첫 테스트(주차 기본값/표시)를 작성한다.

```csharp
// tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class WeeklyReportViewModelTests
{
    private static (WeeklyReportViewModel vm, FakeWeeklyReportService svc, FakeNoteRepository notes,
        FakeClientRepository clients, FakeGroupRepository groups, FakeSettingsRepository settings,
        FakeClipboardService clip, FakeConfirmationDialogService dlg) CreateSut(DateTimeOffset? now = null)
    {
        var svc = new FakeWeeklyReportService();
        var notes = new FakeNoteRepository();
        var clients = new FakeClientRepository();
        var groups = new FakeGroupRepository
        {
            Groups = { new Group { Id = 2, Name = "주간보고", IsSystem = true, SortOrder = 1 } }
        };
        var settings = new FakeSettingsRepository();
        var clip = new FakeClipboardService();
        var dlg = new FakeConfirmationDialogService();
        var vm = new WeeklyReportViewModel(
            svc, new FakeWeekCalculator(), notes, clients, groups, settings, clip, dlg,
            new FixedTimeProvider(now ?? new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero)));
        return (vm, svc, notes, clients, groups, settings, clip, dlg);
    }

    [Fact]
    public void Default_week_is_current_week_monday_to_friday()
    {
        // 2026-06-24 == 수요일 → 그 주 월 06/22, 금 06/26
        var (vm, _, _, _, _, _, _, _) = CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));

        vm.SelectedDate.Should().Be(new DateOnly(2026, 6, 24));
        vm.WeekStart.Should().Be(new DateOnly(2026, 6, 22));
        vm.WeekEnd.Should().Be(new DateOnly(2026, 6, 26));
        vm.WeekRangeLabel.Should().Be("06/22 ~ 06/26");
        vm.SelectedFormat.Should().Be(ReportFormatKind.A);
    }

    [Fact]
    public void Changing_selected_date_recomputes_week_range()
    {
        var (vm, _, _, _, _, _, _, _) = CreateSut();

        vm.SelectedDate = new DateOnly(2026, 1, 1); // 목요일 → 월 2025-12-29, 금 2026-01-02

        vm.WeekStart.Should().Be(new DateOnly(2025, 12, 29));
        vm.WeekEnd.Should().Be(new DateOnly(2026, 1, 2));
        vm.WeekRangeLabel.Should().Be("12/29 ~ 01/02");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeeklyReportViewModelTests"
```
예상 실패: 컴파일 에러 `CS0246: The type or namespace name 'WeeklyReportViewModel' could not be found` 및 `IClipboardService`/`IConfirmationDialogService` 미정의.

- [ ] **Step 3: Write minimal implementation**

App 전용 추상화 인터페이스 2개와 VM 스캐폴드를 작성한다.

```csharp
// src/Memoria.App/Services/IClipboardService.cs
namespace Memoria.App.Services;

public interface IClipboardService
{
    void SetText(string text);
}
```

```csharp
// src/Memoria.App/Services/IConfirmationDialogService.cs
namespace Memoria.App.Services;

public interface IConfirmationDialogService
{
    /// 사용자가 확인(예)을 누르면 true, 취소(아니오)면 false.
    bool Confirm(string message);
}
```

```csharp
// src/Memoria.App/ViewModels/WeeklyReportViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using Memoria.App.Services;
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Services;

namespace Memoria.App.ViewModels;

public partial class WeeklyReportViewModel : ObservableObject
{
    private readonly IWeeklyReportService _reportService;
    private readonly IWeekCalculator _weekCalculator;
    private readonly INoteRepository _noteRepository;
    private readonly IClientRepository _clientRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly ISettingsRepository _settings;
    private readonly IClipboardService _clipboard;
    private readonly IConfirmationDialogService _dialogs;
    private readonly TimeProvider _timeProvider;

    private const string WeeklyReportGroupName = "주간보고";

    private int? _currentNoteId;

    [ObservableProperty]
    private DateOnly _selectedDate;

    [ObservableProperty]
    private ReportFormatKind _selectedFormat = ReportFormatKind.A;

    [ObservableProperty]
    private DateOnly _weekStart;

    [ObservableProperty]
    private DateOnly _weekEnd;

    [ObservableProperty]
    private string _weekRangeLabel = "";

    public WeeklyReportViewModel(
        IWeeklyReportService reportService,
        IWeekCalculator weekCalculator,
        INoteRepository noteRepository,
        IClientRepository clientRepository,
        IGroupRepository groupRepository,
        ISettingsRepository settings,
        IClipboardService clipboard,
        IConfirmationDialogService dialogs,
        TimeProvider timeProvider)
    {
        _reportService = reportService;
        _weekCalculator = weekCalculator;
        _noteRepository = noteRepository;
        _clientRepository = clientRepository;
        _groupRepository = groupRepository;
        _settings = settings;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _timeProvider = timeProvider;

        // 기본 = 오늘이 포함된 주. 부작용(리포지토리 호출)을 피하려고 backing field에 직접 설정.
        _selectedDate = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        RecomputeWeek();
    }

    partial void OnSelectedDateChanged(DateOnly value) => RecomputeWeek();

    private void RecomputeWeek()
    {
        var (monday, friday) = _weekCalculator.GetWorkWeek(SelectedDate);
        WeekStart = monday;
        WeekEnd = friday;
        WeekRangeLabel = $"{monday:MM/dd} ~ {friday:MM/dd}";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeeklyReportViewModelTests"
```
예상 PASS: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```bash
git add src/Memoria.App/Services/IClipboardService.cs src/Memoria.App/Services/IConfirmationDialogService.cs src/Memoria.App/ViewModels/WeeklyReportViewModel.cs tests/Memoria.Tests/ViewModels/WeeklyReportFakes.cs tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs
git commit -m "feat(m4): WeeklyReportViewModel scaffold with default-week selection

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: settings 기반 ReportRenderOptions 구성 + Build 호출 + 미분류 경고 배너

**Files:**
- Modify: `src/Memoria.App/ViewModels/WeeklyReportViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs`

**Interfaces:**
- Consumes: `Memoria.Core.Services.IWeeklyReportService.Build(DateOnly anyDateInWeek, ReportRenderOptions options) -> WeeklyReportBuildResult`; `IWeeklyReportService.Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options) -> string`; `Memoria.Core.Reporting.ReportRenderOptions`(init 속성 `ReporterName/WeekStart/WeekEnd/TaskHeaderA/IssueHeaderA/TitleWordB/IssueHeaderB/Indent/IncludeDoneOnly/Clients/UnclassifiedLabel`); `Memoria.Core.Services.WeeklyReportBuildResult(WeeklyReportData Data, int UnclassifiedTaskCount, DateOnly Monday, DateOnly Friday)`; `Memoria.Core.SettingsKeys.*`; `IClientRepository.GetAll(enabledOnly: true)`.
- Produces: `WeeklyReportViewModel.ReportText`(string), `.UnclassifiedTaskCount`(int), `.HasUnclassifiedWarning`(bool), `GenerateCommand`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs (append)
using Memoria.Core;
using Memoria.Core.Reporting;
using Memoria.Core.Services;

// ... 클래스 내부에 추가 ...

    [Fact]
    public void Generate_builds_options_from_settings_and_enabled_clients()
    {
        var (vm, svc, _, clients, _, settings, _, _) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));

        settings.Set(SettingsKeys.ReporterName, "홍길동");
        settings.Set(SettingsKeys.FormatATaskHeader, "[할 일]");
        settings.Set(SettingsKeys.FormatAIssueHeader, "[이슈들]");
        settings.Set(SettingsKeys.FormatBTitleWord, "위클리");
        settings.Set(SettingsKeys.FormatBIssueHeader, "* 이슈:");
        settings.Set(SettingsKeys.ReportIndent, "  ");
        settings.Set(SettingsKeys.IncludeDoneOnly, "true");
        clients.Clients.Add(new Client { Id = 1, Name = "SLD", SortOrder = 1, Enabled = true });
        clients.Clients.Add(new Client { Id = 2, Name = "MTP", SortOrder = 2, Enabled = false });

        vm.GenerateCommand.Execute(null);

        clients.LastEnabledOnly.Should().BeTrue();
        var opts = svc.LastOptions!;
        opts.ReporterName.Should().Be("홍길동");
        opts.TaskHeaderA.Should().Be("[할 일]");
        opts.IssueHeaderA.Should().Be("[이슈들]");
        opts.TitleWordB.Should().Be("위클리");
        opts.IssueHeaderB.Should().Be("* 이슈:");
        opts.Indent.Should().Be("  ");
        opts.IncludeDoneOnly.Should().BeTrue();
        opts.WeekStart.Should().Be(new DateOnly(2026, 6, 22));
        opts.WeekEnd.Should().Be(new DateOnly(2026, 6, 26));
        opts.Clients.Select(c => c.Name).Should().Equal("SLD"); // enabledOnly → MTP 제외
        svc.LastAnyDate.Should().Be(new DateOnly(2026, 6, 24));
    }

    [Fact]
    public void Generate_uses_contract_defaults_when_settings_missing()
    {
        var (vm, svc, _, _, _, _, _, _) = CreateSut();

        vm.GenerateCommand.Execute(null);

        var opts = svc.LastOptions!;
        opts.ReporterName.Should().Be("이승현");
        opts.TaskHeaderA.Should().Be("[업무 내용]");
        opts.IssueHeaderA.Should().Be("[이슈]");
        opts.TitleWordB.Should().Be("주간 보고");
        opts.IssueHeaderB.Should().Be("* 이슈사항:");
        opts.Indent.Should().Be("\t");
        opts.IncludeDoneOnly.Should().BeFalse();
        opts.UnclassifiedLabel.Should().Be("미분류");
    }

    [Fact]
    public void Generate_sets_report_text_from_renderer_for_selected_format()
    {
        var (vm, svc, _, _, _, _, _, _) = CreateSut();
        svc.RenderResult = "최종 보고서 본문";
        vm.SelectedFormat = ReportFormatKind.B;

        vm.GenerateCommand.Execute(null);

        vm.ReportText.Should().Be("최종 보고서 본문");
        svc.LastRenderFormat.Should().Be(ReportFormatKind.B);
    }

    [Fact]
    public void Warning_banner_shows_only_when_unclassified_count_positive()
    {
        var (vm, svc, _, _, _, _, _, _) = CreateSut();
        svc.BuildImpl = (d, o) => new WeeklyReportBuildResult(
            new WeeklyReportData(new List<ReportTask>(), new List<ReportIssue>()), 3, o.WeekStart, o.WeekEnd);

        vm.GenerateCommand.Execute(null);

        vm.UnclassifiedTaskCount.Should().Be(3);
        vm.HasUnclassifiedWarning.Should().BeTrue();

        svc.BuildImpl = (d, o) => new WeeklyReportBuildResult(
            new WeeklyReportData(new List<ReportTask>(), new List<ReportIssue>()), 0, o.WeekStart, o.WeekEnd);
        vm.GenerateCommand.Execute(null);

        vm.UnclassifiedTaskCount.Should().Be(0);
        vm.HasUnclassifiedWarning.Should().BeFalse();
    }
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeeklyReportViewModelTests"
```
예상 실패: `CS1061: 'WeeklyReportViewModel' does not contain a definition for 'GenerateCommand'` / `ReportText` / `HasUnclassifiedWarning`.

- [ ] **Step 3: Write minimal implementation**

`WeeklyReportViewModel.cs`에 옵션 빌더·Generate·관련 속성을 추가한다. `using CommunityToolkit.Mvvm.Input;`, `using Memoria.Core;`, `using Memoria.Core.Reporting;`를 파일 상단에 추가.

```csharp
// using 추가
using CommunityToolkit.Mvvm.Input;
using Memoria.Core;
using Memoria.Core.Reporting;
```

```csharp
// 속성 추가 (기존 [ObservableProperty] 블록 아래)
    [ObservableProperty]
    private string _reportText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnclassifiedWarning))]
    private int _unclassifiedTaskCount;

    public bool HasUnclassifiedWarning => UnclassifiedTaskCount > 0;
```

```csharp
// 메서드/커맨드 추가 (RecomputeWeek 아래)
    private ReportRenderOptions BuildOptions(DateOnly monday, DateOnly friday)
    {
        var includeDoneOnly =
            bool.TryParse(_settings.GetOrDefault(SettingsKeys.IncludeDoneOnly, "false"), out var b) && b;

        return new ReportRenderOptions
        {
            ReporterName = _settings.GetOrDefault(SettingsKeys.ReporterName, "이승현"),
            WeekStart = monday,
            WeekEnd = friday,
            TaskHeaderA = _settings.GetOrDefault(SettingsKeys.FormatATaskHeader, "[업무 내용]"),
            IssueHeaderA = _settings.GetOrDefault(SettingsKeys.FormatAIssueHeader, "[이슈]"),
            TitleWordB = _settings.GetOrDefault(SettingsKeys.FormatBTitleWord, "주간 보고"),
            IssueHeaderB = _settings.GetOrDefault(SettingsKeys.FormatBIssueHeader, "* 이슈사항:"),
            Indent = _settings.GetOrDefault(SettingsKeys.ReportIndent, "\t"),
            IncludeDoneOnly = includeDoneOnly,
            Clients = _clientRepository.GetAll(enabledOnly: true),
            UnclassifiedLabel = "미분류",
        };
    }

    private string RenderFresh(DateOnly monday, DateOnly friday)
    {
        var options = BuildOptions(monday, friday);
        var build = _reportService.Build(SelectedDate, options);
        UnclassifiedTaskCount = build.UnclassifiedTaskCount;
        return _reportService.Render(SelectedFormat, build.Data, options);
    }

    [RelayCommand]
    private void Generate()
    {
        var (monday, friday) = _weekCalculator.GetWorkWeek(SelectedDate);
        ReportText = RenderFresh(monday, friday);
    }
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeeklyReportViewModelTests"
```
예상 PASS: `Passed!  - Failed: 0, Passed: 6`.

- [ ] **Step 5: Commit**

```bash
git add src/Memoria.App/ViewModels/WeeklyReportViewModel.cs tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs
git commit -m "feat(m4): build ReportRenderOptions from settings and surface unclassified warning

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: 멱등 재사용 / 재생성 덮어쓰기 확인 / 시스템 그룹 '주간보고' 배치

**Files:**
- Modify: `src/Memoria.App/ViewModels/WeeklyReportViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs`

**Interfaces:**
- Consumes: `INoteRepository.FindWeeklyReport(DateOnly weekStart, ReportFormatKind format) -> Note?`; `INoteRepository.Create(Note) -> int`; `INoteRepository.Update(Note)`; `IGroupRepository.GetAll() -> IReadOnlyList<Group>`; `Memoria.Core.Models.Note`(`Type=NoteType.WeeklyReport`, `GroupId`, `ReportFormat`, `ReportWeekStart`, `Body`); `Memoria.Core.Models.Group`(`IsSystem`, `Name`); `IConfirmationDialogService.Confirm(string) -> bool`.
- Produces: `WeeklyReportViewModel.RegenerateCommand`, 멱등 로드/생성 동작.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs (append)

    [Fact]
    public void Generate_reuses_existing_report_body_without_rebuilding()
    {
        var (vm, svc, notes, _, _, _, _, _) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
        notes.Notes.Add(new Note
        {
            Id = 55,
            Type = NoteType.WeeklyReport,
            ReportFormat = ReportFormatKind.A,
            ReportWeekStart = new DateOnly(2026, 6, 22),
            Body = "사용자가 손으로 편집한 보고서",
        });

        vm.GenerateCommand.Execute(null);

        vm.ReportText.Should().Be("사용자가 손으로 편집한 보고서");
        notes.Created.Should().BeEmpty();
        notes.Updated.Should().BeEmpty();
    }

    [Fact]
    public void Generate_creates_new_report_in_weekly_report_system_group()
    {
        var (vm, svc, notes, _, _, _, _, _) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
        svc.RenderResult = "새로 생성된 본문";

        vm.GenerateCommand.Execute(null);

        notes.Created.Should().HaveCount(1);
        var created = notes.Created[0];
        created.Type.Should().Be(NoteType.WeeklyReport);
        created.GroupId.Should().Be(2); // FakeGroupRepository의 '주간보고' 시스템 그룹 Id
        created.ReportFormat.Should().Be(ReportFormatKind.A);
        created.ReportWeekStart.Should().Be(new DateOnly(2026, 6, 22));
        created.Body.Should().Be("새로 생성된 본문");
    }

    [Fact]
    public void Regenerate_overwrites_existing_body_after_confirm()
    {
        var (vm, svc, notes, _, _, _, _, dlg) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
        notes.Notes.Add(new Note
        {
            Id = 77,
            Type = NoteType.WeeklyReport,
            ReportFormat = ReportFormatKind.A,
            ReportWeekStart = new DateOnly(2026, 6, 22),
            Body = "예전 편집본",
        });
        svc.RenderResult = "재생성된 본문";
        dlg.Result = true;

        vm.RegenerateCommand.Execute(null);

        dlg.CallCount.Should().Be(1);
        notes.Updated.Should().HaveCount(1);
        notes.Updated[0].Id.Should().Be(77);
        notes.Updated[0].Body.Should().Be("재생성된 본문");
        vm.ReportText.Should().Be("재생성된 본문");
    }

    [Fact]
    public void Regenerate_keeps_existing_body_when_confirm_declined()
    {
        var (vm, svc, notes, _, _, _, _, dlg) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
        notes.Notes.Add(new Note
        {
            Id = 88,
            Type = NoteType.WeeklyReport,
            ReportFormat = ReportFormatKind.A,
            ReportWeekStart = new DateOnly(2026, 6, 22),
            Body = "지키고 싶은 편집본",
        });
        svc.RenderResult = "버려질 본문";
        dlg.Result = false;

        vm.RegenerateCommand.Execute(null);

        dlg.CallCount.Should().Be(1);
        notes.Updated.Should().BeEmpty();
        notes.Notes.Single().Body.Should().Be("지키고 싶은 편집본");
    }

    [Fact]
    public void Regenerate_does_not_prompt_when_no_existing_body()
    {
        var (vm, svc, notes, _, _, _, _, dlg) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
        svc.RenderResult = "첫 생성 본문";

        vm.RegenerateCommand.Execute(null);

        dlg.CallCount.Should().Be(0);
        notes.Created.Should().HaveCount(1);
        notes.Created[0].Body.Should().Be("첫 생성 본문");
    }
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeeklyReportViewModelTests"
```
예상 실패: `Generate_reuses_existing_report_body...`에서 `notes.Created`가 비어있지 않거나(현재 Generate가 항상 새로 생성), `RegenerateCommand` 미정의(`CS1061`).

- [ ] **Step 3: Write minimal implementation**

Task 2의 `Generate`를 멱등 로드로 교체하고, 신규 생성/덮어쓰기 저장과 `Regenerate`를 추가한다.

```csharp
// 기존 Generate 교체
    [RelayCommand]
    private void Generate()
    {
        var (monday, friday) = _weekCalculator.GetWorkWeek(SelectedDate);
        var existing = _noteRepository.FindWeeklyReport(monday, SelectedFormat);
        if (existing is not null && !string.IsNullOrEmpty(existing.Body))
        {
            // 멱등 재사용: 사용자가 편집한 기존 본문을 그대로 표시.
            _currentNoteId = existing.Id;
            ReportText = existing.Body;
            var options = BuildOptions(monday, friday);
            UnclassifiedTaskCount = _reportService.Build(SelectedDate, options).UnclassifiedTaskCount;
            return;
        }

        var text = RenderFresh(monday, friday);
        ReportText = text;
        Persist(monday, existing, text);
    }

    [RelayCommand]
    private void Regenerate()
    {
        var (monday, friday) = _weekCalculator.GetWorkWeek(SelectedDate);
        var existing = _noteRepository.FindWeeklyReport(monday, SelectedFormat);
        if (existing is not null && !string.IsNullOrEmpty(existing.Body))
        {
            if (!_dialogs.Confirm("기존에 편집한 주간보고 내용을 덮어씁니다. 계속할까요?"))
                return;
        }

        var text = RenderFresh(monday, friday);
        ReportText = text;
        Persist(monday, existing, text);
    }

    private void Persist(DateOnly monday, Note? existing, string text)
    {
        if (existing is null)
        {
            var note = new Note
            {
                Type = NoteType.WeeklyReport,
                GroupId = ResolveWeeklyReportGroupId(),
                ReportFormat = SelectedFormat,
                ReportWeekStart = monday,
                Body = text,
            };
            _currentNoteId = _noteRepository.Create(note);
        }
        else
        {
            existing.Body = text;
            _noteRepository.Update(existing);
            _currentNoteId = existing.Id;
        }
    }

    private int? ResolveWeeklyReportGroupId()
    {
        var group = _groupRepository.GetAll()
            .FirstOrDefault(g => g.IsSystem && g.Name == WeeklyReportGroupName);
        return group?.Id;
    }
```

`using System.Linq;`가 필요하면 추가(`FirstOrDefault`). .NET 9 ImplicitUsings가 켜져 있으면 생략 가능.

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeeklyReportViewModelTests"
```
예상 PASS: `Passed!  - Failed: 0, Passed: 11`.

- [ ] **Step 5: Commit**

```bash
git add src/Memoria.App/ViewModels/WeeklyReportViewModel.cs tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs
git commit -m "feat(m4): idempotent reuse, regenerate confirm, and system-group placement

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: 복사 커맨드 + 양식 토글 로드 + DateOnly 컨버터 + WPF 뷰 + DI + 수동 검증

**Files:**
- Modify: `src/Memoria.App/ViewModels/WeeklyReportViewModel.cs`
- Create: `src/Memoria.App/Converters/DateOnlyConverter.cs`
- Create: `src/Memoria.App/Services/WpfClipboardService.cs`
- Create: `src/Memoria.App/Services/MessageBoxConfirmationDialogService.cs`
- Create: `src/Memoria.App/Views/WeeklyReportView.xaml`
- Create: `src/Memoria.App/Views/WeeklyReportView.xaml.cs`
- Modify: `src/Memoria.App/App.xaml.cs` (M2의 `OnStartup` step3 인라인 `sc` 등록 블록 — `ConfigureServices` 메서드는 없으므로 그 인라인 블록에 등록한다)
- Test: `tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs`
- Test: `tests/Memoria.Tests/Converters/DateOnlyConverterTests.cs`

**Interfaces:**
- Consumes: `IClipboardService.SetText(string)`; `INoteRepository.FindWeeklyReport(...)`; M2 DI 컨테이너(`Microsoft.Extensions.DependencyInjection`).
- Produces: `WeeklyReportViewModel.CopyCommand`, 양식 토글 시 재로드, `Memoria.App.Converters.DateOnlyConverter`, `WpfClipboardService`, `MessageBoxConfirmationDialogService`, `WeeklyReportView`.

- [ ] **Step 1: Write the failing test**

VM 로직(복사/토글 재로드)과 컨버터 라운드트립을 테스트한다.

```csharp
// tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs (append)

    [Fact]
    public void Copy_sends_current_report_text_to_clipboard()
    {
        var (vm, svc, _, _, _, _, clip, _) = CreateSut();
        svc.RenderResult = "복사 대상 본문";
        vm.GenerateCommand.Execute(null);

        vm.CopyCommand.Execute(null);

        clip.SetCount.Should().Be(1);
        clip.LastText.Should().Be("복사 대상 본문");
    }

    [Fact]
    public void Changing_format_loads_existing_report_for_that_format()
    {
        var (vm, _, notes, _, _, _, _, _) =
            CreateSut(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
        notes.Notes.Add(new Note
        {
            Id = 91,
            Type = NoteType.WeeklyReport,
            ReportFormat = ReportFormatKind.B,
            ReportWeekStart = new DateOnly(2026, 6, 22),
            Body = "B 양식 기존 본문",
        });

        vm.SelectedFormat = ReportFormatKind.B;

        vm.ReportText.Should().Be("B 양식 기존 본문");
    }

    [Fact]
    public void Changing_format_clears_text_when_no_existing_report()
    {
        var (vm, svc, _, _, _, _, _, _) = CreateSut();
        svc.RenderResult = "A 본문";
        vm.GenerateCommand.Execute(null);
        vm.ReportText.Should().Be("A 본문");

        vm.SelectedFormat = ReportFormatKind.B;

        vm.ReportText.Should().BeEmpty();
    }
```

```csharp
// tests/Memoria.Tests/Converters/DateOnlyConverterTests.cs
using System.Globalization;
using FluentAssertions;
using Memoria.App.Converters;
using Xunit;

namespace Memoria.Tests.Converters;

public class DateOnlyConverterTests
{
    [Fact]
    public void Convert_dateonly_to_datetime()
    {
        var c = new DateOnlyConverter();
        var result = c.Convert(new DateOnly(2026, 6, 22), typeof(DateTime?), null!, CultureInfo.InvariantCulture);
        result.Should().Be(new DateTime(2026, 6, 22));
    }

    [Fact]
    public void ConvertBack_datetime_to_dateonly()
    {
        var c = new DateOnlyConverter();
        var result = c.ConvertBack(new DateTime(2026, 6, 22, 10, 30, 0), typeof(DateOnly), null!, CultureInfo.InvariantCulture);
        result.Should().Be(new DateOnly(2026, 6, 22));
    }

    [Fact]
    public void ConvertBack_null_keeps_dateonly_minvalue()
    {
        var c = new DateOnlyConverter();
        var result = c.ConvertBack(null!, typeof(DateOnly), null!, CultureInfo.InvariantCulture);
        result.Should().Be(DateOnly.MinValue);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeeklyReportViewModelTests|FullyQualifiedName~DateOnlyConverterTests"
```
예상 실패: `CS1061: ... 'CopyCommand'`, `CS0246: ... 'DateOnlyConverter'`, 그리고 양식 토글 시 `ReportText`가 갱신되지 않아 어서션 실패.

- [ ] **Step 3: Write minimal implementation**

VM에 Copy 커맨드와 양식 변경 핸들러를 추가한다.

```csharp
// WeeklyReportViewModel.cs 에 추가
    partial void OnSelectedFormatChanged(ReportFormatKind value) => LoadExisting();

    private void LoadExisting()
    {
        var (monday, _) = _weekCalculator.GetWorkWeek(SelectedDate);
        var existing = _noteRepository.FindWeeklyReport(monday, SelectedFormat);
        _currentNoteId = existing?.Id;
        ReportText = existing?.Body ?? "";
        UnclassifiedTaskCount = 0;
    }

    [RelayCommand]
    private void Copy() => _clipboard.SetText(ReportText ?? "");
```

DateOnly ↔ DateTime 컨버터:

```csharp
// src/Memoria.App/Converters/DateOnlyConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace Memoria.App.Converters;

public sealed class DateOnlyConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DateOnly d ? (DateTime?)d.ToDateTime(TimeOnly.MinValue) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DateTime dt ? DateOnly.FromDateTime(dt) : DateOnly.MinValue;
}
```

WPF 구현 서비스(수동 검증 대상):

```csharp
// src/Memoria.App/Services/WpfClipboardService.cs
using System.Windows;

namespace Memoria.App.Services;

public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text) => Clipboard.SetText(text ?? "");
}
```

```csharp
// src/Memoria.App/Services/MessageBoxConfirmationDialogService.cs
using System.Windows;

namespace Memoria.App.Services;

public sealed class MessageBoxConfirmationDialogService : IConfirmationDialogService
{
    public bool Confirm(string message)
        => MessageBox.Show(message, "확인", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;
}
```

WPF 뷰(얇은 code-behind, 모든 색은 `DynamicResource`):

```xml
<!-- src/Memoria.App/Views/WeeklyReportView.xaml -->
<UserControl x:Class="Memoria.App.Views.WeeklyReportView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:conv="clr-namespace:Memoria.App.Converters"
             xmlns:models="clr-namespace:Memoria.Core.Models;assembly=Memoria.Core"
             Background="{DynamicResource Brush.WindowBackground}">
    <UserControl.Resources>
        <conv:DateOnlyConverter x:Key="DateOnlyConverter"/>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>
    </UserControl.Resources>
    <DockPanel Margin="12">
        <!-- 상단: 주차 선택 + 양식 토글 + 액션 -->
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="주차:" VerticalAlignment="Center" Margin="0,0,6,0"
                       Foreground="{DynamicResource Brush.Foreground}"/>
            <DatePicker SelectedDate="{Binding SelectedDate, Converter={StaticResource DateOnlyConverter}}"
                        Width="140"/>
            <TextBlock Text="{Binding WeekRangeLabel}" VerticalAlignment="Center" Margin="10,0"
                       Foreground="{DynamicResource Brush.Foreground}"/>
            <RadioButton Content="양식 A" Margin="10,0,4,0" VerticalAlignment="Center"
                         Foreground="{DynamicResource Brush.Foreground}"
                         IsChecked="{Binding SelectedFormat, Converter={x:Static conv:EnumMatchConverter.Instance}, ConverterParameter={x:Static models:ReportFormatKind.A}}"/>
            <RadioButton Content="양식 B" Margin="4,0" VerticalAlignment="Center"
                         Foreground="{DynamicResource Brush.Foreground}"
                         IsChecked="{Binding SelectedFormat, Converter={x:Static conv:EnumMatchConverter.Instance}, ConverterParameter={x:Static models:ReportFormatKind.B}}"/>
            <Button Content="생성" Command="{Binding GenerateCommand}" Margin="16,0,4,0" Padding="10,2"/>
            <Button Content="다시 생성" Command="{Binding RegenerateCommand}" Margin="4,0" Padding="10,2"/>
            <Button Content="복사" Command="{Binding CopyCommand}" Margin="4,0" Padding="10,2"/>
        </StackPanel>

        <!-- 미분류 경고 배너 -->
        <Border DockPanel.Dock="Top"
                Visibility="{Binding HasUnclassifiedWarning, Converter={StaticResource BoolToVis}}"
                Background="{DynamicResource Brush.WarningBackground}"
                BorderBrush="{DynamicResource Brush.WarningBorder}" BorderThickness="1"
                Padding="8" Margin="0,0,0,8" CornerRadius="4">
            <TextBlock Foreground="{DynamicResource Brush.WarningForeground}" TextWrapping="Wrap"
                       Text="{Binding UnclassifiedTaskCount, StringFormat=미분류 업무가 {0}건 있습니다. 양식 B에 [ 미분류 ] 섹션으로 출력됩니다. 키워드/고객사를 확인하세요.}"/>
        </Border>

        <!-- 본문(편집 가능) -->
        <TextBox Text="{Binding ReportText, UpdateSourceTrigger=PropertyChanged}"
                 AcceptsReturn="True" AcceptsTab="True" TextWrapping="NoWrap"
                 VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto"
                 FontFamily="Consolas" FontSize="13"
                 Background="{DynamicResource Brush.EditorBackground}"
                 Foreground="{DynamicResource Brush.Foreground}"
                 BorderBrush="{DynamicResource Brush.Border}"/>
    </DockPanel>
</UserControl>
```

```csharp
// src/Memoria.App/Views/WeeklyReportView.xaml.cs
using System.Windows.Controls;

namespace Memoria.App.Views;

public partial class WeeklyReportView : UserControl
{
    public WeeklyReportView() => InitializeComponent();
}
```

양식 A/B 라디오 버튼 바인딩용 enum 매칭 컨버터(라우팅 로직, 단위 테스트 대상이 아닌 단순 UI 헬퍼):

```csharp
// src/Memoria.App/Converters/EnumMatchConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace Memoria.App.Converters;

public sealed class EnumMatchConverter : IValueConverter
{
    public static readonly EnumMatchConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null && value.Equals(parameter);

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;
}
```

DI 등록(M2의 `OnStartup` step3 인라인 `sc` 블록에 추가 — `App.xaml.cs`에 `ConfigureServices` 메서드는 **없다**; M2는 `var sc = new ServiceCollection(); sc.AddMemoriaCore(...); ... sc.BuildServiceProvider();` 형태이므로 그 블록 안, `BuildServiceProvider()` 호출 이전에 등록한다. M7/M9도 동일하게 이 인라인 `sc` 블록에 자기 서비스를 추가한다):

```csharp
// src/Memoria.App/App.xaml.cs — OnStartup step3 인라인 `sc` 등록 블록 안(BuildServiceProvider() 이전)에 추가
sc.AddSingleton<IClipboardService, WpfClipboardService>();
sc.AddSingleton<IConfirmationDialogService, MessageBoxConfirmationDialogService>();
// TimeProvider.System 은 M2가 step3에서 이미 등록한다 → 중복 등록 금지(여기서 다시 등록하지 않는다).
sc.AddTransient<WeeklyReportViewModel>();
```

> **MainWindow 호스팅/진입점은 M9에서 통합한다.** 본 마일스톤은 `WeeklyReportView` + `WeeklyReportViewModel` + 위 DI 배선까지만 산출한다. MainWindow의 `NoteType.WeeklyReport` → `WeeklyReportView` 호스팅(ContentControl+DataTemplate)과 툴바 [📋 주간보고] 진입점(계약 §9.3 `OpenWeeklyReportCommand` 본문)은 계약 §11 M9(셸 통합 capstone)에서 배선한다. M2는 `OpenWeeklyReportCommand`를 스텁으로 선언만 한 상태이므로, M4 시점에는 수동 검증을 위해 임시로 단독 창/테스트 호스트에서 `WeeklyReportView`를 띄워 확인한다.

> 본 뷰는 계약 §10(WPF 테마 브러시 키, 단일 진리원천)의 키만 `DynamicResource`로 사용한다: `Brush.WindowBackground`, `Brush.Foreground`, `Brush.Border`, `Brush.EditorBackground`, `Brush.WarningBackground`, `Brush.WarningBorder`, `Brush.WarningForeground`. 이 키들은 M7 팔레트가 모두 정의한다. M4 시점에 키가 없다면 임시로 `App.xaml`의 `MergedDictionaries` 테마 사전에 §10과 **동일한 `Brush.*` 키**를 `DynamicResource` 소비가 가능하도록 정의해 두고, M7에서 정식 팔레트로 대체한다(StaticResource 및 임의 키 사용 금지).

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeeklyReportViewModelTests|FullyQualifiedName~DateOnlyConverterTests"
```
예상 PASS: `Passed!  - Failed: 0, Passed: 17`.
이어서 전체 솔루션 빌드로 WPF 뷰/컨버터/DI 컴파일을 검증:
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상: `Build succeeded. 0 Error(s)`.

- [ ] **수동 검증 체크포인트** (자동 테스트 불가: 클립보드/다이얼로그/시각 동작)

앱을 실행(`dotnet.exe run --project ...Memoria.App` 또는 publish 후 실행)하고 주간보고 화면에서 확인한다.

1. 주차 선택 기본값: 화면 진입 시 DatePicker가 오늘 날짜, `WeekRangeLabel`이 이번주 `MM/dd ~ MM/dd`로 표시되는지 눈으로 확인.
2. 양식 토글: 양식 A/B 라디오 전환 시 본문이 해당 양식의 기존 저장본(있으면)으로 바뀌거나, 없으면 비워지는지 확인.
3. 미분류 경고 배너: 미분류 task가 있는 주를 생성하면 노란 경고 배너가 보이고 건수가 맞는지, 0건이면 배너가 사라지는지 확인.
4. 복사 버튼: [복사] 클릭 후 다른 앱(메모장)에 붙여넣어 본문 텍스트(탭 들여쓰기·`* ` 글머리 포함)가 그대로 들어오는지 확인.
5. 멱등/덮어쓰기 다이얼로그: 본문을 손으로 수정한 뒤 [다시 생성] 클릭 → "덮어씁니다" 확인 다이얼로그가 뜨고, [아니오]면 편집 유지, [예]면 재생성 본문으로 교체되는지 확인.
6. 시스템 그룹 배치: 신규 생성한 주간보고가 사이드바 `🔒 주간보고` 그룹에 나타나는지 확인.
7. 테마: 라이트/다크 전환 시 배너/에디터/버튼 색이 `DynamicResource`로 즉시 바뀌는지(깜빡임 없이) 확인.

- [ ] **Step 5: Commit**

```bash
git add src/Memoria.App/ViewModels/WeeklyReportViewModel.cs src/Memoria.App/Converters/ src/Memoria.App/Services/WpfClipboardService.cs src/Memoria.App/Services/MessageBoxConfirmationDialogService.cs src/Memoria.App/Views/WeeklyReportView.xaml src/Memoria.App/Views/WeeklyReportView.xaml.cs src/Memoria.App/App.xaml.cs tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs tests/Memoria.Tests/Converters/DateOnlyConverterTests.cs
git commit -m "feat(m4): clipboard copy, format toggle reload, WPF weekly report view and DI

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 마일스톤 완료 기준
- `WeeklyReportViewModelTests`(11+3=14건) + `DateOnlyConverterTests`(3건) 전부 PASS.
- `dotnet.exe build Memoria.sln` 성공(WPF 뷰/DI 포함).
- 수동 검증 체크포인트 1~7 모두 통과.
- 산출물 `WeeklyReportViewModel`이 후속 마일스톤(M5 그룹/휴지통, M7 테마)에서 소비 가능.

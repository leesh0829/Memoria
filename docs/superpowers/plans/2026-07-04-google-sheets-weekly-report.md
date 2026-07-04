# 구글 시트 주간보고 연동 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 구글 드라이브 구글 시트(일일 작업기록)를 서비스 계정으로 읽어(읽기 전용) 기존 주간보고(포맷 A/B)를 생성한다.

**Architecture:** 순수 파서(`SheetWorkParser`, Core)가 셀 격자 → 주간 (Tasks, Issues) 텍스트로 변환하고, 기존 분류기·렌더러·저장 경로를 그대로 재사용한다. 네트워크/인증은 `ISpreadsheetReader` 뒤의 얇은 App 어댑터(`GoogleSheetReader`)로 격리한다. 네트워크 fetch만 async.

**Tech Stack:** C#/.NET9, WPF, SQLite, `Google.Apis.Sheets.v4` + `Google.Apis.Auth`(서비스 계정), CommunityToolkit.Mvvm, xUnit + FluentAssertions.

## Global Constraints

- 빌드/테스트는 **Windows `dotnet.exe`를 WSL interop로** 호출. 실행 전 `taskkill.exe /IM Memoria.exe /F 2>/dev/null`.
  - 빌드: `dotnet.exe build "Memoria.sln" -c Release`
  - 테스트: `dotnet.exe test "tests/Memoria.Tests" -c Release`
- 커밋 메시지 끝에 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- 목표: 빌드 **경고 0 / 오류 0**, 기존 **329 테스트 + 신규 테스트 그린**.
- **시트는 읽기 전용**(`SheetsService.Scope.SpreadsheetsReadonly`). 쓰기 API 절대 사용 금지.
- 네트워크/클립보드/파일다이얼로그 WPF 부분은 WSL 자동 테스트 불가 → **빌드 게이트 + Windows GUI 수동 검증**. 순수 로직은 Core에 두어 테스트.
- **비동기 최소화**: `ISpreadsheetReader.ReadRowsAsync`만 async. 파싱·분류·렌더·저장은 동기.
- **중복 금지**: 시트 경로는 기존 `ClientClassifier`·`WeeklyReportRenderer`·`Persist`를 그대로 재사용(분류/렌더 로직 복제 금지).
- 설정 키 문자열: `google.serviceAccountJsonPath`, `google.sheetId`, `google.sheetTabName`. JSON 키는 **경로만** 저장.

---

## File Structure

- `src/Memoria.Core/Sheets/SheetWorkParser.cs` — 신규: 격자 → `ParsedWeek(Tasks, Issues)` 순수 파서 + `ParsedWeek` record.
- `src/Memoria.Core/Sheets/ISpreadsheetReader.cs` — 신규: async 격자 fetch 계약(구글 의존성 없음).
- `src/Memoria.Core/SettingsKeys.cs` — 수정: `google.*` 상수 3개.
- `src/Memoria.Core/Services/IWeeklyReportService.cs` + `WeeklyReportService.cs` — 수정: `BuildFromTexts(...)` 추가.
- `src/Memoria.App/Services/GoogleSheetReader.cs` — 신규: 서비스 계정 인증 + Sheets API fetch → 격자.
- `src/Memoria.App/Memoria.App.csproj` — 수정: `Google.Apis.Sheets.v4` 패키지.
- `src/Memoria.App/App.xaml.cs` — 수정: `ISpreadsheetReader` DI 등록.
- `src/Memoria.App/ViewModels/WeeklyReportViewModel.cs` — 수정: `ISpreadsheetReader` 주입 + `GenerateFromSheetCommand`(async).
- `src/Memoria.App/Views/WeeklyReportView.xaml` — 수정: "구글 시트에서 생성" 버튼.
- `src/Memoria.App/ViewModels/SettingsViewModel.cs` + `Views/SettingsWindow.xaml`(+`.xaml.cs`) — 수정: 구글 연동 설정 섹션 + JSON 경로 파일 선택.
- 테스트: `tests/Memoria.Tests/Sheets/SheetWorkParserTests.cs`(신규), `Services/WeeklyReportServiceTests.cs`(추가), `ViewModels/WeeklyReportViewModelTests.cs`(추가) + `ViewModels/WeeklyReportFakes.cs`(FakeSpreadsheetReader 추가 + FakeWeeklyReportService.BuildFromTexts).

---

## GM1: SheetWorkParser — 격자 → 주간 (Tasks, Issues) (Core, TDD)

**Files:**
- Create: `src/Memoria.Core/Sheets/SheetWorkParser.cs`
- Test: `tests/Memoria.Tests/Sheets/SheetWorkParserTests.cs`

**Interfaces:**
- Produces: `Memoria.Core.Sheets.ParsedWeek(IReadOnlyList<string> Tasks, IReadOnlyList<string> Issues)`; `static ParsedWeek SheetWorkParser.Parse(IReadOnlyList<IReadOnlyList<string>> grid, DateOnly monday, DateOnly friday)`; `static bool SheetWorkParser.TryParseDate(string cell, out DateOnly date)`.

- [ ] **Step 1: 실패 테스트 작성** — `tests/Memoria.Tests/Sheets/SheetWorkParserTests.cs`

```csharp
using FluentAssertions;
using Memoria.Core.Sheets;
using Xunit;

namespace Memoria.Tests.Sheets;

public class SheetWorkParserTests
{
    // 격자 헬퍼: 각 행은 셀 문자열 목록.
    private static IReadOnlyList<IReadOnlyList<string>> Grid(params string[][] rows)
        => rows.Select(r => (IReadOnlyList<string>)r.ToList()).ToList();

    private static readonly DateOnly Mon = new(2025, 9, 22);
    private static readonly DateOnly Fri = new(2025, 9, 26);

    [Fact]
    public void Parse_SkipsHeader_ExtractsTasksAndIssues_StripsNumbering()
    {
        var grid = Grid(
            new[] { "일자", "작업내역", "특이사항" },
            new[] { "2025.09.22 (월)", "1. SLD 점검\n2. MTP 정리", "1. 장비 오류\n2, 재확인" });

        var r = SheetWorkParser.Parse(grid, Mon, Fri);

        r.Tasks.Should().Equal("SLD 점검", "MTP 정리");
        r.Issues.Should().Equal("장비 오류", "재확인");
    }

    [Fact]
    public void Parse_FiltersRowsOutsideWeek()
    {
        var grid = Grid(
            new[] { "일자", "작업내역", "특이사항" },
            new[] { "2025.09.19 (금)", "이전주 업무", "" },   // 주 밖
            new[] { "2025.09.23 (화)", "이번주 업무", "" },
            new[] { "2025.09.29 (월)", "다음주 업무", "" });   // 주 밖

        var r = SheetWorkParser.Parse(grid, Mon, Fri);

        r.Tasks.Should().Equal("이번주 업무");
    }

    [Fact]
    public void Parse_SkipsRowsWithUnparseableOrEmptyDate()
    {
        var grid = Grid(
            new[] { "일자", "작업내역", "특이사항" },
            new[] { "", "빈 날짜", "" },
            new[] { "메모", "잘못된 날짜", "" },
            new[] { "2025.09.24 (수)", "정상", "" });

        var r = SheetWorkParser.Parse(grid, Mon, Fri);

        r.Tasks.Should().Equal("정상");
    }

    [Fact]
    public void Parse_EmptyIssueCell_YieldsNoIssues_AndRaggedRowIsSafe()
    {
        var grid = Grid(
            new[] { "일자", "작업내역", "특이사항" },
            new[] { "2025.09.24 (수)", "업무만" });   // C열 없음(래그드)

        var r = SheetWorkParser.Parse(grid, Mon, Fri);

        r.Tasks.Should().Equal("업무만");
        r.Issues.Should().BeEmpty();
    }

    [Theory]
    [InlineData("2025.09.22 (월)", true, 2025, 9, 22)]
    [InlineData("2025.9.2", true, 2025, 9, 2)]
    [InlineData("  2025.12.31 (수) ", true, 2025, 12, 31)]
    [InlineData("2025.13.01", false, 0, 0, 0)]
    [InlineData("메모", false, 0, 0, 0)]
    public void TryParseDate_ParsesLeadingYmd(string cell, bool ok, int y, int m, int d)
    {
        SheetWorkParser.TryParseDate(cell, out var date).Should().Be(ok);
        if (ok) date.Should().Be(new DateOnly(y, m, d));
    }
}
```

- [ ] **Step 2: 실패 확인**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~SheetWorkParserTests" 2>&1 | tail -8
```
기대: 컴파일 실패(`SheetWorkParser` 없음).

- [ ] **Step 3: 파서 구현** — `src/Memoria.Core/Sheets/SheetWorkParser.cs`

```csharp
using System.Text.RegularExpressions;

namespace Memoria.Core.Sheets;

public sealed record ParsedWeek(IReadOnlyList<string> Tasks, IReadOnlyList<string> Issues);

/// <summary>구글 시트 '일자 작업내역' 격자 → 대상 주(월~금)의 업무/이슈 텍스트. 순수 함수.</summary>
public static class SheetWorkParser
{
    // A열: 선행 YYYY.MM.DD (뒤의 " (요일)"은 무시).
    private static readonly Regex DateRx = new(@"^\s*(\d{4})\.(\d{1,2})\.(\d{1,2})", RegexOptions.Compiled);
    // 각 줄 선행 번호 "1. " / "2, " 제거.
    private static readonly Regex NumRx = new(@"^\s*\d+\s*[.,]\s*", RegexOptions.Compiled);

    public static ParsedWeek Parse(IReadOnlyList<IReadOnlyList<string>> grid, DateOnly monday, DateOnly friday)
    {
        var tasks = new List<string>();
        var issues = new List<string>();
        for (int r = 1; r < grid.Count; r++)   // 0행(헤더) 스킵
        {
            var row = grid[r];
            if (!TryParseDate(Cell(row, 0), out var date)) continue;
            if (date < monday || date > friday) continue;
            tasks.AddRange(SplitItems(Cell(row, 1)));
            issues.AddRange(SplitItems(Cell(row, 2)));
        }
        return new ParsedWeek(tasks, issues);
    }

    public static bool TryParseDate(string cell, out DateOnly date)
    {
        date = default;
        var m = DateRx.Match(cell ?? "");
        if (!m.Success) return false;
        int y = int.Parse(m.Groups[1].Value);
        int mo = int.Parse(m.Groups[2].Value);
        int d = int.Parse(m.Groups[3].Value);
        if (mo < 1 || mo > 12 || d < 1 || d > 31) return false;
        try { date = new DateOnly(y, mo, d); return true; }
        catch { return false; }
    }

    private static IEnumerable<string> SplitItems(string cell)
    {
        if (string.IsNullOrWhiteSpace(cell)) yield break;
        foreach (var raw in cell.Split('\n'))
        {
            var line = NumRx.Replace(raw.Trim().TrimEnd('\r').Trim(), "").Trim();
            if (line.Length > 0) yield return line;
        }
    }

    private static string Cell(IReadOnlyList<string> row, int i) => i < row.Count ? (row[i] ?? "") : "";
}
```

- [ ] **Step 4: 통과 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~SheetWorkParserTests" 2>&1 | tail -8
```
기대: PASS(전체 케이스).

- [ ] **Step 5: 커밋**

```bash
git add src/Memoria.Core/Sheets/SheetWorkParser.cs tests/Memoria.Tests/Sheets/SheetWorkParserTests.cs
git commit -m "feat(sheets): SheetWorkParser grid -> weekly tasks/issues

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## GM2: WeeklyReportService.BuildFromTexts (Core, TDD)

**Files:**
- Modify: `src/Memoria.Core/Services/IWeeklyReportService.cs`, `src/Memoria.Core/Services/WeeklyReportService.cs`
- Modify: `tests/Memoria.Tests/ViewModels/WeeklyReportFakes.cs` (FakeWeeklyReportService 인터페이스 구현 추가)
- Test: `tests/Memoria.Tests/Services/WeeklyReportServiceTests.cs` (추가)

**Interfaces:**
- Consumes: `ClientClassifier.Classify(string, IEnumerable<ClientRule>, ISet<int>)` (기존).
- Produces: `WeeklyReportBuildResult IWeeklyReportService.BuildFromTexts(IReadOnlyList<string> taskTexts, IReadOnlyList<string> issueTexts, DateOnly monday, DateOnly friday, ReportRenderOptions options)`. 각 업무는 자동 분류, `Done: true`.

- [ ] **Step 1: 실패 테스트 추가** — `WeeklyReportServiceTests.cs`

```csharp
    [Fact]
    public void BuildFromTexts_ClassifiesTasks_WrapsIssues_CountsUnclassified()
    {
        using var db = new TestDb();
        var (svc, _, _, clients) = Build(db);
        var sldId = clients.GetAll().Single(c => c.Name == "SLD").Id;
        var options = new ReportRenderOptions
        {
            WeekStart = new DateOnly(2026, 6, 22),
            WeekEnd = new DateOnly(2026, 6, 26),
            Clients = clients.GetAll(enabledOnly: true),
        };

        var result = svc.BuildFromTexts(
            new[] { "SLD 점검", "기타 정리" },
            new[] { "장비 오류" },
            new DateOnly(2026, 6, 22), new DateOnly(2026, 6, 26), options);

        result.Monday.Should().Be(new DateOnly(2026, 6, 22));
        result.Friday.Should().Be(new DateOnly(2026, 6, 26));
        result.Data.Tasks.Should().HaveCount(2);
        result.Data.Tasks.Should().OnlyContain(t => t.Done);   // 시트엔 완료여부 없음 → 전부 완료
        result.Data.Tasks.Should().Contain(t => t.Text == "SLD 점검" && t.ClientId == sldId);
        result.Data.Issues.Should().ContainSingle(i => i.Text == "장비 오류");
        result.UnclassifiedTaskCount.Should().Be(1);
    }
```

- [ ] **Step 2: 실패 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~WeeklyReportServiceTests" 2>&1 | tail -8
```
기대: 컴파일 실패(`BuildFromTexts` 없음) — FakeWeeklyReportService 미구현 컴파일 오류 포함될 수 있음(다음 스텝에서 함께 해결).

- [ ] **Step 3: 인터페이스에 메서드 추가** — `IWeeklyReportService.cs`, `Render` 선언 아래에 추가

```csharp
    /// 시트 등 텍스트 목록에서 빌드(자동 분류, Done=true). 체크리스트 경로와 병존.
    WeeklyReportBuildResult BuildFromTexts(
        IReadOnlyList<string> taskTexts, IReadOnlyList<string> issueTexts,
        DateOnly monday, DateOnly friday, ReportRenderOptions options);
```

- [ ] **Step 4: 서비스 구현** — `WeeklyReportService.cs`, `Render` 메서드 아래에 추가

```csharp
    public WeeklyReportBuildResult BuildFromTexts(
        IReadOnlyList<string> taskTexts, IReadOnlyList<string> issueTexts,
        DateOnly monday, DateOnly friday, ReportRenderOptions options)
    {
        var rules = _clients.GetRules();
        var enabledIds = _clients.GetAll(enabledOnly: true).Select(c => c.Id).ToHashSet();

        var tasks = taskTexts
            .Select(t => new ReportTask(t, _classifier.Classify(t, rules, enabledIds), Done: true))
            .ToList();
        var issues = issueTexts.Select(t => new ReportIssue(t)).ToList();

        var relevant = options.IncludeDoneOnly ? tasks.Where(t => t.Done) : tasks;
        int unclassified = relevant.Count(t => t.ClientId is null);

        var data = new WeeklyReportData(tasks, issues);
        return new WeeklyReportBuildResult(data, unclassified, monday, friday);
    }
```

- [ ] **Step 5: FakeWeeklyReportService에 구현 추가** — `WeeklyReportFakes.cs`의 `FakeWeeklyReportService` 클래스에 추가(테스트 컴파일 통과용, 호출 기록)

```csharp
    public IReadOnlyList<string>? LastTaskTexts { get; private set; }
    public IReadOnlyList<string>? LastIssueTexts { get; private set; }
    public int BuildFromTextsCallCount { get; private set; }

    public WeeklyReportBuildResult BuildFromTexts(
        IReadOnlyList<string> taskTexts, IReadOnlyList<string> issueTexts,
        DateOnly monday, DateOnly friday, ReportRenderOptions options)
    {
        BuildFromTextsCallCount++;
        LastTaskTexts = taskTexts;
        LastIssueTexts = issueTexts;
        LastOptions = options;
        return new WeeklyReportBuildResult(
            new WeeklyReportData(
                taskTexts.Select(t => new ReportTask(t, null, true)).ToList(),
                issueTexts.Select(t => new ReportIssue(t)).ToList()),
            0, monday, friday);
    }
```

- [ ] **Step 6: 통과 확인 (신규 + 기존 서비스/VM 테스트)**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~WeeklyReport" 2>&1 | tail -8
```
기대: PASS(신규 BuildFromTexts + 기존 서비스/렌더러/VM 테스트).

- [ ] **Step 7: 커밋**

```bash
git add src/Memoria.Core/Services/IWeeklyReportService.cs src/Memoria.Core/Services/WeeklyReportService.cs tests/Memoria.Tests/Services/WeeklyReportServiceTests.cs tests/Memoria.Tests/ViewModels/WeeklyReportFakes.cs
git commit -m "feat(sheets): WeeklyReportService.BuildFromTexts (classify text lists)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## GM3: ISpreadsheetReader 계약 + 설정 키 (Core, 선언)

**Files:**
- Create: `src/Memoria.Core/Sheets/ISpreadsheetReader.cs`
- Modify: `src/Memoria.Core/SettingsKeys.cs`

**Interfaces:**
- Produces: `Task<IReadOnlyList<IReadOnlyList<string>>> ISpreadsheetReader.ReadRowsAsync(string sheetId, string tabName, CancellationToken ct = default)`; `SettingsKeys.GoogleServiceAccountJsonPath`/`GoogleSheetId`/`GoogleSheetTabName`.

> 순수 선언(동작 없음)이라 단위 테스트 없음. 뒤 태스크(GM4 구현, GM5 소비)가 이 계약을 사용한다.

- [ ] **Step 1: 인터페이스 생성** — `src/Memoria.Core/Sheets/ISpreadsheetReader.cs`

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace Memoria.Core.Sheets;

/// <summary>스프레드시트 셀 격자를 읽는 계약(구글 의존성 없음). 실패 시 예외.</summary>
public interface ISpreadsheetReader
{
    Task<IReadOnlyList<IReadOnlyList<string>>> ReadRowsAsync(
        string sheetId, string tabName, CancellationToken ct = default);
}
```

- [ ] **Step 2: 설정 키 추가** — `SettingsKeys.cs`, `AutosaveDebounceMs` 아래에 추가

```csharp
    public const string GoogleServiceAccountJsonPath = "google.serviceAccountJsonPath";
    public const string GoogleSheetId = "google.sheetId";
    public const string GoogleSheetTabName = "google.sheetTabName";
```

- [ ] **Step 3: 빌드 확인 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -4
git add src/Memoria.Core/Sheets/ISpreadsheetReader.cs src/Memoria.Core/SettingsKeys.cs
git commit -m "feat(sheets): ISpreadsheetReader contract + google.* setting keys

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## GM4: Google.Apis + GoogleSheetReader (App, 빌드 게이트)

**Files:**
- Modify: `src/Memoria.App/Memoria.App.csproj`
- Create: `src/Memoria.App/Services/GoogleSheetReader.cs`
- Modify: `src/Memoria.App/App.xaml.cs` (DI)

**Interfaces:**
- Consumes: `ISpreadsheetReader` (GM3), `ISettingsRepository.GetOrDefault` (기존), `SettingsKeys.GoogleServiceAccountJsonPath` (GM3).

> 네트워크/인증이라 자동 테스트 불가. 검증 = 패키지 복원 + 빌드 경고0/오류0. 실제 fetch는 GM8 GUI.

- [ ] **Step 1: Google 패키지 추가** — `Memoria.App.csproj`의 PackageReference ItemGroup에 추가

```xml
    <PackageReference Include="Google.Apis.Sheets.v4" Version="1.68.0.3421" />
```
> 복원 실패 시 최신 `Google.Apis.Sheets.v4` 안정 버전으로. 확인: `dotnet.exe restore "Memoria.sln" 2>&1 | tail -3`. (Google.Apis.Auth 등은 전이 의존으로 함께 복원.)

- [ ] **Step 2: 리더 구현** — `src/Memoria.App/Services/GoogleSheetReader.cs`

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Memoria.Core;
using Memoria.Core.Data;
using Memoria.Core.Sheets;

namespace Memoria.App.Services;

/// <summary>서비스 계정 JSON으로 인증 후 Sheets API(A:C, 읽기 전용)로 셀 격자를 읽는다.</summary>
public sealed class GoogleSheetReader : ISpreadsheetReader
{
    private readonly ISettingsRepository _settings;
    public GoogleSheetReader(ISettingsRepository settings) => _settings = settings;

    public async Task<IReadOnlyList<IReadOnlyList<string>>> ReadRowsAsync(
        string sheetId, string tabName, CancellationToken ct = default)
    {
        var jsonPath = _settings.GetOrDefault(SettingsKeys.GoogleServiceAccountJsonPath, "");
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            throw new InvalidOperationException("서비스 계정 JSON 키 경로가 설정되지 않았거나 파일이 없습니다.");

        var credential = GoogleCredential.FromFile(jsonPath)
            .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);

        using var service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Memoria",
        });

        var request = service.Spreadsheets.Values.Get(sheetId, $"{tabName}!A:C");
        var response = await request.ExecuteAsync(ct).ConfigureAwait(false);

        var grid = new List<IReadOnlyList<string>>();
        if (response.Values is null) return grid;
        foreach (var row in response.Values)
        {
            var cells = new List<string>(row.Count);
            foreach (var cell in row) cells.Add(cell?.ToString() ?? "");
            grid.Add(cells);
        }
        return grid;
    }
}
```

- [ ] **Step 3: DI 등록** — `App.xaml.cs`, `IMarkdownRenderer` 등록 근처에 추가

```csharp
        sc.AddSingleton<Memoria.Core.Sheets.ISpreadsheetReader>(
            sp => new Memoria.App.Services.GoogleSheetReader(sp.GetRequiredService<ISettingsRepository>()));
```

- [ ] **Step 4: 빌드 확인 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -6
git add src/Memoria.App/Memoria.App.csproj src/Memoria.App/Services/GoogleSheetReader.cs src/Memoria.App/App.xaml.cs
git commit -m "feat(sheets): GoogleSheetReader (service-account, readonly A:C) + DI

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## GM5: WeeklyReportViewModel — 시트 생성 커맨드 (App, TDD via fake reader)

**Files:**
- Modify: `src/Memoria.App/ViewModels/WeeklyReportViewModel.cs`
- Modify: `tests/Memoria.Tests/ViewModels/WeeklyReportFakes.cs` (FakeSpreadsheetReader 추가)
- Test: `tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs` (추가) — 기존 VM 생성부에 리더 인자 추가

**Interfaces:**
- Consumes: `ISpreadsheetReader.ReadRowsAsync` (GM3), `SheetWorkParser.Parse` (GM1), `IWeeklyReportService.BuildFromTexts` (GM2), `SettingsKeys.GoogleSheetId`/`GoogleSheetTabName` (GM3).
- Produces: `WeeklyReportViewModel.GenerateFromSheetCommand` (async `IAsyncRelayCommand`); 생성자에 `ISpreadsheetReader` 인자 추가(마지막 파라미터).

- [ ] **Step 1: FakeSpreadsheetReader 추가** — `WeeklyReportFakes.cs` 끝에 추가

```csharp
internal sealed class FakeSpreadsheetReader : Memoria.Core.Sheets.ISpreadsheetReader
{
    public IReadOnlyList<IReadOnlyList<string>> Grid { get; set; } =
        new List<IReadOnlyList<string>>();
    public string? LastSheetId { get; private set; }
    public string? LastTabName { get; private set; }
    public int CallCount { get; private set; }
    public System.Exception? Throw { get; set; }

    public System.Threading.Tasks.Task<IReadOnlyList<IReadOnlyList<string>>> ReadRowsAsync(
        string sheetId, string tabName, System.Threading.CancellationToken ct = default)
    {
        CallCount++;
        LastSheetId = sheetId;
        LastTabName = tabName;
        if (Throw is not null) throw Throw;
        return System.Threading.Tasks.Task.FromResult(Grid);
    }
}
```

- [ ] **Step 2: 실패 테스트 추가** — `WeeklyReportViewModelTests.cs`

먼저 이 파일의 기존 VM 생성 헬퍼를 확인하고, `WeeklyReportViewModel` 생성자에 **마지막 인자로 `FakeSpreadsheetReader`**를 넘기도록 갱신한다(기존 테스트도 함께 갱신). 그 후 아래 테스트 추가:

```csharp
    [Fact]
    public async Task GenerateFromSheet_ReadsGrid_ParsesWeek_BuildsAndSetsReportText()
    {
        var settings = new Memoria.Tests.Fakes.FakeSettingsRepository();
        settings.Set(Memoria.Core.SettingsKeys.GoogleSheetId, "SHEET123");
        settings.Set(Memoria.Core.SettingsKeys.GoogleSheetTabName, "일자 작업내역");
        var reader = new FakeSpreadsheetReader
        {
            // 헤더 + SelectedDate가 속한 주 안의 한 행.
            Grid = new List<IReadOnlyList<string>>
            {
                new List<string> { "일자", "작업내역", "특이사항" },
                new List<string> { "2026.06.24 (수)", "1. SLD 점검", "1. 장비 오류" },
            },
        };
        var svc = new FakeWeeklyReportService { RenderResult = "SHEET-REPORT" };
        var vm = NewVm(settings, svc, reader);   // NewVm: 이 파일의 생성 헬퍼(리더 인자 추가)
        vm.SelectedDate = new DateOnly(2026, 6, 24);   // 월=6/22

        await vm.GenerateFromSheetCommand.ExecuteAsync(null);

        reader.CallCount.Should().Be(1);
        reader.LastSheetId.Should().Be("SHEET123");
        svc.LastTaskTexts.Should().Equal("SLD 점검");
        svc.LastIssueTexts.Should().Equal("장비 오류");
        vm.ReportText.Should().Be("SHEET-REPORT");
    }
```
> `NewVm(settings, svc, reader)`는 이 파일의 기존 생성 헬퍼를 확장한 형태다. 기존 헬퍼 시그니처에 맞춰 리더를 주입하도록 갱신하고, 다른 인자(week calc/repos/clipboard/dialogs/time)는 기존 fake를 재사용한다.

- [ ] **Step 3: 실패 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~WeeklyReportViewModelTests" 2>&1 | tail -8
```
기대: 컴파일 실패(생성자 인자/커맨드 없음).

- [ ] **Step 4: VM 수정** — `WeeklyReportViewModel.cs`

using 추가: `using Memoria.Core.Sheets;` `using System.Threading.Tasks;`

필드 + 생성자 인자 추가(기존 필드 `_timeProvider` 옆 / 생성자 마지막 파라미터):
```csharp
    private readonly ISpreadsheetReader _sheetReader;
```
생성자 시그니처 마지막에 `ISpreadsheetReader sheetReader` 추가하고 본문에 `_sheetReader = sheetReader;` 추가.

커맨드 추가(`Generate` 근처):
```csharp
    [RelayCommand]
    private async Task GenerateFromSheet()
    {
        var monday = WeekStart;
        var friday = WeekEnd;
        var sheetId = _settings.GetOrDefault(SettingsKeys.GoogleSheetId, "");
        var tabName = _settings.GetOrDefault(SettingsKeys.GoogleSheetTabName, "일자 작업내역");
        if (string.IsNullOrWhiteSpace(sheetId))
        {
            _dialogs.Confirm("구글 시트 ID가 설정되지 않았습니다. 설정 > 구글 연동에서 입력하세요.");
            return;
        }
        try
        {
            var grid = await _sheetReader.ReadRowsAsync(sheetId, tabName);
            var parsed = SheetWorkParser.Parse(grid, monday, friday);
            var options = BuildOptions(monday, friday);
            var build = _reportService.BuildFromTexts(parsed.Tasks, parsed.Issues, monday, friday, options);
            UnclassifiedTaskCount = build.UnclassifiedTaskCount;
            var text = _reportService.Render(SelectedFormat, build.Data, options);
            ReportText = text;
            var existing = _noteRepository.FindWeeklyReport(monday, SelectedFormat);
            Persist(monday, existing, text);
        }
        catch (System.Exception ex)
        {
            _dialogs.Confirm($"구글 시트에서 가져오지 못했습니다: {ex.Message}");
        }
    }
```
> 에러 표시는 기존 `IConfirmationDialogService.Confirm`로 메시지를 띄운다(전용 알림 서비스 없음). 네트워크 fetch만 async이고 이후는 동기.

- [ ] **Step 5: 통과 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~WeeklyReportViewModelTests" 2>&1 | tail -8
```
기대: PASS(신규 + 갱신된 기존 VM 테스트).

- [ ] **Step 6: 전체 빌드 확인 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -5
git add src/Memoria.App/ViewModels/WeeklyReportViewModel.cs tests/Memoria.Tests/ViewModels/WeeklyReportFakes.cs tests/Memoria.Tests/ViewModels/WeeklyReportViewModelTests.cs
git commit -m "feat(sheets): WeeklyReportViewModel.GenerateFromSheet (async)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## GM6: 설정 UI — 구글 연동 섹션 (App, 빌드 게이트)

**Files:**
- Modify: `src/Memoria.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/Memoria.App/Views/SettingsWindow.xaml` (+ `.xaml.cs`)

**Interfaces:**
- Consumes: `SettingsKeys.Google*` (GM3), `ISettingsRepository` (기존).
- Produces: `SettingsViewModel.ServiceAccountJsonPath`/`SheetId`/`SheetTabName` (ObservableProperty), Load/Save 연동.

> WPF UI + 파일 다이얼로그 → 빌드 게이트. 검증 = 빌드 경고0/오류0 + GM8 GUI.

- [ ] **Step 1: SettingsViewModel 프로퍼티 추가** — `IncludeDoneOnly` 옆에 추가

```csharp
    [ObservableProperty] private string _serviceAccountJsonPath = "";
    [ObservableProperty] private string _sheetId = "";
    [ObservableProperty] private string _sheetTabName = "일자 작업내역";
```

- [ ] **Step 2: Load()에 로드 추가** — `Load()`의 report 설정 로드 뒤에 추가

```csharp
        ServiceAccountJsonPath = _settings.GetOrDefault(SettingsKeys.GoogleServiceAccountJsonPath, "");
        SheetId = _settings.GetOrDefault(SettingsKeys.GoogleSheetId, "");
        SheetTabName = _settings.GetOrDefault(SettingsKeys.GoogleSheetTabName, "일자 작업내역");
```

- [ ] **Step 3: Save()에 저장 추가** — `Save()`의 report 설정 저장 뒤에 추가

```csharp
        _settings.Set(SettingsKeys.GoogleServiceAccountJsonPath, ServiceAccountJsonPath ?? "");
        _settings.Set(SettingsKeys.GoogleSheetId, SheetId ?? "");
        _settings.Set(SettingsKeys.GoogleSheetTabName, string.IsNullOrWhiteSpace(SheetTabName) ? "일자 작업내역" : SheetTabName);
```

- [ ] **Step 4: 설정 창에 '구글 연동' 탭 추가** — `SettingsWindow.xaml`, `앱` TabItem 뒤(마지막 `</TabControl>` 앞)에 추가

```xml
        <TabItem Header="구글 연동">
            <StackPanel Margin="12">
                <TextBlock Text="서비스 계정 JSON 키 경로" />
                <StackPanel Orientation="Horizontal">
                    <TextBox Text="{Binding ServiceAccountJsonPath}" Width="360" />
                    <Button Content="찾아보기…" Margin="6,0,0,0" Click="OnBrowseJsonClick" />
                </StackPanel>
                <TextBlock Text="스프레드시트 ID (시트 URL의 /d/{ID}/ 부분)" Margin="0,8,0,0" />
                <TextBox Text="{Binding SheetId}" />
                <TextBlock Text="탭(시트) 이름" Margin="0,8,0,0" />
                <TextBox Text="{Binding SheetTabName}" Width="220" HorizontalAlignment="Left" />
                <TextBlock Text="※ 구글 클라우드에서 서비스 계정 JSON 키를 만들고, 이 시트를 서비스 계정 이메일과 '뷰어'로 공유하세요."
                           TextWrapping="Wrap" Margin="0,8,0,0"
                           Foreground="{DynamicResource Brush.SecondaryForeground}" />
            </StackPanel>
        </TabItem>
```

- [ ] **Step 5: 파일 선택 핸들러 추가** — `SettingsWindow.xaml.cs`

```csharp
    private void OnBrowseJsonClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON 키|*.json|모든 파일|*.*" };
        if (dlg.ShowDialog() == true && DataContext is Memoria.App.ViewModels.SettingsViewModel vm)
            vm.ServiceAccountJsonPath = dlg.FileName;
    }
```
> `DataContext`가 SettingsViewModel인지 확인 후 설정. 기존 SettingsWindow의 DataContext 배선 방식을 따를 것.

- [ ] **Step 6: 빌드 확인 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -5
git add src/Memoria.App/ViewModels/SettingsViewModel.cs src/Memoria.App/Views/SettingsWindow.xaml src/Memoria.App/Views/SettingsWindow.xaml.cs
git commit -m "feat(sheets): google integration settings tab (json path/sheet id/tab)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## GM7: 주간보고 뷰 — 구글 시트 생성 버튼 (App, 빌드 게이트)

**Files:**
- Modify: `src/Memoria.App/Views/WeeklyReportView.xaml`

**Interfaces:**
- Consumes: `WeeklyReportViewModel.GenerateFromSheetCommand` (GM5).

> 스펙 §4.3의 '소스 선택'을 **명시적 액션 버튼**으로 실현(라디오/기본소스 대신, 기존 생성/다시생성/복사 버튼 UI와 일관). WPF → 빌드 게이트.

- [ ] **Step 1: 버튼 추가** — `WeeklyReportView.xaml`의 상단 WrapPanel, `복사` 버튼 뒤에 추가

```xml
            <Button Content="구글 시트에서 생성" Command="{Binding GenerateFromSheetCommand}"
                    Margin="0,0,4,4" Padding="10,2" />
```

- [ ] **Step 2: 빌드 확인 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -5
git add src/Memoria.App/Views/WeeklyReportView.xaml
git commit -m "feat(sheets): weekly report view '구글 시트에서 생성' button

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## GM8: 통합 — 전체 빌드·테스트·퍼블리시 + GUI 체크리스트

**Files:** 없음(검증 단계)

- [ ] **Step 1: 전체 빌드 + 전체 테스트**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null
dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -6
dotnet.exe test "tests/Memoria.Tests" -c Release 2>&1 | tail -4
```
기대: 경고0/오류0, 실패0 / 통과(기존 329 + 신규 ~10).

- [ ] **Step 2: 자체 포함 단일 파일 퍼블리시** (Google.Apis 포함 단일파일 동작 확인)

```bash
dotnet.exe publish "src/Memoria.App/Memoria.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish 2>&1 | tail -3
ls -la --time-style=+%H:%M publish/Memoria.exe
```

- [ ] **Step 3: 사용자 GUI 검증** — `publish/Memoria.exe`:
  1. 설정 > 구글 연동: JSON 경로 찾아보기·시트ID·탭명 입력 후 저장 → 재열기 시 유지.
  2. (사전) 구글 클라우드 서비스계정 JSON 준비 + 시트를 서비스계정 이메일과 뷰어 공유.
  3. 주간보고 뷰 → 주 선택 → **구글 시트에서 생성** → 해당 주 업무/이슈가 포맷 A/B로 렌더.
  4. 양식 A/B 전환 시 출력 형태 확인, 미분류 경고 배너 동작.
  5. 오류 케이스: JSON 경로 없음/시트 미공유/시트ID 오타 → **크래시 없이** 메시지.
  6. 시트가 수정되지 않는지(읽기 전용) 확인.
  7. 기존 체크리스트 기반 생성/다시생성도 그대로 동작(비파괴).

- [ ] **Step 4: (사용자 통과 후) finishing-a-development-branch로 병합 + v0.4.0 릴리스**

---

## Self-Review (작성자 점검 결과)

- **스펙 커버리지**: §4.1 파싱→GM1, §4.3 통합(BuildFromTexts/분류·렌더 재사용)→GM2/GM5, §4.2 리더→GM3(계약)/GM4(구현), §4.4 설정→GM3(키)/GM6(UI), §4.5 오류→GM5(try/catch+메시지)/GM1(행 스킵), §7 테스트→GM1/GM2/GM5 자동 + GM8 수동. 전 항목 매핑.
- **스펙 대비 의식적 단순화**: 스펙 §4.3 "소스 선택(라디오+기본)"을 GM7에서 **명시적 '구글 시트에서 생성' 버튼**으로 축소(YAGNI — 기존 버튼 UI와 일관, 죽은 상태 프로퍼티 회피). 리뷰어가 spec 편차로 볼 수 있으니 명시.
- **플레이스홀더**: 없음. GM3는 순수 선언이라 테스트 없음(명시).
- **타입 일관성**: `ParsedWeek(Tasks, Issues)`, `SheetWorkParser.Parse/TryParseDate`, `ISpreadsheetReader.ReadRowsAsync`, `IWeeklyReportService.BuildFromTexts(taskTexts, issueTexts, monday, friday, options)`, `SettingsKeys.Google*`, `GenerateFromSheetCommand` — GM 간 명칭/시그니처 일치.
- **주의**: Google.Apis.Sheets.v4 버전 복원 실패 시 최신 안정으로. 단일파일 퍼블리시는 `PublishTrimmed=false`(release.yml)라 리플렉션 기반 Google 라이브러리 OK. `WeeklyReportViewModel` 생성자 인자 추가 → 기존 VM 테스트 생성 헬퍼 갱신 필수(GM5).

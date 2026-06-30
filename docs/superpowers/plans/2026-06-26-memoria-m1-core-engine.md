# Memoria Core Engine (M1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Memoria의 모든 순수 로직(도메인 모델, 고객사 분류, 주차 계산, 주간보고 양식 A/B 렌더, SQLite 영속성·마이그레이션·시드·FTS5 검색, 태깅/주간보고 오케스트레이션)을 UI 없이 100% 자동 테스트로 구현한다.

**Architecture:** `Memoria.Core`(net9.0, 윈도우 비의존) 한 어셈블리에 Models / Classification / Reporting / Data / Services 네임스페이스를 두고, 인터페이스 계약 문서의 시그니처를 정확히 구현한다. 영속성은 `Microsoft.Data.Sqlite` + `Dapper`(커스텀 TypeHandler로 enum→문자열, DateOnly/DateTimeOffset→ISO-8601 매핑)로 처리하며, 전문검색은 FTS5 가상 테이블 + 동기화 트리거로 구현한다. 모든 검증은 `Memoria.Tests`(net9.0-windows, xUnit + FluentAssertions)에서 Windows `dotnet.exe`로 실행한다.

**Tech Stack:** C# / .NET 9, Microsoft.Data.Sqlite, Dapper, Microsoft.Extensions.DependencyInjection(.Abstractions), xUnit, FluentAssertions, Microsoft.NET.Test.Sdk. (App/WPF·CommunityToolkit.Mvvm는 M2에서 도입; 본 마일스톤은 Core+Tests만 생성.)

## Global Constraints
- 런타임: **.NET 9**.
- TFM: **Core = net9.0**, **App = net9.0-windows**(M2에서 생성), **Tests = net9.0-windows**.
- DB 위치(런타임): `%LOCALAPPDATA%\Memoria\memoria.db` (테스트는 임시 파일 경로 사용).
- WPF publish는 **트리밍/압축 금지**(M1과 무관하나 솔루션 정책으로 명기) — `PublishTrimmed`/`EnableCompressionInSingleFile` 금지.
- 빌드/테스트는 **Windows `dotnet.exe`** + **Windows 절대경로**로 수행(WSL 호출 시에도 동일). 저장소 Windows 경로: `C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria`.
- 고객사 분류 우선순위: **`자율형공장` > `SLD`** (자율형공장 키워드가 있으면 자율형 공장으로, 없고 SLD만 있으면 SLD). 규칙은 `priority` 오름차순 첫 매칭, **대소문자 무시**, **비활성 고객사 규칙 제외**.
- 양식 A: `[업무 내용]` 블록과 `[이슈]` 머리글 사이 **빈 줄 정확히 1개**.
- 양식 B: 제목줄 `[ {이름} {주간 보고} (MM/dd ~ MM/dd) ]:`(0 포함 2자리 날짜, ` ~ ` 구분, 끝 콜론, 📢 미출력), 고객사 섹션은 표시순(SortOrder)으로 **빈 섹션 머리글도 출력**, `[ 미분류 ]`는 미분류 task가 1개 이상일 때만 이슈 섹션 직전 출력, 이슈는 `* 이슈사항:` 뒤 나열.
- 들여쓰기 = 탭 1개(`\t`), 글머리 = `* `.
- `IncludeDoneOnly`(기본 false): true면 `Kind=Task` 중 `Done=true`만 렌더, **Issue는 옵션과 무관하게 항상 전부**.
- 모든 색상은 `DynamicResource`로(M1과 무관, 솔루션 정책으로 명기 — Core는 색상 비의존).
- 날짜: `DateOnly`(달력일) / `DateTimeOffset`(타임스탬프, UTC 저장·ISO-8601 문자열). enum은 DB에 **문자열**로 저장(`type`=plain|checklist|weekly_report, `kind`=task|issue, `report_format`=A|B).
- **단일 직렬 라이터(스펙 §7.7 / 계약 §8):** `SqliteConnectionFactory`는 **단일 영속 쓰기 연결**(`Write`)과 **`object WriteSync` 락**을 노출한다. 모든 쓰기(리포지토리 INSERT/UPDATE/DELETE, 마이그레이션/시드, 백업 `VACUUM INTO`)는 `lock (factory.WriteSync)` 안에서 `factory.Write` 연결로 수행해 `SQLITE_BUSY`를 회피한다. 읽기는 `factory.Open()`이 반환하는 별도 연결로 WAL 동시 읽기를 허용한다. `SqliteConnectionFactory`는 `IDisposable`이며 Dispose 시 `PRAGMA wal_checkpoint(TRUNCATE)` 후 쓰기 연결을 닫는다.

---
### Task 1: 솔루션/프로젝트 스캐폴딩 + 도메인 모델

**Files:**
- Create: `Memoria.sln`, `src/Memoria.Core/Memoria.Core.csproj`, `src/Memoria.Core/Models/Models.cs`, `tests/Memoria.Tests/Memoria.Tests.csproj`
- Test: `tests/Memoria.Tests/Models/ModelsTests.cs`

**Interfaces:**
- Consumes: (없음 — 최초 마일스톤)
- Produces: 솔루션 구조 + `Memoria.Core.Models`의 `NoteType`, `ItemKind`, `ReportFormatKind`, `ThemeMode`, `Group`, `Note`, `ChecklistItem`, `Client`, `ClientRule` (계약 §1 정확히).

- [ ] **Step 1: Write the failing test**

먼저 솔루션/프로젝트를 생성한다(스캐폴딩 명령은 코드가 아니라 셸 명령이므로 Step 1에서 실행하고, 그 다음 실패 테스트를 작성한다).

```bash
# 솔루션 + 프로젝트 생성 (Windows dotnet.exe, Windows 절대경로)
dotnet.exe new sln -n Memoria -o "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria"
dotnet.exe new classlib -n Memoria.Core -o "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\src\Memoria.Core"
dotnet.exe new xunit  -n Memoria.Tests -o "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests"

# 솔루션에 추가
dotnet.exe sln "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln" add "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\src\Memoria.Core\Memoria.Core.csproj"
dotnet.exe sln "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln" add "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests\Memoria.Tests.csproj"

# 참조 + NuGet
dotnet.exe add "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests\Memoria.Tests.csproj" reference "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\src\Memoria.Core\Memoria.Core.csproj"
dotnet.exe add "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\src\Memoria.Core\Memoria.Core.csproj" package Microsoft.Data.Sqlite
dotnet.exe add "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\src\Memoria.Core\Memoria.Core.csproj" package Dapper
dotnet.exe add "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests\Memoria.Tests.csproj" package Microsoft.Data.Sqlite
dotnet.exe add "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests\Memoria.Tests.csproj" package FluentAssertions --version 7.2.0
```

`tests/Memoria.Tests/Memoria.Tests.csproj`의 TFM을 `net9.0` → `net9.0-windows`로 바꾼다(계약 §0, App ViewModel 테스트 호환). 기본 classlib 더미 파일 `src/Memoria.Core/Class1.cs`는 삭제한다. (csproj 편집은 Edit 도구로 `<TargetFramework>net9.0</TargetFramework>` → `<TargetFramework>net9.0-windows</TargetFramework>`.)

이제 모델을 검증하는 실패 테스트를 작성한다.

`tests/Memoria.Tests/Models/ModelsTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Models;

public class ModelsTests
{
    [Fact]
    public void Enums_HaveExpectedMembers()
    {
        Enum.GetNames<NoteType>().Should().BeEquivalentTo("Plain", "Checklist", "WeeklyReport");
        Enum.GetNames<ItemKind>().Should().BeEquivalentTo("Task", "Issue");
        Enum.GetNames<ReportFormatKind>().Should().BeEquivalentTo("A", "B");
        Enum.GetNames<ThemeMode>().Should().BeEquivalentTo("Light", "Dark", "System");
    }

    [Fact]
    public void Note_DefaultsAndAssignment_Work()
    {
        var note = new Note
        {
            Type = NoteType.Checklist,
            LogDate = new DateOnly(2026, 6, 22),
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

        note.GroupId.Should().BeNull();
        note.DeletedAt.Should().BeNull();
        note.Type.Should().Be(NoteType.Checklist);
        note.LogDate.Should().Be(new DateOnly(2026, 6, 22));
    }

    [Fact]
    public void Client_DefaultEnabled_IsTrue()
    {
        new Client().Enabled.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0234: The type or namespace name 'Models' does not exist in the namespace 'Memoria.Core'` (모델 미구현으로 컴파일 실패).

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Models/Models.cs` (계약 §1 그대로):
```csharp
namespace Memoria.Core.Models;

public enum NoteType { Plain, Checklist, WeeklyReport }
public enum ItemKind { Task, Issue }
public enum ReportFormatKind { A, B }
public enum ThemeMode { Light, Dark, System }

public sealed class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
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
    public DateOnly? LogDate { get; set; }
    public ReportFormatKind? ReportFormat { get; set; }
    public DateOnly? ReportWeekStart { get; set; }
    public bool Pinned { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
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
    public int? ClientId { get; set; }
    public bool IsManual { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class ClientRule
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Keyword { get; set; } = "";
    public int Priority { get; set; }
}
```

`Memoria.Core.csproj`에 `<ImplicitUsings>enable</ImplicitUsings>`, `<Nullable>enable</Nullable>`가 켜져 있는지 확인한다(.NET 9 classlib 기본값). `System`이 implicit using으로 들어와 `DateOnly`/`DateTimeOffset` 사용 가능.

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~ModelsTests"
```
예상: `Passed!  - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "chore: scaffold solution and Memoria.Core domain models

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---
### Task 2: 주차 계산 `IWeekCalculator`

**Files:**
- Create: `src/Memoria.Core/Classification/WeekCalculator.cs`, `src/Memoria.Core/Classification/IWeekCalculator.cs`
- Test: `tests/Memoria.Tests/Classification/WeekCalculatorTests.cs`

**Interfaces:**
- Consumes: (없음)
- Produces: `Memoria.Core.Classification.IWeekCalculator.GetWorkWeek(DateOnly) -> (DateOnly Monday, DateOnly Friday)` (계약 §2).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Classification/WeekCalculatorTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core.Classification;
using Xunit;

namespace Memoria.Tests.Classification;

public class WeekCalculatorTests
{
    private readonly IWeekCalculator _calc = new WeekCalculator();

    [Fact]
    public void Friday_ReturnsSameWeekMondayToFriday()
    {
        // 2026-06-26 은 금요일
        var (monday, friday) = _calc.GetWorkWeek(new DateOnly(2026, 6, 26));
        monday.Should().Be(new DateOnly(2026, 6, 22));
        friday.Should().Be(new DateOnly(2026, 6, 26));
    }

    [Fact]
    public void Monday_ReturnsItselfAsMonday()
    {
        var (monday, friday) = _calc.GetWorkWeek(new DateOnly(2026, 6, 22));
        monday.Should().Be(new DateOnly(2026, 6, 22));
        friday.Should().Be(new DateOnly(2026, 6, 26));
    }

    [Fact]
    public void Sunday_BelongsToWeekStartedPreviousMonday()
    {
        // 2026-06-28 은 일요일 → 그 주는 06-22(월)~06-26(금)
        var (monday, friday) = _calc.GetWorkWeek(new DateOnly(2026, 6, 28));
        monday.Should().Be(new DateOnly(2026, 6, 22));
        friday.Should().Be(new DateOnly(2026, 6, 26));
    }

    [Fact]
    public void YearBoundary_WeekSpansNewYear()
    {
        // 2026-12-31 은 목요일 → 월 2026-12-28, 금 2027-01-01
        var (monday, friday) = _calc.GetWorkWeek(new DateOnly(2026, 12, 31));
        monday.Should().Be(new DateOnly(2026, 12, 28));
        friday.Should().Be(new DateOnly(2027, 1, 1));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'IWeekCalculator' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Classification/IWeekCalculator.cs`:
```csharp
namespace Memoria.Core.Classification;

public interface IWeekCalculator
{
    /// 임의 날짜가 속한 주의 (월요일, 금요일) 반환.
    (DateOnly Monday, DateOnly Friday) GetWorkWeek(DateOnly anyDate);
}
```

`src/Memoria.Core/Classification/WeekCalculator.cs`:
```csharp
namespace Memoria.Core.Classification;

public sealed class WeekCalculator : IWeekCalculator
{
    public (DateOnly Monday, DateOnly Friday) GetWorkWeek(DateOnly anyDate)
    {
        // DayOfWeek: Sunday=0 .. Saturday=6. 월요일 기준 경과일 = ((int)dow + 6) % 7.
        int daysSinceMonday = ((int)anyDate.DayOfWeek + 6) % 7;
        DateOnly monday = anyDate.AddDays(-daysSinceMonday);
        DateOnly friday = monday.AddDays(4);
        return (monday, friday);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeekCalculatorTests"
```
예상: `Passed!  - Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add WeekCalculator (Mon-Fri work week)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: 고객사 분류 `IClientClassifier`

**Files:**
- Create: `src/Memoria.Core/Classification/IClientClassifier.cs`, `src/Memoria.Core/Classification/ClientClassifier.cs`
- Test: `tests/Memoria.Tests/Classification/ClientClassifierTests.cs`

**Interfaces:**
- Consumes: `Memoria.Core.Models.ClientRule`.
- Produces: `Memoria.Core.Classification.IClientClassifier.Classify(string, IEnumerable<ClientRule>, ISet<int>) -> int?` (계약 §2).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Classification/ClientClassifierTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core.Classification;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Classification;

public class ClientClassifierTests
{
    // 고객사 id: SLD=1, MTP=2, 코모텍=3, 충북=4, 자율형 공장=5, 카본센스=6
    private static readonly List<ClientRule> Rules =
    [
        new() { ClientId = 5, Keyword = "자율형공장", Priority = 1 },
        new() { ClientId = 5, Keyword = "자율형 공장", Priority = 1 },
        new() { ClientId = 4, Keyword = "충북", Priority = 2 },
        new() { ClientId = 4, Keyword = "DL정보기술", Priority = 2 },
        new() { ClientId = 3, Keyword = "코모텍", Priority = 3 },
        new() { ClientId = 2, Keyword = "MTP", Priority = 4 },
        new() { ClientId = 5, Keyword = "머티리얼즈파크", Priority = 4 },
        new() { ClientId = 6, Keyword = "카본센스", Priority = 5 },
        new() { ClientId = 1, Keyword = "SLD", Priority = 6 },
    ];

    private static readonly HashSet<int> AllEnabled = [1, 2, 3, 4, 5, 6];
    private readonly IClientClassifier _sut = new ClientClassifier();

    [Fact]
    public void AutonomousFactory_BeatsSld_WhenBothPresent()
    {
        _sut.Classify("SLD 자율형공장 정리", Rules, AllEnabled).Should().Be(5);
    }

    [Fact]
    public void Sld_WhenOnlySldPresent()
    {
        _sut.Classify("SLD 점검", Rules, AllEnabled).Should().Be(1);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        _sut.Classify("mtp 라인 작업", Rules, AllEnabled).Should().Be(2);
    }

    [Fact]
    public void NoKeyword_ReturnsNull()
    {
        _sut.Classify("기타 잡무 정리", Rules, AllEnabled).Should().BeNull();
    }

    [Fact]
    public void DisabledClientRules_AreIgnored()
    {
        var enabledWithoutSld = new HashSet<int> { 2, 3, 4, 5, 6 };
        _sut.Classify("SLD 점검", Rules, enabledWithoutSld).Should().BeNull();
    }

    [Fact]
    public void ChungbukKeywordVariants_Match()
    {
        _sut.Classify("DL정보기술 협의", Rules, AllEnabled).Should().Be(4);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'IClientClassifier' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Classification/IClientClassifier.cs`:
```csharp
using Memoria.Core.Models;

namespace Memoria.Core.Classification;

public interface IClientClassifier
{
    /// 활성 고객사의 규칙만 대상으로, Priority 오름차순으로 평가하여
    /// 첫 키워드 포함(대소문자 무시) 매칭의 ClientId 반환. 없으면 null(미분류).
    int? Classify(string taskText, IEnumerable<ClientRule> rules, ISet<int> enabledClientIds);
}
```

`src/Memoria.Core/Classification/ClientClassifier.cs`:
```csharp
using Memoria.Core.Models;

namespace Memoria.Core.Classification;

public sealed class ClientClassifier : IClientClassifier
{
    public int? Classify(string taskText, IEnumerable<ClientRule> rules, ISet<int> enabledClientIds)
    {
        foreach (var rule in rules
                     .Where(r => enabledClientIds.Contains(r.ClientId))
                     .OrderBy(r => r.Priority))
        {
            if (taskText.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase))
                return rule.ClientId;
        }
        return null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~ClientClassifierTests"
```
예상: `Passed!  - Failed: 0, Passed: 6`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add ClientClassifier with priority/case-insensitive/enabled rules

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---
### Task 4: 주간보고 렌더 타입 + 양식 A 골든 렌더

**Files:**
- Create: `src/Memoria.Core/Reporting/ReportTypes.cs`, `src/Memoria.Core/Reporting/IWeeklyReportRenderer.cs`, `src/Memoria.Core/Reporting/WeeklyReportRenderer.cs`
- Test: `tests/Memoria.Tests/Reporting/WeeklyReportRendererFormatATests.cs`

**Interfaces:**
- Consumes: `Memoria.Core.Models.Client`, `ReportFormatKind`.
- Produces: `Memoria.Core.Reporting`의 `ReportTask`, `ReportIssue`, `WeeklyReportData`, `ReportRenderOptions`, `IWeeklyReportRenderer.Render(...)` (계약 §3). 본 태스크에서 양식 A를 완성하고 B는 Task 5에서 채운다.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Reporting/WeeklyReportRendererFormatATests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Xunit;

namespace Memoria.Tests.Reporting;

public class WeeklyReportRendererFormatATests
{
    private readonly IWeeklyReportRenderer _sut = new WeeklyReportRenderer();

    [Fact]
    public void FormatA_Golden_HasBlankLineBetweenTasksAndIssues()
    {
        var data = new WeeklyReportData(
            Tasks:
            [
                new ReportTask("task1", null, false),
                new ReportTask("task2", null, false),
            ],
            Issues:
            [
                new ReportIssue("issue1"),
                new ReportIssue("issue2"),
            ]);
        var options = new ReportRenderOptions();

        var text = _sut.Render(ReportFormatKind.A, data, options);

        const string expected =
            "[업무 내용]\n\t* task1\n\t* task2\n\n[이슈]\n\t* issue1\n\t* issue2";
        text.Should().Be(expected);
    }

    [Fact]
    public void FormatA_IncludeDoneOnly_FiltersTasksButKeepsAllIssues()
    {
        var data = new WeeklyReportData(
            Tasks:
            [
                new ReportTask("done task", null, true),
                new ReportTask("open task", null, false),
            ],
            Issues: [new ReportIssue("issue1")]);
        var options = new ReportRenderOptions { IncludeDoneOnly = true };

        var text = _sut.Render(ReportFormatKind.A, data, options);

        const string expected = "[업무 내용]\n\t* done task\n\n[이슈]\n\t* issue1";
        text.Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'WeeklyReportData' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Reporting/ReportTypes.cs` (계약 §3 그대로):
```csharp
using Memoria.Core.Models;

namespace Memoria.Core.Reporting;

public sealed record ReportTask(string Text, int? ClientId, bool Done);
public sealed record ReportIssue(string Text);

public sealed record WeeklyReportData(
    IReadOnlyList<ReportTask> Tasks,
    IReadOnlyList<ReportIssue> Issues);

public sealed record ReportRenderOptions
{
    public string ReporterName { get; init; } = "이승현";
    public DateOnly WeekStart { get; init; }
    public DateOnly WeekEnd { get; init; }
    public string TaskHeaderA { get; init; } = "[업무 내용]";
    public string IssueHeaderA { get; init; } = "[이슈]";
    public string TitleWordB { get; init; } = "주간 보고";
    public string IssueHeaderB { get; init; } = "* 이슈사항:";
    public string Indent { get; init; } = "\t";
    public bool IncludeDoneOnly { get; init; } = false;
    public IReadOnlyList<Client> Clients { get; init; } = new List<Client>();
    public string UnclassifiedLabel { get; init; } = "미분류";
}
```

`src/Memoria.Core/Reporting/IWeeklyReportRenderer.cs` (계약 §3):
```csharp
using Memoria.Core.Models;

namespace Memoria.Core.Reporting;

public interface IWeeklyReportRenderer
{
    /// 양식 A 또는 B의 최종 텍스트를 반환.
    string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options);
}
```

`src/Memoria.Core/Reporting/WeeklyReportRenderer.cs` (A 구현, B는 Task 5에서):
```csharp
using Memoria.Core.Models;

namespace Memoria.Core.Reporting;

public sealed class WeeklyReportRenderer : IWeeklyReportRenderer
{
    public string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options)
        => format switch
        {
            ReportFormatKind.A => RenderA(data, options),
            ReportFormatKind.B => RenderB(data, options),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    private static IEnumerable<ReportTask> VisibleTasks(WeeklyReportData data, ReportRenderOptions options)
        => options.IncludeDoneOnly ? data.Tasks.Where(t => t.Done) : data.Tasks;

    private static string RenderA(WeeklyReportData data, ReportRenderOptions options)
    {
        var lines = new List<string> { options.TaskHeaderA };
        foreach (var t in VisibleTasks(data, options))
            lines.Add(options.Indent + "* " + t.Text);
        lines.Add("");
        lines.Add(options.IssueHeaderA);
        foreach (var i in data.Issues)
            lines.Add(options.Indent + "* " + i.Text);
        return string.Join("\n", lines);
    }

    private static string RenderB(WeeklyReportData data, ReportRenderOptions options)
        => throw new NotImplementedException("양식 B는 Task 5에서 구현");
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeeklyReportRendererFormatATests"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add report types and Format A renderer (golden)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: 양식 B 골든 렌더

**Files:**
- Modify: `src/Memoria.Core/Reporting/WeeklyReportRenderer.cs` (`RenderB` 구현)
- Test: `tests/Memoria.Tests/Reporting/WeeklyReportRendererFormatBTests.cs`

**Interfaces:**
- Consumes: `IWeeklyReportRenderer.Render`, `ReportRenderOptions.Clients`(표시순 활성 고객사), `ReportTask.ClientId`.
- Produces: 양식 B 출력 문자열(제목줄·고객사 섹션·미분류 조건부·이슈 섹션) — 사용자 예시와 글자단위 일치.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Reporting/WeeklyReportRendererFormatBTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Xunit;

namespace Memoria.Tests.Reporting;

public class WeeklyReportRendererFormatBTests
{
    private readonly IWeeklyReportRenderer _sut = new WeeklyReportRenderer();

    private static IReadOnlyList<Client> DisplayClients() =>
    [
        new() { Id = 1, Name = "SLD", SortOrder = 1, Enabled = true },
        new() { Id = 2, Name = "MTP", SortOrder = 2, Enabled = true },
        new() { Id = 3, Name = "코모텍", SortOrder = 3, Enabled = true },
        new() { Id = 4, Name = "충북테크놀로지파크", SortOrder = 4, Enabled = true },
        new() { Id = 5, Name = "자율형 공장", SortOrder = 5, Enabled = true },
        new() { Id = 6, Name = "카본센스", SortOrder = 6, Enabled = true },
    ];

    [Fact]
    public void FormatB_Golden_WithUnclassifiedSection()
    {
        var data = new WeeklyReportData(
            Tasks:
            [
                new ReportTask("SLD 점검", 1, false),
                new ReportTask("코모텍 미팅", 3, false),
                new ReportTask("자율형공장 라인 셋업", 5, false),
                new ReportTask("기타 정리", null, false),
            ],
            Issues:
            [
                new ReportIssue("장비 오류"),
                new ReportIssue("일정 지연"),
            ]);

        var options = new ReportRenderOptions
        {
            ReporterName = "이승현",
            WeekStart = new DateOnly(2026, 6, 23),
            WeekEnd = new DateOnly(2026, 6, 27),
            Clients = DisplayClients(),
        };

        var text = _sut.Render(ReportFormatKind.B, data, options);

        const string expected =
            "[ 이승현 주간 보고 (06/23 ~ 06/27) ]:\n" +
            "\n" +
            "[ SLD ]\n" +
            "\t* SLD 점검\n" +
            "\n" +
            "[ MTP ]\n" +
            "\n" +
            "[ 코모텍 ]\n" +
            "\t* 코모텍 미팅\n" +
            "\n" +
            "[ 충북테크놀로지파크 ]\n" +
            "\n" +
            "[ 자율형 공장 ]\n" +
            "\t* 자율형공장 라인 셋업\n" +
            "\n" +
            "[ 카본센스 ]\n" +
            "\n" +
            "[ 미분류 ]\n" +
            "\t* 기타 정리\n" +
            "\n" +
            "* 이슈사항:\n" +
            "\t* 장비 오류\n" +
            "\t* 일정 지연";
        text.Should().Be(expected);
    }

    [Fact]
    public void FormatB_OmitsUnclassifiedSection_WhenNoneUnclassified()
    {
        var data = new WeeklyReportData(
            Tasks: [new ReportTask("SLD 점검", 1, false)],
            Issues: []);
        var options = new ReportRenderOptions
        {
            WeekStart = new DateOnly(2026, 6, 22),
            WeekEnd = new DateOnly(2026, 6, 26),
            Clients = DisplayClients(),
        };

        var text = _sut.Render(ReportFormatKind.B, data, options);

        text.Should().NotContain("[ 미분류 ]");
        text.Should().Contain("[ 이승현 주간 보고 (06/22 ~ 06/26) ]:");
        text.Should().EndWith("* 이슈사항:");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeeklyReportRendererFormatBTests"
```
예상 실패: `System.NotImplementedException : 양식 B는 Task 5에서 구현`.

- [ ] **Step 3: Write minimal implementation**

`WeeklyReportRenderer.cs`의 `RenderB`를 교체한다. 또한 파일 상단에 `using System.Globalization;`를 추가한다.

```csharp
    private static string RenderB(WeeklyReportData data, ReportRenderOptions options)
    {
        string start = options.WeekStart.ToString("MM/dd", CultureInfo.InvariantCulture);
        string end = options.WeekEnd.ToString("MM/dd", CultureInfo.InvariantCulture);

        var lines = new List<string>
        {
            $"[ {options.ReporterName} {options.TitleWordB} ({start} ~ {end}) ]:",
            "",
        };

        var tasks = VisibleTasks(data, options).ToList();

        foreach (var client in options.Clients)
        {
            lines.Add($"[ {client.Name} ]");
            foreach (var t in tasks.Where(t => t.ClientId == client.Id))
                lines.Add(options.Indent + "* " + t.Text);
            lines.Add("");
        }

        var unclassified = tasks.Where(t => t.ClientId is null).ToList();
        if (unclassified.Count > 0)
        {
            lines.Add($"[ {options.UnclassifiedLabel} ]");
            foreach (var t in unclassified)
                lines.Add(options.Indent + "* " + t.Text);
            lines.Add("");
        }

        lines.Add(options.IssueHeaderB);
        foreach (var i in data.Issues)
            lines.Add(options.Indent + "* " + i.Text);

        return string.Join("\n", lines);
    }
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeeklyReportRendererFormatBTests"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): implement Format B renderer (golden, conditional unclassified)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---
### Task 6: Dapper 매핑 + 연결 팩토리 + `IDatabaseInitializer` (스키마/PRAGMA/마이그레이션/시드)

**Files:**
- Create: `src/Memoria.Core/Data/DapperConfig.cs`, `src/Memoria.Core/Data/SqliteConnectionFactory.cs`, `src/Memoria.Core/Data/IDatabaseInitializer.cs`, `src/Memoria.Core/Data/DatabaseInitializer.cs`, `tests/Memoria.Tests/Data/TestDb.cs`
- Test: `tests/Memoria.Tests/Data/DatabaseInitializerTests.cs`

**Interfaces:**
- Consumes: 모든 모델, `SettingsKeys`(상수는 본 태스크에서 함께 생성).
- Produces: `Memoria.Core.Data.SqliteConnectionFactory`(인프라), `IDatabaseInitializer.EnsureReady()/CheckIntegrity()` (계약 §4), 스키마 v1 + 시드. 이후 모든 Repository 태스크가 `SqliteConnectionFactory`와 시드 데이터를 소비한다.

> **FTS5 구현 메모:** 계약 §4의 `ISearchService`는 DDL을 강제하지 않는다. 집계 컬럼 `items`(노트의 모든 checklist_items.text 결합)를 트리거로 안정 동기화하기 위해, 설계 §7.3의 "트리거 동기화" 요구를 **own-content FTS5 테이블**(`fts5(title, body, items)`)로 구현한다. contentless(`content=''`)는 집계 컬럼의 트리거 기반 DELETE/UPDATE가 원본값 재구성을 요구해 오류 위험이 크므로 채택하지 않는다. 검색 동작·대상(title+body+items)·트리거 동기화 요구는 모두 충족한다.

- [ ] **Step 1: Write the failing test**

`SettingsKeys` 상수도 본 태스크에서 필요하므로 먼저 `src/Memoria.Core/SettingsKeys.cs`를 시드/테스트에서 참조한다(구현은 Step 3에 포함).

`tests/Memoria.Tests/Data/DatabaseInitializerTests.cs`:
```csharp
using Dapper;
using FluentAssertions;
using Memoria.Core;
using Memoria.Core.Data;
using Xunit;

namespace Memoria.Tests.Data;

public class DatabaseInitializerTests
{
    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), "memoria_init_" + Guid.NewGuid().ToString("N") + ".db");

    [Fact]
    public void EnsureReady_CreatesSchema_SetsUserVersion_AndSeeds()
    {
        var path = NewDbPath();
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();

            File.Exists(path).Should().BeTrue();

            using var conn = factory.Open();
            conn.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(1);
            conn.ExecuteScalar<string>("PRAGMA journal_mode;").Should().Be("wal");

            conn.ExecuteScalar<long>("SELECT COUNT(*) FROM clients;").Should().Be(6);
            conn.ExecuteScalar<long>("SELECT COUNT(*) FROM groups WHERE is_system = 1;").Should().Be(2);
            conn.ExecuteScalar<string>(
                "SELECT value FROM settings WHERE key = @k;",
                new { k = SettingsKeys.ReporterName }).Should().Be("이승현");
            conn.ExecuteScalar<string>(
                "SELECT value FROM settings WHERE key = @k;",
                new { k = SettingsKeys.ReportIndent }).Should().Be("\t");
            // 분류 규칙: 자율형공장 키워드가 SLD보다 낮은(우선) priority
            var autoPriority = conn.ExecuteScalar<long>(
                "SELECT priority FROM client_rules WHERE keyword = '자율형공장';");
            var sldPriority = conn.ExecuteScalar<long>(
                "SELECT priority FROM client_rules WHERE keyword = 'SLD';");
            autoPriority.Should().BeLessThan(sldPriority);
        }
        finally
        {
            factory.Dispose();  // 영속 쓰기 연결 닫기(파일 잠금 해제)
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var p in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(p)) File.Delete(p);
        }
    }

    [Fact]
    public void EnsureReady_IsIdempotent_DoesNotDuplicateSeed()
    {
        var path = NewDbPath();
        var factory = new SqliteConnectionFactory(path);
        try
        {
            var init = new DatabaseInitializer(factory);
            init.EnsureReady();
            init.EnsureReady(); // 두 번째 호출은 마이그레이션/시드를 다시 적용하지 않음

            using var conn = factory.Open();
            conn.ExecuteScalar<long>("SELECT COUNT(*) FROM clients;").Should().Be(6);
        }
        finally
        {
            factory.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var p in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(p)) File.Delete(p);
        }
    }

    [Fact]
    public void CheckIntegrity_ReturnsTrue_ForFreshDb()
    {
        var path = NewDbPath();
        var factory = new SqliteConnectionFactory(path);
        try
        {
            var init = new DatabaseInitializer(factory);
            init.EnsureReady();
            init.CheckIntegrity().Should().BeTrue();
        }
        finally
        {
            factory.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var p in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(p)) File.Delete(p);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'SqliteConnectionFactory' could not be found` / `'SettingsKeys' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/SettingsKeys.cs` (계약 §6 그대로):
```csharp
namespace Memoria.Core;

public static class SettingsKeys
{
    public const string ThemeMode = "theme.mode";
    public const string ThemePreset = "theme.preset";
    public const string ThemeAccent = "theme.accent";
    public const string ReporterName = "report.reporterName";
    public const string FormatATaskHeader = "report.formatA.taskHeader";
    public const string FormatAIssueHeader = "report.formatA.issueHeader";
    public const string FormatBTitleWord = "report.formatB.titleWord";
    public const string FormatBIssueHeader = "report.formatB.issueHeader";
    public const string ReportIndent = "report.indent";
    public const string IncludeDoneOnly = "report.includeDoneOnly";
    public const string HotkeyNewNote = "hotkey.newNote";
    public const string Autostart = "app.autostart";
    public const string CloseToTray = "app.closeToTray";
    public const string BackupRetentionCount = "backup.retentionCount";
    public const string TrashRetentionDays = "trash.retentionDays";
    public const string AutosaveDebounceMs = "autosave.debounceMs";
}
```

`src/Memoria.Core/Data/DapperConfig.cs` (enum→문자열, DateOnly/DateTimeOffset→ISO-8601 TypeHandler):
```csharp
using System.Data;
using System.Globalization;
using Dapper;
using Memoria.Core.Models;

namespace Memoria.Core.Data;

internal static class DapperConfig
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new NoteTypeHandler());
        SqlMapper.AddTypeHandler(new ItemKindHandler());
        SqlMapper.AddTypeHandler(new ReportFormatKindHandler());
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) =>
            DateOnly.ParseExact((string)value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) =>
            DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("O", CultureInfo.InvariantCulture);
        }
    }

    private sealed class NoteTypeHandler : SqlMapper.TypeHandler<NoteType>
    {
        public override NoteType Parse(object value) => (string)value switch
        {
            "plain" => NoteType.Plain,
            "checklist" => NoteType.Checklist,
            "weekly_report" => NoteType.WeeklyReport,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown note type"),
        };

        public override void SetValue(IDbDataParameter parameter, NoteType value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value switch
            {
                NoteType.Plain => "plain",
                NoteType.Checklist => "checklist",
                NoteType.WeeklyReport => "weekly_report",
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            };
        }
    }

    private sealed class ItemKindHandler : SqlMapper.TypeHandler<ItemKind>
    {
        public override ItemKind Parse(object value) => (string)value switch
        {
            "task" => ItemKind.Task,
            "issue" => ItemKind.Issue,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown item kind"),
        };

        public override void SetValue(IDbDataParameter parameter, ItemKind value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value == ItemKind.Task ? "task" : "issue";
        }
    }

    private sealed class ReportFormatKindHandler : SqlMapper.TypeHandler<ReportFormatKind>
    {
        public override ReportFormatKind Parse(object value) => (string)value switch
        {
            "A" => ReportFormatKind.A,
            "B" => ReportFormatKind.B,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown report format"),
        };

        public override void SetValue(IDbDataParameter parameter, ReportFormatKind value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value == ReportFormatKind.A ? "A" : "B";
        }
    }
}
```

`src/Memoria.Core/Data/SqliteConnectionFactory.cs` (계약 §8 / 스펙 §7.7 — 단일 영속 쓰기 연결 + `WriteSync` 락):
```csharp
using Dapper;
using Microsoft.Data.Sqlite;

namespace Memoria.Core.Data;

public sealed class SqliteConnectionFactory : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection _writeConnection;

    /// 모든 쓰기를 직렬화하는 락(스펙 §7.7 단일 직렬 라이터, 계약 §8).
    public object WriteSync { get; } = new();

    /// 원본 DB 파일 경로(백업/복원에서 사용).
    public string DatabasePath { get; }

    public SqliteConnectionFactory(string dbPath)
    {
        DapperConfig.EnsureRegistered();
        DatabasePath = dbPath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        _writeConnection = OpenConfigured(setWal: true);
    }

    /// 단일 영속 쓰기 연결. 반드시 `lock (WriteSync)` 안에서만 사용한다(직렬 라이터).
    public SqliteConnection Write => _writeConnection;

    /// 읽기 전용 연결(WAL 동시 읽기). 호출자가 `using`으로 해제한다.
    public SqliteConnection Open() => OpenConfigured(setWal: false);

    private SqliteConnection OpenConfigured(bool setWal)
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        conn.Execute(
            (setWal ? "PRAGMA journal_mode = WAL; " : "") +
            "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
        return conn;
    }

    /// 복원(IBackupService.TryRestoreFromLatestBackup) 전용: 쓰기 연결을 닫아 파일 잠금을 해제한다.
    /// 호출자가 `lock (WriteSync)`를 보유한 상태에서만 사용한다.
    internal void CloseForRestore() => _writeConnection.Dispose();

    /// 복원 후 쓰기 연결을 다시 연다(WAL 재설정). 호출자가 `lock (WriteSync)`를 보유한 상태에서만 사용한다.
    internal void ReopenAfterRestore() => _writeConnection = OpenConfigured(setWal: true);

    public void Dispose()
    {
        lock (WriteSync)
        {
            try { _writeConnection.Execute("PRAGMA wal_checkpoint(TRUNCATE);"); }
            catch (SqliteException) { /* best-effort checkpoint */ }
            _writeConnection.Dispose();
        }
    }
}
```

`src/Memoria.Core/Data/IDatabaseInitializer.cs` (계약 §4):
```csharp
namespace Memoria.Core.Data;

public interface IDatabaseInitializer
{
    /// 파일 없으면 생성, PRAGMA(WAL/foreign_keys/busy_timeout) 설정,
    /// 마이그레이션 적용(user_version), 첫 실행 시드(clients/client_rules/시스템 그룹/settings 기본값).
    void EnsureReady();
    /// PRAGMA integrity_check 결과(true=정상).
    bool CheckIntegrity();
}
```

`src/Memoria.Core/Data/DatabaseInitializer.cs`:
```csharp
using Dapper;

namespace Memoria.Core.Data;

public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private const long TargetVersion = 1;
    private readonly SqliteConnectionFactory _factory;

    public DatabaseInitializer(SqliteConnectionFactory factory) => _factory = factory;

    public void EnsureReady()
    {
        // 마이그레이션/시드는 쓰기이므로 단일 직렬 라이터 락 + 영속 쓰기 연결로 수행(계약 §8).
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            conn.Execute(
                "CREATE TABLE IF NOT EXISTS _migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);");

            var current = conn.ExecuteScalar<long>("PRAGMA user_version;");
            if (current >= TargetVersion) return;

            using var tx = conn.BeginTransaction();
            conn.Execute(SchemaV1, transaction: tx);
            SeedV1(conn, tx);
            conn.Execute(
                "INSERT INTO _migrations(version, applied_at) VALUES(1, strftime('%Y-%m-%dT%H:%M:%fZ','now'));",
                transaction: tx);
            conn.Execute("PRAGMA user_version = 1;", transaction: tx);
            tx.Commit();
        }
    }

    public bool CheckIntegrity()
    {
        using var conn = _factory.Open();
        var result = conn.ExecuteScalar<string>("PRAGMA integrity_check;");
        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
    }

    private static void SeedV1(Microsoft.Data.Sqlite.SqliteConnection conn, System.Data.IDbTransaction tx)
    {
        conn.Execute(SeedClientsSql, transaction: tx);
        conn.Execute(SeedRulesSql, transaction: tx);
        conn.Execute(SeedGroupsSql, transaction: tx);
        conn.Execute(SeedSettingsSql, transaction: tx);
    }

    private const string SchemaV1 = @"
CREATE TABLE groups (
  id          INTEGER PRIMARY KEY,
  name        TEXT NOT NULL,
  parent_id   INTEGER REFERENCES groups(id),
  is_system   INTEGER NOT NULL DEFAULT 0,
  sort_order  INTEGER NOT NULL DEFAULT 0,
  color       TEXT,
  created_at  TEXT NOT NULL
);

CREATE TABLE clients (
  id          INTEGER PRIMARY KEY,
  name        TEXT NOT NULL,
  sort_order  INTEGER NOT NULL,
  enabled     INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE notes (
  id                 INTEGER PRIMARY KEY,
  group_id           INTEGER REFERENCES groups(id) ON DELETE SET NULL,
  type               TEXT NOT NULL,
  title              TEXT,
  body               TEXT,
  log_date           TEXT,
  report_format      TEXT,
  report_week_start  TEXT,
  pinned             INTEGER NOT NULL DEFAULT 0,
  sort_order         INTEGER NOT NULL DEFAULT 0,
  deleted_at         TEXT,
  created_at         TEXT NOT NULL,
  updated_at         TEXT NOT NULL
);

CREATE TABLE checklist_items (
  id          INTEGER PRIMARY KEY,
  note_id     INTEGER NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
  kind        TEXT NOT NULL,
  text        TEXT NOT NULL,
  done        INTEGER NOT NULL DEFAULT 0,
  done_at     TEXT,
  client_id   INTEGER REFERENCES clients(id) ON DELETE SET NULL,
  is_manual   INTEGER NOT NULL DEFAULT 0,
  sort_order  INTEGER NOT NULL DEFAULT 0,
  created_at  TEXT NOT NULL,
  updated_at  TEXT NOT NULL
);

CREATE TABLE client_rules (
  id         INTEGER PRIMARY KEY,
  client_id  INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
  keyword    TEXT NOT NULL,
  priority   INTEGER NOT NULL
);

CREATE TABLE settings (
  key    TEXT PRIMARY KEY,
  value  TEXT NOT NULL
);

CREATE INDEX idx_notes_group_id   ON notes(group_id);
CREATE INDEX idx_notes_log_date   ON notes(log_date);
CREATE INDEX idx_notes_deleted_at ON notes(deleted_at);
CREATE INDEX idx_notes_week       ON notes(report_week_start, report_format);
CREATE INDEX idx_items_note_id    ON checklist_items(note_id);
CREATE INDEX idx_items_client_id  ON checklist_items(client_id);

CREATE VIRTUAL TABLE notes_fts USING fts5(title, body, items);

CREATE TRIGGER notes_ai AFTER INSERT ON notes BEGIN
  INSERT INTO notes_fts(rowid, title, body, items)
  VALUES (new.id, COALESCE(new.title, ''), COALESCE(new.body, ''), '');
END;

CREATE TRIGGER notes_au AFTER UPDATE ON notes BEGIN
  UPDATE notes_fts
     SET title = COALESCE(new.title, ''), body = COALESCE(new.body, '')
   WHERE rowid = new.id;
END;

CREATE TRIGGER notes_ad AFTER DELETE ON notes BEGIN
  DELETE FROM notes_fts WHERE rowid = old.id;
END;

CREATE TRIGGER items_ai AFTER INSERT ON checklist_items BEGIN
  UPDATE notes_fts
     SET items = (SELECT COALESCE(GROUP_CONCAT(text, ' '), '')
                    FROM checklist_items WHERE note_id = new.note_id)
   WHERE rowid = new.note_id;
END;

CREATE TRIGGER items_au AFTER UPDATE ON checklist_items BEGIN
  UPDATE notes_fts
     SET items = (SELECT COALESCE(GROUP_CONCAT(text, ' '), '')
                    FROM checklist_items WHERE note_id = new.note_id)
   WHERE rowid = new.note_id;
END;

CREATE TRIGGER items_ad AFTER DELETE ON checklist_items BEGIN
  UPDATE notes_fts
     SET items = (SELECT COALESCE(GROUP_CONCAT(text, ' '), '')
                    FROM checklist_items WHERE note_id = old.note_id)
   WHERE rowid = old.note_id;
END;
";

    private const string SeedClientsSql = @"
INSERT INTO clients(name, sort_order, enabled) VALUES
  ('SLD', 1, 1),
  ('MTP', 2, 1),
  ('코모텍', 3, 1),
  ('충북테크놀로지파크', 4, 1),
  ('자율형 공장', 5, 1),
  ('카본센스', 6, 1);
";

    private const string SeedRulesSql = @"
INSERT INTO client_rules(client_id, keyword, priority)
SELECT id, '자율형공장', 1 FROM clients WHERE name = '자율형 공장'
UNION ALL SELECT id, '자율형 공장', 1 FROM clients WHERE name = '자율형 공장'
UNION ALL SELECT id, '충북', 2 FROM clients WHERE name = '충북테크놀로지파크'
UNION ALL SELECT id, '충북테크놀로지파크', 2 FROM clients WHERE name = '충북테크놀로지파크'
UNION ALL SELECT id, 'DL정보기술', 2 FROM clients WHERE name = '충북테크놀로지파크'
UNION ALL SELECT id, '코모텍', 3 FROM clients WHERE name = '코모텍'
UNION ALL SELECT id, 'MTP', 4 FROM clients WHERE name = 'MTP'
UNION ALL SELECT id, '머티리얼즈파크', 4 FROM clients WHERE name = 'MTP'
UNION ALL SELECT id, '카본센스', 5 FROM clients WHERE name = '카본센스'
UNION ALL SELECT id, 'SLD', 6 FROM clients WHERE name = 'SLD';
";

    private const string SeedGroupsSql = @"
INSERT INTO groups(name, is_system, sort_order, created_at) VALUES
  ('일일업무일지', 1, 0, strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  ('주간보고',    1, 1, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
";

    private const string SeedSettingsSql = @"
INSERT INTO settings(key, value) VALUES
  ('theme.mode', 'system'),
  ('theme.preset', 'default'),
  ('theme.accent', '#0078D4'),
  ('report.reporterName', '이승현'),
  ('report.formatA.taskHeader', '[업무 내용]'),
  ('report.formatA.issueHeader', '[이슈]'),
  ('report.formatB.titleWord', '주간 보고'),
  ('report.formatB.issueHeader', '* 이슈사항:'),
  ('report.indent', char(9)),
  ('report.includeDoneOnly', 'false'),
  ('hotkey.newNote', 'Ctrl+Alt+N'),
  ('app.autostart', 'true'),
  ('app.closeToTray', 'true'),
  ('backup.retentionCount', '7'),
  ('trash.retentionDays', '30'),
  ('autosave.debounceMs', '500');
";
}
```

`tests/Memoria.Tests/Data/TestDb.cs` (이후 Repository 태스크 공용 픽스처):
```csharp
using Memoria.Core.Data;
using Microsoft.Data.Sqlite;

namespace Memoria.Tests.Data;

internal sealed class TestDb : IDisposable
{
    public string Path { get; }
    public SqliteConnectionFactory Factory { get; }

    public TestDb()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "memoria_test_" + Guid.NewGuid().ToString("N") + ".db");
        Factory = new SqliteConnectionFactory(Path);
        new DatabaseInitializer(Factory).EnsureReady();
    }

    public void Dispose()
    {
        Factory.Dispose();   // 영속 쓰기 연결 닫기(파일 잠금 해제) — 계약 §8
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { Path, Path + "-wal", Path + "-shm" })
            if (File.Exists(p)) { try { File.Delete(p); } catch { /* best-effort cleanup */ } }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~DatabaseInitializerTests"
```
예상: `Passed!  - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add SQLite schema v1, migration runner, seed, FTS5 triggers

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---
### Task 7: `ISettingsRepository`

**Files:**
- Create: `src/Memoria.Core/Data/ISettingsRepository.cs`, `src/Memoria.Core/Data/SettingsRepository.cs`
- Test: `tests/Memoria.Tests/Data/SettingsRepositoryTests.cs`

**Interfaces:**
- Consumes: `SqliteConnectionFactory`, 시드된 settings.
- Produces: `ISettingsRepository.Get/GetOrDefault/Set/GetAll` (계약 §4).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Data/SettingsRepositoryTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core;
using Memoria.Core.Data;
using Xunit;

namespace Memoria.Tests.Data;

public class SettingsRepositoryTests
{
    [Fact]
    public void Get_ReturnsSeededValue_AndNullForMissing()
    {
        using var db = new TestDb();
        var sut = new SettingsRepository(db.Factory);

        sut.Get(SettingsKeys.ReporterName).Should().Be("이승현");
        sut.Get("does.not.exist").Should().BeNull();
    }

    [Fact]
    public void GetOrDefault_FallsBack_WhenMissing()
    {
        using var db = new TestDb();
        var sut = new SettingsRepository(db.Factory);

        sut.GetOrDefault("does.not.exist", "fallback").Should().Be("fallback");
        sut.GetOrDefault(SettingsKeys.ThemeMode, "x").Should().Be("system");
    }

    [Fact]
    public void Set_InsertsAndUpdates()
    {
        using var db = new TestDb();
        var sut = new SettingsRepository(db.Factory);

        sut.Set("custom.key", "v1");
        sut.Get("custom.key").Should().Be("v1");

        sut.Set("custom.key", "v2");
        sut.Get("custom.key").Should().Be("v2");
    }

    [Fact]
    public void GetAll_ContainsSeededKeys()
    {
        using var db = new TestDb();
        var sut = new SettingsRepository(db.Factory);

        var all = sut.GetAll();
        all.Should().ContainKey(SettingsKeys.Autostart);
        all[SettingsKeys.Autostart].Should().Be("true");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'SettingsRepository' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Data/ISettingsRepository.cs` (계약 §4):
```csharp
namespace Memoria.Core.Data;

public interface ISettingsRepository
{
    string? Get(string key);
    string GetOrDefault(string key, string fallback);
    void Set(string key, string value);
    IReadOnlyDictionary<string, string> GetAll();
}
```

`src/Memoria.Core/Data/SettingsRepository.cs`:
```csharp
using Dapper;

namespace Memoria.Core.Data;

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SettingsRepository(SqliteConnectionFactory factory) => _factory = factory;

    public string? Get(string key)
    {
        using var conn = _factory.Open();
        return conn.ExecuteScalar<string?>(
            "SELECT value FROM settings WHERE key = @key;", new { key });
    }

    public string GetOrDefault(string key, string fallback) => Get(key) ?? fallback;

    public void Set(string key, string value)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute(
                "INSERT INTO settings(key, value) VALUES(@key, @value) " +
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                new { key, value });
        }
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        using var conn = _factory.Open();
        var rows = conn.Query<(string Key, string Value)>("SELECT key AS Key, value AS Value FROM settings;");
        return rows.ToDictionary(r => r.Key, r => r.Value);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~SettingsRepositoryTests"
```
예상: `Passed!  - Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add SettingsRepository (upsert + GetAll)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: `IGroupRepository`

**Files:**
- Create: `src/Memoria.Core/Data/IGroupRepository.cs`, `src/Memoria.Core/Data/GroupRepository.cs`
- Test: `tests/Memoria.Tests/Data/GroupRepositoryTests.cs`

**Interfaces:**
- Consumes: `SqliteConnectionFactory`, `Group`, 시드된 시스템 그룹.
- Produces: `IGroupRepository.Create/Update/Delete/Get/GetAll` (계약 §4). Delete는 `notes.group_id ON DELETE SET NULL`.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Data/GroupRepositoryTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Data;

public class GroupRepositoryTests
{
    [Fact]
    public void Create_Get_RoundTrips()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);

        var id = sut.Create(new Group { Name = "업무", SortOrder = 10, Color = "#FF0000" });
        id.Should().BeGreaterThan(0);

        var loaded = sut.Get(id)!;
        loaded.Name.Should().Be("업무");
        loaded.SortOrder.Should().Be(10);
        loaded.Color.Should().Be("#FF0000");
        loaded.IsSystem.Should().BeFalse();
        loaded.CreatedAt.Should().BeAfter(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void GetAll_IncludesSystemGroups_OrderedBySortOrder()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);
        sut.Create(new Group { Name = "개인", SortOrder = 99 });

        var all = sut.GetAll();
        all.Should().Contain(g => g.Name == "일일업무일지" && g.IsSystem);
        all.Should().Contain(g => g.Name == "주간보고" && g.IsSystem);
        all.Select(g => g.SortOrder).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Update_PersistsChanges()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);
        var id = sut.Create(new Group { Name = "old" });

        var g = sut.Get(id)!;
        g.Name = "new";
        g.SortOrder = 5;
        sut.Update(g);

        sut.Get(id)!.Name.Should().Be("new");
        sut.Get(id)!.SortOrder.Should().Be(5);
    }

    [Fact]
    public void Delete_SetsNoteGroupIdToNull()
    {
        using var db = new TestDb();
        var groups = new GroupRepository(db.Factory);
        var notes = new NoteRepository(db.Factory);
        var gid = groups.Create(new Group { Name = "temp" });
        var nid = notes.Create(new Note { Type = NoteType.Plain, GroupId = gid, Title = "n" });

        groups.Delete(gid);

        groups.Get(gid).Should().BeNull();
        notes.Get(nid)!.GroupId.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'GroupRepository' could not be found` (그리고 Task 10 전이면 `NoteRepository`도 미존재). **순서 메모:** 본 태스크의 4번째 테스트는 `NoteRepository`(Task 10)를 참조하므로, Task 10 완료 후 활성화하거나 Task 10과 함께 통과시킨다. 1~3번 테스트만으로 Group 구현을 먼저 검증하려면 4번 테스트에 `[Fact(Skip="needs Task 10 NoteRepository")]`를 붙였다가 Task 10에서 해제한다.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Data/IGroupRepository.cs` (계약 §4):
```csharp
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public interface IGroupRepository
{
    int Create(Group group);
    void Update(Group group);
    void Delete(int id);                  // notes.group_id ON DELETE SET NULL
    Group? Get(int id);
    IReadOnlyList<Group> GetAll();        // 시스템 그룹 포함, SortOrder 정렬
}
```

`src/Memoria.Core/Data/GroupRepository.cs`:
```csharp
using Dapper;
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public sealed class GroupRepository : IGroupRepository
{
    private const string SelectColumns =
        "id AS Id, name AS Name, parent_id AS ParentId, is_system AS IsSystem, " +
        "sort_order AS SortOrder, color AS Color, created_at AS CreatedAt";

    private readonly SqliteConnectionFactory _factory;

    public GroupRepository(SqliteConnectionFactory factory) => _factory = factory;

    public int Create(Group group)
    {
        group.CreatedAt = DateTimeOffset.UtcNow;
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            conn.Execute(
                "INSERT INTO groups(name, parent_id, is_system, sort_order, color, created_at) " +
                "VALUES(@Name, @ParentId, @IsSystem, @SortOrder, @Color, @CreatedAt);", group);
            group.Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid();");
        }
        return group.Id;
    }

    public void Update(Group group)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute(
                "UPDATE groups SET name = @Name, parent_id = @ParentId, is_system = @IsSystem, " +
                "sort_order = @SortOrder, color = @Color WHERE id = @Id;", group);
        }
    }

    public void Delete(int id)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute("DELETE FROM groups WHERE id = @id;", new { id });
        }
    }

    public Group? Get(int id)
    {
        using var conn = _factory.Open();
        return conn.QuerySingleOrDefault<Group>(
            $"SELECT {SelectColumns} FROM groups WHERE id = @id;", new { id });
    }

    public IReadOnlyList<Group> GetAll()
    {
        using var conn = _factory.Open();
        return conn.Query<Group>(
            $"SELECT {SelectColumns} FROM groups ORDER BY sort_order, id;").ToList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupRepositoryTests"
```
예상: `Passed!  - Failed: 0, Passed: 3`(4번째는 Task 10 후 통과; 또는 Skip 해제 후 4 Passed).

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add GroupRepository (CRUD, SET NULL on delete)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---
### Task 9: `IClientRepository`

**Files:**
- Create: `src/Memoria.Core/Data/IClientRepository.cs`, `src/Memoria.Core/Data/ClientRepository.cs`
- Test: `tests/Memoria.Tests/Data/ClientRepositoryTests.cs`

**Interfaces:**
- Consumes: `SqliteConnectionFactory`, `Client`, `ClientRule`, 시드된 고객사/규칙.
- Produces: `IClientRepository.Create/Update/Delete/GetAll(enabledOnly)/GetRules/ReplaceRules` (계약 §4).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Data/ClientRepositoryTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Data;

public class ClientRepositoryTests
{
    [Fact]
    public void GetAll_ReturnsSeededClients_InSortOrder()
    {
        using var db = new TestDb();
        var sut = new ClientRepository(db.Factory);

        var all = sut.GetAll();
        all.Should().HaveCount(6);
        all.Select(c => c.Name).First().Should().Be("SLD");
        all.Select(c => c.SortOrder).Should().BeInAscendingOrder();
    }

    [Fact]
    public void GetAll_EnabledOnly_ExcludesDisabled()
    {
        using var db = new TestDb();
        var sut = new ClientRepository(db.Factory);
        var sld = sut.GetAll().Single(c => c.Name == "SLD");
        sld.Enabled = false;
        sut.Update(sld);

        sut.GetAll(enabledOnly: true).Should().NotContain(c => c.Name == "SLD");
        sut.GetAll(enabledOnly: false).Should().Contain(c => c.Name == "SLD");
    }

    [Fact]
    public void GetRules_ReturnsSeededRules()
    {
        using var db = new TestDb();
        var sut = new ClientRepository(db.Factory);

        var rules = sut.GetRules();
        rules.Should().Contain(r => r.Keyword == "자율형공장" && r.Priority == 1);
        rules.Should().Contain(r => r.Keyword == "SLD" && r.Priority == 6);
    }

    [Fact]
    public void Create_AddsClient()
    {
        using var db = new TestDb();
        var sut = new ClientRepository(db.Factory);

        var id = sut.Create(new Client { Name = "신규고객", SortOrder = 7, Enabled = true });
        sut.GetAll().Should().Contain(c => c.Id == id && c.Name == "신규고객");
    }

    [Fact]
    public void ReplaceRules_ReplacesOnlyThatClientRules()
    {
        using var db = new TestDb();
        var sut = new ClientRepository(db.Factory);
        var sld = sut.GetAll().Single(c => c.Name == "SLD");

        sut.ReplaceRules(sld.Id,
        [
            new ClientRule { ClientId = sld.Id, Keyword = "에스엘디", Priority = 6 },
        ]);

        var rules = sut.GetRules();
        rules.Where(r => r.ClientId == sld.Id).Should().ContainSingle()
             .Which.Keyword.Should().Be("에스엘디");
        rules.Should().Contain(r => r.Keyword == "자율형공장"); // 다른 고객사 규칙은 유지
    }

    [Fact]
    public void Delete_RemovesClientAndCascadesRules()
    {
        using var db = new TestDb();
        var sut = new ClientRepository(db.Factory);
        var carbon = sut.GetAll().Single(c => c.Name == "카본센스");

        sut.Delete(carbon.Id);

        sut.GetAll().Should().NotContain(c => c.Id == carbon.Id);
        sut.GetRules().Should().NotContain(r => r.ClientId == carbon.Id);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'ClientRepository' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Data/IClientRepository.cs` (계약 §4):
```csharp
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public interface IClientRepository
{
    int Create(Client client);
    void Update(Client client);
    void Delete(int id);                                       // checklist_items.client_id ON DELETE SET NULL
    IReadOnlyList<Client> GetAll(bool enabledOnly = false);    // SortOrder 정렬
    IReadOnlyList<ClientRule> GetRules();                      // 전체 규칙
    void ReplaceRules(int clientId, IEnumerable<ClientRule> rules);
}
```

`src/Memoria.Core/Data/ClientRepository.cs`:
```csharp
using Dapper;
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public sealed class ClientRepository : IClientRepository
{
    private readonly SqliteConnectionFactory _factory;

    public ClientRepository(SqliteConnectionFactory factory) => _factory = factory;

    public int Create(Client client)
    {
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            conn.Execute(
                "INSERT INTO clients(name, sort_order, enabled) VALUES(@Name, @SortOrder, @Enabled);", client);
            client.Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid();");
        }
        return client.Id;
    }

    public void Update(Client client)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute(
                "UPDATE clients SET name = @Name, sort_order = @SortOrder, enabled = @Enabled WHERE id = @Id;",
                client);
        }
    }

    public void Delete(int id)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute("DELETE FROM clients WHERE id = @id;", new { id });
        }
    }

    public IReadOnlyList<Client> GetAll(bool enabledOnly = false)
    {
        using var conn = _factory.Open();
        var where = enabledOnly ? "WHERE enabled = 1 " : "";
        return conn.Query<Client>(
            "SELECT id AS Id, name AS Name, sort_order AS SortOrder, enabled AS Enabled " +
            $"FROM clients {where}ORDER BY sort_order, id;").ToList();
    }

    public IReadOnlyList<ClientRule> GetRules()
    {
        using var conn = _factory.Open();
        return conn.Query<ClientRule>(
            "SELECT id AS Id, client_id AS ClientId, keyword AS Keyword, priority AS Priority " +
            "FROM client_rules ORDER BY priority, id;").ToList();
    }

    public void ReplaceRules(int clientId, IEnumerable<ClientRule> rules)
    {
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            using var tx = conn.BeginTransaction();
            conn.Execute("DELETE FROM client_rules WHERE client_id = @clientId;",
                new { clientId }, tx);
            foreach (var rule in rules)
            {
                conn.Execute(
                    "INSERT INTO client_rules(client_id, keyword, priority) VALUES(@ClientId, @Keyword, @Priority);",
                    new { ClientId = clientId, rule.Keyword, rule.Priority }, tx);
            }
            tx.Commit();
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~ClientRepositoryTests"
```
예상: `Passed!  - Failed: 0, Passed: 6`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add ClientRepository (clients + rules, cascade delete)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: `INoteRepository`

**Files:**
- Create: `src/Memoria.Core/Data/INoteRepository.cs`, `src/Memoria.Core/Data/NoteRepository.cs`
- Test: `tests/Memoria.Tests/Data/NoteRepositoryTests.cs`

**Interfaces:**
- Consumes: `SqliteConnectionFactory`, `Note`, `NoteType`, `ReportFormatKind`.
- Produces: `INoteRepository.Create/Update/SoftDelete/Restore/Purge/PurgeExpiredTrash/Get/GetByGroup/GetTrash/GetChecklistsInWeek/FindWeeklyReport` (계약 §4). Task 8의 4번째 테스트(`Delete_SetsNoteGroupIdToNull`)와 Task 14가 이를 소비한다.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Data/NoteRepositoryTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Data;

public class NoteRepositoryTests
{
    [Fact]
    public void Create_FillsTimestamps_AndRoundTripsEnumsAndDates()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);

        var note = new Note
        {
            Type = NoteType.WeeklyReport,
            Title = "주간",
            Body = "내용",
            ReportFormat = ReportFormatKind.B,
            ReportWeekStart = new DateOnly(2026, 6, 22),
        };
        var id = sut.Create(note);

        var loaded = sut.Get(id)!;
        loaded.Type.Should().Be(NoteType.WeeklyReport);
        loaded.ReportFormat.Should().Be(ReportFormatKind.B);
        loaded.ReportWeekStart.Should().Be(new DateOnly(2026, 6, 22));
        loaded.CreatedAt.Should().BeAfter(DateTimeOffset.UnixEpoch);
        loaded.UpdatedAt.Should().BeAfter(DateTimeOffset.UnixEpoch);
        loaded.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void GetByGroup_FiltersByGroupAndExcludesDeleted_NullMeansUnclassified()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var inGroup = sut.Create(new Note { Type = NoteType.Plain, GroupId = 1, Title = "g" });
        var unclassified = sut.Create(new Note { Type = NoteType.Plain, GroupId = null, Title = "u" });

        sut.GetByGroup(1).Should().Contain(n => n.Id == inGroup);
        sut.GetByGroup(null).Should().Contain(n => n.Id == unclassified)
            .And.NotContain(n => n.Id == inGroup);
    }

    [Fact]
    public void SoftDelete_Restore_Purge_Work()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note { Type = NoteType.Plain, GroupId = null, Title = "t" });

        sut.SoftDelete(id);
        sut.GetByGroup(null).Should().NotContain(n => n.Id == id);
        sut.GetTrash().Should().Contain(n => n.Id == id);

        sut.Restore(id);
        sut.GetTrash().Should().NotContain(n => n.Id == id);
        sut.GetByGroup(null).Should().Contain(n => n.Id == id);

        sut.SoftDelete(id);
        sut.Purge(id);
        sut.Get(id).Should().BeNull();
    }

    [Fact]
    public void PurgeExpiredTrash_RemovesOnlyOldDeleted()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var oldId = sut.Create(new Note { Type = NoteType.Plain, Title = "old" });
        var recentId = sut.Create(new Note { Type = NoteType.Plain, Title = "recent" });

        var old = sut.Get(oldId)!;
        old.DeletedAt = DateTimeOffset.UtcNow.AddDays(-40);
        sut.Update(old);
        sut.SoftDelete(recentId);

        sut.PurgeExpiredTrash(retentionDays: 30);

        sut.Get(oldId).Should().BeNull();
        sut.Get(recentId).Should().NotBeNull();
    }

    [Fact]
    public void GetChecklistsInWeek_ReturnsOnlyChecklistsInRange()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 23), Title = "in" });
        sut.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 29), Title = "out" });
        sut.Create(new Note { Type = NoteType.Plain, Title = "plain" });

        var results = sut.GetChecklistsInWeek(new DateOnly(2026, 6, 22), new DateOnly(2026, 6, 26));
        results.Should().ContainSingle().Which.Title.Should().Be("in");
    }

    [Fact]
    public void FindWeeklyReport_MatchesWeekStartAndFormat()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note
        {
            Type = NoteType.WeeklyReport,
            ReportFormat = ReportFormatKind.A,
            ReportWeekStart = new DateOnly(2026, 6, 22),
        });

        sut.FindWeeklyReport(new DateOnly(2026, 6, 22), ReportFormatKind.A)!.Id.Should().Be(id);
        sut.FindWeeklyReport(new DateOnly(2026, 6, 22), ReportFormatKind.B).Should().BeNull();
    }
}
```

또한 Task 8의 `GroupRepositoryTests.Delete_SetsNoteGroupIdToNull`에 Skip을 달았다면 본 태스크에서 해제한다.

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'NoteRepository' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Data/INoteRepository.cs` (계약 §4):
```csharp
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public interface INoteRepository
{
    int Create(Note note);                        // created_at/updated_at 채움, Id 반환
    void Update(Note note);                        // 전달된 Note 그대로 저장
    void SoftDelete(int id);                       // deleted_at 설정
    void Restore(int id);                          // deleted_at = null
    void Purge(int id);                            // 영구삭제(checklist_items CASCADE)
    void PurgeExpiredTrash(int retentionDays);     // deleted_at 경과분 영구삭제
    Note? Get(int id);
    IReadOnlyList<Note> GetByGroup(int? groupId);  // 활성, groupId=null → 미분류
    IReadOnlyList<Note> GetTrash();                // deleted_at NOT NULL
    IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday);
    Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format);
}
```

`src/Memoria.Core/Data/NoteRepository.cs`:
```csharp
using System.Globalization;
using Dapper;
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public sealed class NoteRepository : INoteRepository
{
    private const string SelectColumns =
        "id AS Id, group_id AS GroupId, type AS Type, title AS Title, body AS Body, " +
        "log_date AS LogDate, report_format AS ReportFormat, report_week_start AS ReportWeekStart, " +
        "pinned AS Pinned, sort_order AS SortOrder, deleted_at AS DeletedAt, " +
        "created_at AS CreatedAt, updated_at AS UpdatedAt";

    private readonly SqliteConnectionFactory _factory;

    public NoteRepository(SqliteConnectionFactory factory) => _factory = factory;

    public int Create(Note note)
    {
        var now = DateTimeOffset.UtcNow;
        note.CreatedAt = now;
        note.UpdatedAt = now;
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            conn.Execute(
                "INSERT INTO notes(group_id, type, title, body, log_date, report_format, report_week_start, " +
                "pinned, sort_order, deleted_at, created_at, updated_at) " +
                "VALUES(@GroupId, @Type, @Title, @Body, @LogDate, @ReportFormat, @ReportWeekStart, " +
                "@Pinned, @SortOrder, @DeletedAt, @CreatedAt, @UpdatedAt);", note);
            note.Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid();");
        }
        return note.Id;
    }

    public void Update(Note note)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute(
                "UPDATE notes SET group_id = @GroupId, type = @Type, title = @Title, body = @Body, " +
                "log_date = @LogDate, report_format = @ReportFormat, report_week_start = @ReportWeekStart, " +
                "pinned = @Pinned, sort_order = @SortOrder, deleted_at = @DeletedAt, " +
                "created_at = @CreatedAt, updated_at = @UpdatedAt WHERE id = @Id;", note);
        }
    }

    public void SoftDelete(int id)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute("UPDATE notes SET deleted_at = @now WHERE id = @id;",
                new { id, now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) });
        }
    }

    public void Restore(int id)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute("UPDATE notes SET deleted_at = NULL WHERE id = @id;", new { id });
        }
    }

    public void Purge(int id)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute("DELETE FROM notes WHERE id = @id;", new { id });
        }
    }

    public void PurgeExpiredTrash(int retentionDays)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToString("O", CultureInfo.InvariantCulture);
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute(
                "DELETE FROM notes WHERE deleted_at IS NOT NULL AND deleted_at < @cutoff;", new { cutoff });
        }
    }

    public Note? Get(int id)
    {
        using var conn = _factory.Open();
        return conn.QuerySingleOrDefault<Note>(
            $"SELECT {SelectColumns} FROM notes WHERE id = @id;", new { id });
    }

    public IReadOnlyList<Note> GetByGroup(int? groupId)
    {
        using var conn = _factory.Open();
        var where = groupId is null ? "group_id IS NULL" : "group_id = @groupId";
        return conn.Query<Note>(
            $"SELECT {SelectColumns} FROM notes WHERE deleted_at IS NULL AND {where} " +
            "ORDER BY pinned DESC, updated_at DESC, id DESC;", new { groupId }).ToList();
    }

    public IReadOnlyList<Note> GetTrash()
    {
        using var conn = _factory.Open();
        return conn.Query<Note>(
            $"SELECT {SelectColumns} FROM notes WHERE deleted_at IS NOT NULL " +
            "ORDER BY deleted_at DESC, id DESC;").ToList();
    }

    public IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday)
    {
        using var conn = _factory.Open();
        return conn.Query<Note>(
            $"SELECT {SelectColumns} FROM notes " +
            "WHERE type = 'checklist' AND deleted_at IS NULL " +
            "AND log_date BETWEEN @Monday AND @Friday " +
            "ORDER BY log_date, id;", new { Monday = monday, Friday = friday }).ToList();
    }

    public Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format)
    {
        using var conn = _factory.Open();
        return conn.QuerySingleOrDefault<Note>(
            $"SELECT {SelectColumns} FROM notes " +
            "WHERE type = 'weekly_report' AND deleted_at IS NULL " +
            "AND report_week_start = @WeekStart AND report_format = @Format LIMIT 1;",
            new { WeekStart = weekStart, Format = format });
    }
}
```

> 메모: `GetChecklistsInWeek`/`FindWeeklyReport`의 `@Monday`/`@Friday`/`@WeekStart`(DateOnly)와 `@Format`(ReportFormatKind)는 Task 6의 TypeHandler로 각각 `yyyy-MM-dd` / `A|B` 문자열로 바인딩된다.

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~NoteRepositoryTests"
```
예상: `Passed!  - Failed: 0, Passed: 6`. (그리고 `GroupRepositoryTests` 전체 4 Passed 재확인.)

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add NoteRepository (CRUD, soft delete, week/report queries)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---
### Task 11: `IChecklistRepository`

**Files:**
- Create: `src/Memoria.Core/Data/IChecklistRepository.cs`, `src/Memoria.Core/Data/ChecklistRepository.cs`
- Test: `tests/Memoria.Tests/Data/ChecklistRepositoryTests.cs`

**Interfaces:**
- Consumes: `SqliteConnectionFactory`, `ChecklistItem`, `ItemKind`, `NoteRepository`(부모 노트 생성용).
- Produces: `IChecklistRepository.AddItem/UpdateItem/DeleteItem/GetByNote` (계약 §4).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Data/ChecklistRepositoryTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Data;

public class ChecklistRepositoryTests
{
    private static int NewNote(SqliteConnectionFactory f) =>
        new NoteRepository(f).Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 22) });

    [Fact]
    public void AddItem_GetByNote_RoundTrips_OrderedBySortOrder()
    {
        using var db = new TestDb();
        var noteId = NewNote(db.Factory);
        var sut = new ChecklistRepository(db.Factory);

        sut.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Issue, Text = "이슈", SortOrder = 1 });
        sut.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "할일", SortOrder = 0, ClientId = 1, IsManual = true });

        var items = sut.GetByNote(noteId);
        items.Should().HaveCount(2);
        items[0].Text.Should().Be("할일");
        items[0].Kind.Should().Be(ItemKind.Task);
        items[0].ClientId.Should().Be(1);
        items[0].IsManual.Should().BeTrue();
        items[0].CreatedAt.Should().BeAfter(DateTimeOffset.UnixEpoch);
        items[1].Kind.Should().Be(ItemKind.Issue);
    }

    [Fact]
    public void UpdateItem_PersistsDoneAndClient()
    {
        using var db = new TestDb();
        var noteId = NewNote(db.Factory);
        var sut = new ChecklistRepository(db.Factory);
        var id = sut.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "t" });

        var item = sut.GetByNote(noteId).Single();
        item.Done = true;
        item.DoneAt = DateTimeOffset.UtcNow;
        item.ClientId = 2;
        sut.UpdateItem(item);

        var reloaded = sut.GetByNote(noteId).Single();
        reloaded.Done.Should().BeTrue();
        reloaded.DoneAt.Should().NotBeNull();
        reloaded.ClientId.Should().Be(2);
    }

    [Fact]
    public void DeleteItem_RemovesIt()
    {
        using var db = new TestDb();
        var noteId = NewNote(db.Factory);
        var sut = new ChecklistRepository(db.Factory);
        var id = sut.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "t" });

        sut.DeleteItem(id);
        sut.GetByNote(noteId).Should().BeEmpty();
    }

    [Fact]
    public void PurgingParentNote_CascadeDeletesItems()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var noteId = NewNote(db.Factory);
        var sut = new ChecklistRepository(db.Factory);
        sut.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "t" });

        notes.Purge(noteId);

        sut.GetByNote(noteId).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'ChecklistRepository' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Data/IChecklistRepository.cs` (계약 §4):
```csharp
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public interface IChecklistRepository
{
    int AddItem(ChecklistItem item);
    void UpdateItem(ChecklistItem item);
    void DeleteItem(int id);
    IReadOnlyList<ChecklistItem> GetByNote(int noteId);  // SortOrder 정렬
}
```

`src/Memoria.Core/Data/ChecklistRepository.cs`:
```csharp
using Dapper;
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public sealed class ChecklistRepository : IChecklistRepository
{
    private const string SelectColumns =
        "id AS Id, note_id AS NoteId, kind AS Kind, text AS Text, done AS Done, done_at AS DoneAt, " +
        "client_id AS ClientId, is_manual AS IsManual, sort_order AS SortOrder, " +
        "created_at AS CreatedAt, updated_at AS UpdatedAt";

    private readonly SqliteConnectionFactory _factory;

    public ChecklistRepository(SqliteConnectionFactory factory) => _factory = factory;

    public int AddItem(ChecklistItem item)
    {
        var now = DateTimeOffset.UtcNow;
        item.CreatedAt = now;
        item.UpdatedAt = now;
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            conn.Execute(
                "INSERT INTO checklist_items(note_id, kind, text, done, done_at, client_id, is_manual, " +
                "sort_order, created_at, updated_at) " +
                "VALUES(@NoteId, @Kind, @Text, @Done, @DoneAt, @ClientId, @IsManual, " +
                "@SortOrder, @CreatedAt, @UpdatedAt);", item);
            item.Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid();");
        }
        return item.Id;
    }

    public void UpdateItem(ChecklistItem item)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute(
                "UPDATE checklist_items SET kind = @Kind, text = @Text, done = @Done, done_at = @DoneAt, " +
                "client_id = @ClientId, is_manual = @IsManual, sort_order = @SortOrder, " +
                "updated_at = @UpdatedAt WHERE id = @Id;", item);
        }
    }

    public void DeleteItem(int id)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute("DELETE FROM checklist_items WHERE id = @id;", new { id });
        }
    }

    public IReadOnlyList<ChecklistItem> GetByNote(int noteId)
    {
        using var conn = _factory.Open();
        return conn.Query<ChecklistItem>(
            $"SELECT {SelectColumns} FROM checklist_items WHERE note_id = @noteId " +
            "ORDER BY sort_order, id;", new { noteId }).ToList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistRepositoryTests"
```
예상: `Passed!  - Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add ChecklistRepository (item CRUD, ordered by sort_order)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 12: `ISearchService` (FTS5)

**Files:**
- Create: `src/Memoria.Core/Data/ISearchService.cs`, `src/Memoria.Core/Data/SearchService.cs`
- Test: `tests/Memoria.Tests/Data/SearchServiceTests.cs`

**Interfaces:**
- Consumes: `SqliteConnectionFactory`, FTS5 트리거(Task 6), `NoteRepository`/`ChecklistRepository`(데이터 준비).
- Produces: `Memoria.Core.Data.SearchHit`, `ISearchService.Search(string) -> IReadOnlyList<SearchHit>` (계약 §4).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Data/SearchServiceTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Data;

public class SearchServiceTests
{
    [Fact]
    public void Search_FindsByTitleAndBody()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var sut = new SearchService(db.Factory);
        var id = notes.Create(new Note { Type = NoteType.Plain, Title = "회의록", Body = "SLD 점검 내용" });

        var hits = sut.Search("SLD");
        hits.Should().ContainSingle(h => h.NoteId == id);
    }

    [Fact]
    public void Search_FindsByChecklistItemText()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var items = new ChecklistRepository(db.Factory);
        var sut = new SearchService(db.Factory);
        var id = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 22) });
        items.AddItem(new ChecklistItem { NoteId = id, Kind = ItemKind.Task, Text = "코모텍 미팅" });

        sut.Search("코모텍").Should().ContainSingle(h => h.NoteId == id);
    }

    [Fact]
    public void Search_ExcludesSoftDeletedNotes()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var sut = new SearchService(db.Factory);
        var id = notes.Create(new Note { Type = NoteType.Plain, Title = "삭제대상", Body = "카본센스 자료" });
        notes.SoftDelete(id);

        sut.Search("카본센스").Should().BeEmpty();
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        using var db = new TestDb();
        var sut = new SearchService(db.Factory);
        sut.Search("   ").Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'SearchService' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Data/ISearchService.cs` (계약 §4):
```csharp
namespace Memoria.Core.Data;

public sealed record SearchHit(int NoteId, string TitlePreview, string Snippet);

public interface ISearchService
{
    /// FTS5로 title+body+items 검색. 빈 쿼리는 빈 결과.
    IReadOnlyList<SearchHit> Search(string query);
}
```

`src/Memoria.Core/Data/SearchService.cs`:
```csharp
using Dapper;

namespace Memoria.Core.Data;

public sealed class SearchService : ISearchService
{
    private readonly SqliteConnectionFactory _factory;

    public SearchService(SqliteConnectionFactory factory) => _factory = factory;

    public IReadOnlyList<SearchHit> Search(string query)
    {
        var trimmed = query?.Trim() ?? "";
        if (trimmed.Length == 0) return [];

        // 사용자 입력을 FTS5 구문 오류 없이 안전하게 평가하기 위해 전체를 인용된 구(phrase)로 감싼다.
        var ftsQuery = "\"" + trimmed.Replace("\"", "\"\"") + "\"";

        using var conn = _factory.Open();
        return conn.Query<SearchHit>(
            "SELECT n.id AS NoteId, " +
            "       COALESCE(NULLIF(n.title, ''), n.body, '') AS TitlePreview, " +
            "       snippet(notes_fts, -1, '', '', ' … ', 8) AS Snippet " +
            "FROM notes_fts " +
            "JOIN notes n ON n.id = notes_fts.rowid " +
            "WHERE notes_fts MATCH @q AND n.deleted_at IS NULL " +
            "ORDER BY rank;",
            new { q = ftsQuery }).ToList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~SearchServiceTests"
```
예상: `Passed!  - Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add SearchService (FTS5 over title/body/items)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---
### Task 13: `ITaggingService` (수동 보호 자동 태깅)

**Files:**
- Create: `src/Memoria.Core/Services/ITaggingService.cs`, `src/Memoria.Core/Services/TaggingService.cs`
- Test: `tests/Memoria.Tests/Services/TaggingServiceTests.cs`

**Interfaces:**
- Consumes: `IClientClassifier`(Task 3), `IClientRepository`(Task 9), `ChecklistItem`, `ItemKind`.
- Produces: `Memoria.Core.Services.ITaggingService.ApplyAutoTag(ChecklistItem) -> ChecklistItem` (계약 §5).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Services/TaggingServiceTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Services;
using Memoria.Tests.Data;
using Xunit;

namespace Memoria.Tests.Services;

public class TaggingServiceTests
{
    private static (TaggingService Svc, ClientRepository Clients) Build(TestDb db)
    {
        var clients = new ClientRepository(db.Factory);
        var svc = new TaggingService(new ClientClassifier(), clients);
        return (svc, clients);
    }

    [Fact]
    public void ApplyAutoTag_AutoTask_GetsClassified()
    {
        using var db = new TestDb();
        var (svc, clients) = Build(db);
        var autoFactoryId = clients.GetAll().Single(c => c.Name == "자율형 공장").Id;

        var item = new ChecklistItem { Kind = ItemKind.Task, Text = "자율형공장 점검", IsManual = false };
        var result = svc.ApplyAutoTag(item);

        result.ClientId.Should().Be(autoFactoryId);
    }

    [Fact]
    public void ApplyAutoTag_ManualTask_IsNotOverwritten()
    {
        using var db = new TestDb();
        var (svc, _) = Build(db);

        var item = new ChecklistItem { Kind = ItemKind.Task, Text = "SLD 점검", IsManual = true, ClientId = 99 };
        var result = svc.ApplyAutoTag(item);

        result.ClientId.Should().Be(99);
    }

    [Fact]
    public void ApplyAutoTag_Issue_StaysUnclassified()
    {
        using var db = new TestDb();
        var (svc, _) = Build(db);

        var item = new ChecklistItem { Kind = ItemKind.Issue, Text = "SLD 장애", IsManual = false };
        var result = svc.ApplyAutoTag(item);

        result.ClientId.Should().BeNull();
    }

    [Fact]
    public void ApplyAutoTag_NoKeyword_ResultsInNull()
    {
        using var db = new TestDb();
        var (svc, _) = Build(db);

        var item = new ChecklistItem { Kind = ItemKind.Task, Text = "기타 잡무", IsManual = false };
        svc.ApplyAutoTag(item).ClientId.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'TaggingService' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Services/ITaggingService.cs` (계약 §5):
```csharp
using Memoria.Core.Models;

namespace Memoria.Core.Services;

/// task 텍스트 변경 시 자동 분류를 적용(수동 교정 항목은 보호).
public interface ITaggingService
{
    /// item이 Task이고 IsManual=false이면 현재 규칙으로 ClientId 재계산하여 반환(변경된 item).
    /// Issue이거나 IsManual=true이면 그대로 반환.
    ChecklistItem ApplyAutoTag(ChecklistItem item);
}
```

`src/Memoria.Core/Services/TaggingService.cs`:
```csharp
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Core.Services;

public sealed class TaggingService : ITaggingService
{
    private readonly IClientClassifier _classifier;
    private readonly IClientRepository _clients;

    public TaggingService(IClientClassifier classifier, IClientRepository clients)
    {
        _classifier = classifier;
        _clients = clients;
    }

    public ChecklistItem ApplyAutoTag(ChecklistItem item)
    {
        if (item.Kind != ItemKind.Task || item.IsManual) return item;

        var rules = _clients.GetRules();
        var enabledIds = _clients.GetAll(enabledOnly: true).Select(c => c.Id).ToHashSet();
        item.ClientId = _classifier.Classify(item.Text, rules, enabledIds);
        return item;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~TaggingServiceTests"
```
예상: `Passed!  - Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add TaggingService (auto-tag tasks, protect manual)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 14: `IWeeklyReportService` (수집 + 재분류 + 렌더 위임)

**Files:**
- Create: `src/Memoria.Core/Services/IWeeklyReportService.cs`, `src/Memoria.Core/Services/WeeklyReportService.cs`
- Test: `tests/Memoria.Tests/Services/WeeklyReportServiceTests.cs`

**Interfaces:**
- Consumes: `IWeekCalculator`(Task 2), `INoteRepository`(Task 10), `IChecklistRepository`(Task 11), `IClientClassifier`(Task 3), `IClientRepository`(Task 9), `IWeeklyReportRenderer`(Task 4/5), `ReportRenderOptions`/`ReportTask`/`ReportIssue`/`WeeklyReportData`.
- Produces: `Memoria.Core.Services.WeeklyReportBuildResult`, `IWeeklyReportService.Build(...)/Render(...)` (계약 §5).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Services/WeeklyReportServiceTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Memoria.Tests.Data;
using Xunit;

namespace Memoria.Tests.Services;

public class WeeklyReportServiceTests
{
    private static (WeeklyReportService Svc, NoteRepository Notes, ChecklistRepository Items, ClientRepository Clients) Build(TestDb db)
    {
        var notes = new NoteRepository(db.Factory);
        var items = new ChecklistRepository(db.Factory);
        var clients = new ClientRepository(db.Factory);
        var svc = new WeeklyReportService(
            new WeekCalculator(), notes, items, new ClientClassifier(), clients, new WeeklyReportRenderer());
        return (svc, notes, items, clients);
    }

    [Fact]
    public void Build_CollectsWeekTasks_ReclassifiesAuto_CountsUnclassified()
    {
        using var db = new TestDb();
        var (svc, notes, items, clients) = Build(db);
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 23) });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "SLD 점검", SortOrder = 0 });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "기타 정리", SortOrder = 1 });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Issue, Text = "장비 오류", SortOrder = 2 });

        var sldId = clients.GetAll().Single(c => c.Name == "SLD").Id;
        var options = new ReportRenderOptions
        {
            WeekStart = new DateOnly(2026, 6, 22),
            WeekEnd = new DateOnly(2026, 6, 26),
            Clients = clients.GetAll(enabledOnly: true),
        };

        var result = svc.Build(new DateOnly(2026, 6, 24), options);

        result.Monday.Should().Be(new DateOnly(2026, 6, 22));
        result.Friday.Should().Be(new DateOnly(2026, 6, 26));
        result.Data.Tasks.Should().HaveCount(2);
        result.Data.Issues.Should().HaveCount(1);
        result.UnclassifiedTaskCount.Should().Be(1);
        result.Data.Tasks.Should().Contain(t => t.Text == "SLD 점검" && t.ClientId == sldId);
        result.Data.Tasks.Should().Contain(t => t.Text == "기타 정리" && t.ClientId == null);
    }

    [Fact]
    public void Build_KeepsManualClassification()
    {
        using var db = new TestDb();
        var (svc, notes, items, clients) = Build(db);
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 23) });
        // 수동으로 코모텍 지정했지만 텍스트는 SLD → 재분류로 덮어쓰지 않아야 함
        var komotekId = clients.GetAll().Single(c => c.Name == "코모텍").Id;
        items.AddItem(new ChecklistItem
        {
            NoteId = noteId, Kind = ItemKind.Task, Text = "SLD 점검",
            IsManual = true, ClientId = komotekId,
        });

        var options = new ReportRenderOptions
        {
            WeekStart = new DateOnly(2026, 6, 22),
            WeekEnd = new DateOnly(2026, 6, 26),
            Clients = clients.GetAll(enabledOnly: true),
        };
        var result = svc.Build(new DateOnly(2026, 6, 23), options);

        result.Data.Tasks.Single().ClientId.Should().Be(komotekId);
    }

    [Fact]
    public void Render_DelegatesToRenderer_FormatB()
    {
        using var db = new TestDb();
        var (svc, notes, items, clients) = Build(db);
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 23) });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "SLD 점검", SortOrder = 0 });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "기타 정리", SortOrder = 1 });

        var options = new ReportRenderOptions
        {
            WeekStart = new DateOnly(2026, 6, 22),
            WeekEnd = new DateOnly(2026, 6, 26),
            Clients = clients.GetAll(enabledOnly: true),
        };
        var result = svc.Build(new DateOnly(2026, 6, 23), options);

        var text = svc.Render(ReportFormatKind.B, result.Data, options);

        text.Should().StartWith("[ 이승현 주간 보고 (06/22 ~ 06/26) ]:");
        text.Should().Contain("[ SLD ]\n\t* SLD 점검");
        text.Should().Contain("[ 미분류 ]\n\t* 기타 정리");
    }

    [Fact]
    public void Build_IncludeDoneOnly_AffectsUnclassifiedCount()
    {
        using var db = new TestDb();
        var (svc, notes, items, clients) = Build(db);
        var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 23) });
        // 미분류 task 2개: 하나만 done
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "잡무1", Done = true, SortOrder = 0 });
        items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "잡무2", Done = false, SortOrder = 1 });

        var options = new ReportRenderOptions
        {
            WeekStart = new DateOnly(2026, 6, 22),
            WeekEnd = new DateOnly(2026, 6, 26),
            Clients = clients.GetAll(enabledOnly: true),
            IncludeDoneOnly = true,
        };

        var result = svc.Build(new DateOnly(2026, 6, 23), options);

        result.Data.Tasks.Should().HaveCount(2);   // Data는 전부 포함(렌더러가 필터)
        result.UnclassifiedTaskCount.Should().Be(1); // done인 미분류만 카운트
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'WeeklyReportService' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Services/IWeeklyReportService.cs` (계약 §5):
```csharp
using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.Core.Services;

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

`src/Memoria.Core/Services/WeeklyReportService.cs`:
```csharp
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.Core.Services;

public sealed class WeeklyReportService : IWeeklyReportService
{
    private readonly IWeekCalculator _week;
    private readonly INoteRepository _notes;
    private readonly IChecklistRepository _checklist;
    private readonly IClientClassifier _classifier;
    private readonly IClientRepository _clients;
    private readonly IWeeklyReportRenderer _renderer;

    public WeeklyReportService(
        IWeekCalculator week,
        INoteRepository notes,
        IChecklistRepository checklist,
        IClientClassifier classifier,
        IClientRepository clients,
        IWeeklyReportRenderer renderer)
    {
        _week = week;
        _notes = notes;
        _checklist = checklist;
        _classifier = classifier;
        _clients = clients;
        _renderer = renderer;
    }

    public WeeklyReportBuildResult Build(DateOnly anyDateInWeek, ReportRenderOptions options)
    {
        var (monday, friday) = _week.GetWorkWeek(anyDateInWeek);
        var rules = _clients.GetRules();
        var enabledIds = _clients.GetAll(enabledOnly: true).Select(c => c.Id).ToHashSet();

        var tasks = new List<ReportTask>();
        var issues = new List<ReportIssue>();

        foreach (var note in _notes.GetChecklistsInWeek(monday, friday))
        {
            foreach (var item in _checklist.GetByNote(note.Id))
            {
                if (item.Kind == ItemKind.Task)
                {
                    int? clientId = item.IsManual
                        ? item.ClientId
                        : _classifier.Classify(item.Text, rules, enabledIds);
                    tasks.Add(new ReportTask(item.Text, clientId, item.Done));
                }
                else
                {
                    issues.Add(new ReportIssue(item.Text));
                }
            }
        }

        var relevant = options.IncludeDoneOnly ? tasks.Where(t => t.Done) : tasks;
        int unclassified = relevant.Count(t => t.ClientId is null);

        var data = new WeeklyReportData(tasks, issues);
        return new WeeklyReportBuildResult(data, unclassified, monday, friday);
    }

    public string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options)
        => _renderer.Render(format, data, options);
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~WeeklyReportServiceTests"
```
예상: `Passed!  - Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit + 중간 테스트 스위트 검증**
```bash
# 이 시점(Task 1~14)까지의 전체 스위트
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests"
# 예상: Passed! - Failed: 0  (Models 3 + WeekCalc 4 + Classifier 6 + RendererA 2 + RendererB 2
#        + Init 3 + Settings 4 + Group 4 + Client 6 + Note 6 + Checklist 4 + Search 4
#        + Tagging 4 + WeeklyReportSvc 4 = 60 Passed; 이후 Task 15(Backup) +5, Task 16(CoreDI) +2 → 최종 67)

git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add WeeklyReportService (collect, reclassify auto, render delegate)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 15: `IBackupService` (일일 백업·회전·무결성·복원)

**Files:**
- Create: `src/Memoria.Core/Data/IBackupService.cs`, `src/Memoria.Core/Data/BackupService.cs`
- Test: `tests/Memoria.Tests/Data/BackupServiceTests.cs`

**Interfaces:**
- Consumes: `SqliteConnectionFactory`(Task 6 — `WriteSync`/`Write`/`DatabasePath`/`CloseForRestore`/`ReopenAfterRestore`), `IDatabaseInitializer`(데이터 준비), `NoteRepository`(복원 검증용).
- Produces: `Memoria.Core.Data.IBackupService.BackupIfDue(int)/IsDatabaseHealthy()/TryRestoreFromLatestBackup()` (계약 §8). 부트스트랩 §9.4의 5)·6) 단계가 M9에서 이를 소비한다.

> **백업 정책(계약 §8 / 스펙 §7.7):** 하루 1회 `VACUUM INTO 'backups/memoria-yyyyMMdd.db'`로 WAL 내용을 포함한 일관 스냅샷을 만든다(단순 파일복사 금지). 백업/복원은 모두 `lock (factory.WriteSync)` 하에 수행한다(단일 직렬 라이터). `backups/`는 DB 파일과 같은 디렉터리의 하위 폴더다. 회전은 파일명(=날짜) 내림차순으로 `retentionCount` 개만 남긴다. 복원은 현재 DB(+`-wal`/`-shm`)를 `*.corrupt`로 격리한 뒤 최신 백업을 DB 경로로 복사한다.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/Data/BackupServiceTests.cs`:
```csharp
using System.Globalization;
using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Memoria.Tests.Data;

public class BackupServiceTests
{
    // 각 테스트는 전용 임시 디렉터리(=DB 파일의 부모) 안에서 동작한다.
    private static string NewDbPath() =>
        Path.Combine(
            Path.GetTempPath(),
            "memoria_backup_" + Guid.NewGuid().ToString("N"),
            "memoria.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        var dir = Path.GetDirectoryName(dbPath)!;
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private static string BackupsDir(string dbPath) =>
        Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");

    [Fact]
    public void BackupIfDue_CreatesDatedSnapshot_FirstTime_ThenSkipsSameDay()
    {
        var path = NewDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();
            var sut = new BackupService(factory, path);

            sut.BackupIfDue(7).Should().BeTrue();
            Directory.GetFiles(BackupsDir(path), "memoria-*.db").Should().HaveCount(1);

            sut.BackupIfDue(7).Should().BeFalse(); // 같은 날 두 번째 호출은 skip
            Directory.GetFiles(BackupsDir(path), "memoria-*.db").Should().HaveCount(1);
        }
        finally { factory.Dispose(); Cleanup(path); }
    }

    [Fact]
    public void BackupIfDue_RetainsOnlyRetentionCount_NewestBackups()
    {
        var path = NewDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();
            Directory.CreateDirectory(BackupsDir(path));
            // 오래된 더미 백업 3개(파일명=날짜)
            foreach (var d in new[] { "20200101", "20200102", "20200103" })
                File.WriteAllText(Path.Combine(BackupsDir(path), $"memoria-{d}.db"), "x");

            new BackupService(factory, path).BackupIfDue(2).Should().BeTrue();

            var remaining = Directory.GetFiles(BackupsDir(path), "memoria-*.db")
                .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            remaining.Should().HaveCount(2);
            var today = DateTimeOffset.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            remaining.Should().Contain($"memoria-{today}.db"); // 오늘 백업은 유지
        }
        finally { factory.Dispose(); Cleanup(path); }
    }

    [Fact]
    public void IsDatabaseHealthy_ReturnsTrue_ForFreshDb()
    {
        var path = NewDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();
            new BackupService(factory, path).IsDatabaseHealthy().Should().BeTrue();
        }
        finally { factory.Dispose(); Cleanup(path); }
    }

    [Fact]
    public void TryRestoreFromLatestBackup_ReturnsFalse_WhenNoBackupExists()
    {
        var path = NewDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();
            new BackupService(factory, path).TryRestoreFromLatestBackup().Should().BeFalse();
        }
        finally { factory.Dispose(); Cleanup(path); }
    }

    [Fact]
    public void TryRestoreFromLatestBackup_RollsBackToBackupState_AndQuarantinesCurrent()
    {
        var path = NewDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();
            var notes = new NoteRepository(factory);
            var beforeId = notes.Create(new Note { Type = NoteType.Plain, Title = "before" });

            var sut = new BackupService(factory, path);
            sut.BackupIfDue(7).Should().BeTrue();              // 백업 스냅샷에는 'before'만 존재

            var afterId = notes.Create(new Note { Type = NoteType.Plain, Title = "after" }); // 백업 이후 추가

            sut.TryRestoreFromLatestBackup().Should().BeTrue();

            notes.Get(beforeId).Should().NotBeNull();          // 복원: 백업 시점 데이터 유지
            notes.Get(afterId).Should().BeNull();              // 복원: 백업 이후 변경은 사라짐
            Directory.GetFiles(Path.GetDirectoryName(path)!, "*.corrupt").Should().NotBeEmpty(); // 격리 파일 생성
        }
        finally { factory.Dispose(); Cleanup(path); }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS0246: The type or namespace name 'BackupService' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/Data/IBackupService.cs` (계약 §8 그대로):
```csharp
namespace Memoria.Core.Data;

public interface IBackupService
{
    /// 마지막 백업이 오늘이 아니면 backups/memoria-yyyyMMdd.db 로 일관 스냅샷(VACUUM INTO) 생성 후
    /// retentionCount 개만 남기고 오래된 것 삭제. 백업했으면 true.
    bool BackupIfDue(int retentionCount);
    /// PRAGMA integrity_check == 'ok' 이면 true.
    bool IsDatabaseHealthy();
    /// 손상 시: 현재 DB를 *.corrupt 로 격리 후 최신 정상 백업을 복원. 복원 성공 시 true(복원본 없으면 false).
    bool TryRestoreFromLatestBackup();
}
```

`src/Memoria.Core/Data/BackupService.cs`:
```csharp
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Memoria.Core.Data;

public sealed class BackupService : IBackupService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly string _databasePath;
    private readonly string _backupDirectory;

    public BackupService(SqliteConnectionFactory factory, string databaseFilePath)
    {
        _factory = factory;
        _databasePath = databaseFilePath;
        _backupDirectory = Path.Combine(Path.GetDirectoryName(databaseFilePath)!, "backups");
    }

    public bool BackupIfDue(int retentionCount)
    {
        Directory.CreateDirectory(_backupDirectory);
        var today = DateTimeOffset.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var target = Path.Combine(_backupDirectory, $"memoria-{today}.db");
        if (File.Exists(target)) return false; // 오늘 이미 백업함

        // VACUUM INTO 는 바인딩 파라미터를 받지 않으므로 경로를 문자열 리터럴로 이스케이프해 주입.
        var literal = target.Replace("'", "''");
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute($"VACUUM INTO '{literal}';");
        }

        RotateBackups(retentionCount);
        return true;
    }

    public bool IsDatabaseHealthy()
    {
        try
        {
            using var conn = _factory.Open();
            var result = conn.ExecuteScalar<string>("PRAGMA integrity_check;");
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public bool TryRestoreFromLatestBackup()
    {
        var latest = EnumerateBackupsNewestFirst().FirstOrDefault();
        if (latest is null) return false;

        lock (_factory.WriteSync)
        {
            _factory.CloseForRestore();      // 영속 쓰기 연결 닫기(파일 잠금 해제)
            SqliteConnection.ClearAllPools(); // 풀링된 읽기 연결도 해제
            QuarantineCurrentDatabase();
            File.Copy(latest, _databasePath, overwrite: true);
            _factory.ReopenAfterRestore();   // 새 DB로 쓰기 연결 재개
        }
        return true;
    }

    private void RotateBackups(int retentionCount)
    {
        foreach (var old in EnumerateBackupsNewestFirst().Skip(Math.Max(retentionCount, 0)))
        {
            try { File.Delete(old); } catch (IOException) { /* best-effort rotation */ }
        }
    }

    // 백업 파일을 파일명(=날짜) 내림차순(최신 먼저)으로 반환.
    private IReadOnlyList<string> EnumerateBackupsNewestFirst()
    {
        if (!Directory.Exists(_backupDirectory)) return [];
        return Directory.GetFiles(_backupDirectory, "memoria-*.db")
            .OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal)
            .ToList();
    }

    private void QuarantineCurrentDatabase()
    {
        var stamp = DateTimeOffset.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var src = _databasePath + suffix;
            if (File.Exists(src))
                File.Move(src, $"{_databasePath}.{stamp}.corrupt{suffix}", overwrite: true);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~BackupServiceTests"
```
예상: `Passed!  - Failed: 0, Passed: 5`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add BackupService (daily VACUUM INTO snapshot, rotation, integrity, restore)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 16: `CoreServiceRegistration.AddMemoriaCore` (Core DI 등록)

**Files:**
- Create: `src/Memoria.Core/CoreServiceRegistration.cs`
- Test: `tests/Memoria.Tests/CoreServiceRegistrationTests.cs`

**Interfaces:**
- Consumes: 본 마일스톤의 모든 산출물(`SqliteConnectionFactory`, `IDatabaseInitializer`, `IBackupService`, 모든 Repository, `ISearchService`, `IClientClassifier`, `IWeekCalculator`, `IWeeklyReportRenderer`, `ITaggingService`, `IWeeklyReportService`).
- Produces: `Memoria.Core.CoreServiceRegistration.AddMemoriaCore(IServiceCollection, string databaseFilePath)` (계약 §9.1). M2 부트스트랩 §9.4의 3) 단계가 이를 소비한다.

먼저 DI 패키지를 추가한다(계약 §9.1 — Core는 추상화만, Tests는 컨테이너 구현 포함):
```bash
dotnet.exe add "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\src\Memoria.Core\Memoria.Core.csproj" package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet.exe add "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests\Memoria.Tests.csproj" package Microsoft.Extensions.DependencyInjection
```

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/CoreServiceRegistrationTests.cs`:
```csharp
using FluentAssertions;
using Memoria.Core;
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Memoria.Tests;

public class CoreServiceRegistrationTests
{
    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), "memoria_di_" + Guid.NewGuid().ToString("N") + ".db");

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(p)) { try { File.Delete(p); } catch { /* best-effort cleanup */ } }
    }

    [Fact]
    public void AddMemoriaCore_ResolvesAllCoreServices()
    {
        var path = NewDbPath();
        var provider = new ServiceCollection().AddMemoriaCore(path).BuildServiceProvider();
        try
        {
            provider.GetRequiredService<SqliteConnectionFactory>().Should().NotBeNull();
            provider.GetRequiredService<IDatabaseInitializer>().Should().NotBeNull();
            provider.GetRequiredService<IBackupService>().Should().NotBeNull();
            provider.GetRequiredService<IGroupRepository>().Should().NotBeNull();
            provider.GetRequiredService<INoteRepository>().Should().NotBeNull();
            provider.GetRequiredService<IChecklistRepository>().Should().NotBeNull();
            provider.GetRequiredService<IClientRepository>().Should().NotBeNull();
            provider.GetRequiredService<ISettingsRepository>().Should().NotBeNull();
            provider.GetRequiredService<ISearchService>().Should().NotBeNull();
            provider.GetRequiredService<IClientClassifier>().Should().NotBeNull();
            provider.GetRequiredService<IWeekCalculator>().Should().NotBeNull();
            provider.GetRequiredService<IWeeklyReportRenderer>().Should().NotBeNull();
            provider.GetRequiredService<ITaggingService>().Should().NotBeNull();
            provider.GetRequiredService<IWeeklyReportService>().Should().NotBeNull();
        }
        finally { provider.Dispose(); Cleanup(path); }
    }

    [Fact]
    public void AddMemoriaCore_EndToEnd_InitializeAndRenderReport()
    {
        var path = NewDbPath();
        var provider = new ServiceCollection().AddMemoriaCore(path).BuildServiceProvider();
        try
        {
            provider.GetRequiredService<IDatabaseInitializer>().EnsureReady();

            var notes = provider.GetRequiredService<INoteRepository>();
            var items = provider.GetRequiredService<IChecklistRepository>();
            var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 23) });
            items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "SLD 점검" });

            var clients = provider.GetRequiredService<IClientRepository>();
            var options = new ReportRenderOptions
            {
                WeekStart = new DateOnly(2026, 6, 22),
                WeekEnd = new DateOnly(2026, 6, 26),
                Clients = clients.GetAll(enabledOnly: true),
            };

            var svc = provider.GetRequiredService<IWeeklyReportService>();
            var result = svc.Build(new DateOnly(2026, 6, 23), options);
            svc.Render(ReportFormatKind.B, result.Data, options)
               .Should().Contain("[ SLD ]\n\t* SLD 점검");
        }
        finally { provider.Dispose(); Cleanup(path); }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\Memoria.sln"
```
예상 실패: `error CS1061: 'IServiceCollection' does not contain a definition for 'AddMemoriaCore'`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.Core/CoreServiceRegistration.cs` (계약 §9.1):
```csharp
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Memoria.Core;

public static class CoreServiceRegistration
{
    /// SqliteConnectionFactory(databaseFilePath) + 모든 Repository/Service/Renderer/Classifier/
    /// WeekCalculator/TaggingService/WeeklyReportService/SearchService/IBackupService/IDatabaseInitializer 등록.
    public static IServiceCollection AddMemoriaCore(
        this IServiceCollection services, string databaseFilePath)
    {
        // 단일 영속 쓰기 연결을 공유하려면 팩토리는 싱글턴이어야 한다(계약 §8).
        services.AddSingleton(new SqliteConnectionFactory(databaseFilePath));

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IBackupService>(sp =>
            new BackupService(sp.GetRequiredService<SqliteConnectionFactory>(), databaseFilePath));

        services.AddSingleton<IGroupRepository, GroupRepository>();
        services.AddSingleton<INoteRepository, NoteRepository>();
        services.AddSingleton<IChecklistRepository, ChecklistRepository>();
        services.AddSingleton<IClientRepository, ClientRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<ISearchService, SearchService>();

        services.AddSingleton<IClientClassifier, ClientClassifier>();
        services.AddSingleton<IWeekCalculator, WeekCalculator>();
        services.AddSingleton<IWeeklyReportRenderer, WeeklyReportRenderer>();

        services.AddSingleton<ITaggingService, TaggingService>();
        services.AddSingleton<IWeeklyReportService, WeeklyReportService>();

        return services;
    }
}
```

> 메모: `DatabaseInitializer`/각 Repository/`TaggingService`/`WeeklyReportService`의 생성자 인자(`SqliteConnectionFactory`, 인터페이스들)는 모두 위 등록으로 해소된다. `SqliteConnectionFactory`는 `IDisposable`이므로 `ServiceProvider` Dispose 시 함께 정리된다(부트스트랩 §9.4 OnExit의 checkpoint+Dispose와 일치).

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests" --filter "FullyQualifiedName~CoreServiceRegistrationTests"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit + 전체 테스트 스위트 검증**
```bash
# 전체 스위트(모든 Task 통합)
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\tests\Memoria.Tests"
# 예상: Passed! - Failed: 0 (Task 1~14의 60 + Backup 5 + CoreDI 2 = 67 Passed)

git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria" commit -m "feat(core): add AddMemoriaCore DI registration for all Core services

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 완료 기준 (Definition of Done — M1)

- `dotnet.exe build "...\Memoria.sln"` 성공(경고 0 목표, 오류 0).
- `dotnet.exe test "...\tests\Memoria.Tests"` 전부 통과(약 67개, Failed 0).
- 계약 §1~§6, §8(백업/무결성), §9.1(Core DI 등록)의 모든 타입/인터페이스가 `Memoria.Core`에 구현됨.
- 양식 A/B 골든 테스트가 탭/빈줄/콜론/날짜포맷까지 글자단위 일치.
- DB 스키마 v1 + PRAGMA(WAL/foreign_keys/busy_timeout) + `_migrations`/`user_version` 러너 + 첫실행 시드 + FTS5 동기화 트리거 동작.
- `SqliteConnectionFactory`가 **단일 영속 쓰기 연결 + `object WriteSync` 락**을 노출하고, 모든 쓰기(리포지토리·마이그레이션·시드·백업)가 `lock (WriteSync)` 하에 `Write` 연결로 수행됨(스펙 §7.7 단일 직렬 라이터).
- `IBackupService`(일일 `VACUUM INTO` 스냅샷·회전·`integrity_check`·격리/복원)와 `CoreServiceRegistration.AddMemoriaCore`(전 서비스 DI 해소)가 동작.
- `Memoria.App`(WPF)는 본 마일스톤 범위 밖(M2에서 생성).

## 검증 노트

- 본 마일스톤은 **100% 자동 테스트**이며 수동 검증 체크포인트는 없다(UI/Win32 없음). UI 수동 검증은 M2 이후 계획에서 다룬다.
- 모든 빌드/테스트는 Windows `dotnet.exe` + Windows 절대경로로 실행한다(계약 §7). WSL에서 `dotnet`(Linux)로 실행하지 않는다 — 다만 Core는 net9.0이라 Linux SDK로도 컴파일은 가능하나, 계약 규약 통일을 위해 `dotnet.exe`를 사용한다.


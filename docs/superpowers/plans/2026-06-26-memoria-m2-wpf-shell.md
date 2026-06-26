# Memoria M2 — WPF Shell + Plain Editor + Autosave Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** WPF 셸(메인 창 + 사이드바 그룹 트리 + 메모 목록 + 일반 에디터)을 세우고, 디바운스 자동저장과 크래시 복구 저널로 "저장 행위 없는" 메모 경험을 완성한다.

**Architecture:** `Memoria.App`(net9.0-windows, UseWPF)를 신설해 솔루션에 추가하고, `App.xaml.cs`를 DI 컴포지션 루트로 삼아 M1의 Core 리포지토리/서비스와 M2의 App 서비스(자동저장·복구저널)를 등록한다. 모든 화면 로직은 `CommunityToolkit.Mvvm` 기반 `MainViewModel`/헬퍼/서비스로 분리해 `Memoria.Tests`(net9.0-windows, xUnit + FluentAssertions)에서 자동 테스트하고, 창/렌더/전역 동작은 수동 검증 체크포인트로 확인한다. 쓰기 경로는 App에서 자동저장 서비스 하나로 일원화한다(직렬 라이터/busy_timeout는 M1 Core가 보장).

**Tech Stack:** C# / .NET 9, WPF(net9.0-windows, UseWPF), CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, System.TimeProvider(테스트는 Microsoft.Extensions.TimeProvider.Testing의 FakeTimeProvider), System.Text.Json, xUnit + FluentAssertions.

## Global Constraints
- 런타임: **.NET 9**.
- TFM: **Core=net9.0, App=net9.0-windows(+`<UseWPF>true</UseWPF>`), Tests=net9.0-windows**.
- DB/데이터 위치: **`%LOCALAPPDATA%\Memoria`** (로밍/네트워크 경로 금지). DB 파일 = `%LOCALAPPDATA%\Memoria\memoria.db`, 복구 저널 = `%LOCALAPPDATA%\Memoria\recovery\{noteId}.json`.
- WPF publish: **트리밍(`PublishTrimmed`) 금지, `EnableCompressionInSingleFile` 금지**.
- 빌드/테스트: **Windows `dotnet.exe` + Windows 절대경로**(WSL에서 호출, WPF는 Linux dotnet 불가).
- 자동저장 디바운스: **`autosave.debounceMs`(기본 500ms)**. 콘텐츠(`title`/`body`) 변경에만 `updated_at` 갱신(§7.7) — 그룹 이동/pin/sort는 갱신하지 않음.
- 제목 표시 규칙(§5.1): `title`이 비어 있으면 `body` 첫(비어있지 않은) 줄을 **표시용 제목**으로 사용(컬럼 미저장).
- 분류 우선순위(전역): **자율형공장 > SLD**(이 마일스톤 직접 구현 대상 아님, 참고).
- 색상: **모든 색/브러시는 `DynamicResource`만 사용**(StaticResource 금지). 테마 사전 교체는 M7. M2는 기본 브러시 키만 `App.Resources`에 둔다.
- 의존 방향: `Memoria.App` → `Memoria.Core`(역방향 의존 금지). ViewModel은 WPF 타입에 의존하지 않고 `CommunityToolkit.Mvvm`만 사용. code-behind는 얇게 유지.
- 쓰기 경로 일원화: 본문/항목 영속화는 App의 `IAutosaveService` 콜백을 통해 수행(직렬 라이터/`busy_timeout=5000`은 M1 Core 연결 정책으로 가정).

### M1 의존 가정 (Consumes)
이 마일스톤은 M1이 다음을 이미 제공한다고 가정한다(계약 §1·§4·§6):
- 모델: `Memoria.Core.Models.{Note, Group, NoteType}`.
- 리포지토리/초기화: `Memoria.Core.Data.{INoteRepository, IGroupRepository, ISettingsRepository, IDatabaseInitializer}`.
- 상수: `Memoria.Core.SettingsKeys.AutosaveDebounceMs`.
- **DI 등록 진입점**: `Memoria.Core`가 `IServiceCollection.AddMemoriaCore(string databasePath)` 확장 메서드로 모든 Core 구현(초기화/리포지토리/서비스, 단일 직렬 라이터 연결)을 등록한다고 가정한다. (M1이 이 확장을 노출하지 않았다면, Task 9에서 `AddMemoriaCore` 호출 대신 M1의 실제 concrete 클래스명을 각 interface에 1:1로 `AddSingleton` 등록한다. interface 이름은 본 계약이 단일 진리원천이다.)

---

### Task 1: App 프로젝트 스캐폴딩 + 데이터 경로 결정(AppPaths)
**Files:**
- Create: `src/Memoria.App/Memoria.App.csproj` (dotnet new wpf), `src/Memoria.App/AppPaths.cs`
- Modify: `Memoria.sln`, `tests/Memoria.Tests/Memoria.Tests.csproj`
- Test: `tests/Memoria.Tests/App/AppPathsTests.cs`

**Interfaces:**
- Consumes: (없음 — 프로젝트 스캐폴딩 + BCL `Environment.SpecialFolder.LocalApplicationData`)
- Produces: `Memoria.App.AppPaths.{DataDirectory, DatabaseFile, RecoveryDirectory, EnsureDirectories()}` — 이후 모든 Task와 DI 루트가 의존하는 경로 결정 진리원천.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/AppPathsTests.cs`:
```csharp
using System;
using System.IO;
using FluentAssertions;
using Memoria.App;
using Xunit;

namespace Memoria.Tests.App;

public class AppPathsTests
{
    [Fact]
    public void DataDirectory_is_under_LocalApplicationData_Memoria()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AppPaths.DataDirectory.Should().Be(Path.Combine(local, "Memoria"));
    }

    [Fact]
    public void DatabaseFile_and_RecoveryDirectory_are_under_DataDirectory()
    {
        AppPaths.DatabaseFile.Should().Be(Path.Combine(AppPaths.DataDirectory, "memoria.db"));
        AppPaths.RecoveryDirectory.Should().Be(Path.Combine(AppPaths.DataDirectory, "recovery"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

먼저 프로젝트를 만들고 솔루션/참조/패키지를 연결한다(이 단계가 없으면 `Memoria.App` 네임스페이스를 찾을 수 없어 컴파일 자체가 실패한다):
```bash
dotnet.exe new wpf -o "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App" -n Memoria.App
dotnet.exe sln "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\Memoria.sln" add "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\Memoria.App.csproj"
dotnet.exe add "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\Memoria.App.csproj" reference "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.Core\Memoria.Core.csproj"
dotnet.exe add "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\Memoria.App.csproj" package CommunityToolkit.Mvvm
dotnet.exe add "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\Memoria.App.csproj" package Microsoft.Extensions.DependencyInjection
dotnet.exe add "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests\Memoria.Tests.csproj" reference "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\Memoria.App.csproj"
dotnet.exe add "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests\Memoria.Tests.csproj" package Microsoft.Extensions.TimeProvider.Testing
```
그 다음 테스트 실행:
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~AppPathsTests"
```
예상 실패: `error CS0103: The name 'AppPaths' does not exist` 또는 `Memoria.App.AppPaths` 미존재로 빌드 실패.

- [ ] **Step 3: Write minimal implementation**

`dotnet new wpf`가 만든 기본 `MainWindow.xaml`/`MainWindow.xaml.cs`는 Task 9에서 교체하므로 지금은 둔다. `src/Memoria.App/AppPaths.cs`:
```csharp
using System;
using System.IO;

namespace Memoria.App;

/// 모든 데이터 경로의 단일 결정 지점. DB/복구 저널은 %LOCALAPPDATA%\Memoria 하위.
public static class AppPaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Memoria");

    public static string DatabaseFile => Path.Combine(DataDirectory, "memoria.db");

    public static string RecoveryDirectory => Path.Combine(DataDirectory, "recovery");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(RecoveryDirectory);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~AppPathsTests"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" commit -m "feat(app): scaffold Memoria.App WPF project and AppPaths data-dir resolution

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: 크래시 복구 저널(IRecoveryJournal)
**Files:**
- Create: `src/Memoria.App/Services/IRecoveryJournal.cs`, `src/Memoria.App/Services/RecoveryJournal.cs`
- Test: `tests/Memoria.Tests/App/RecoveryJournalTests.cs`

**Interfaces:**
- Consumes: `AppPaths.RecoveryDirectory`(Task 1) — 단, 테스트를 위해 디렉터리는 생성자 주입.
- Produces: `Memoria.App.Services.RecoverySnapshot(int NoteId, string? Title, string? Body, DateTimeOffset CapturedAt)`, `Memoria.App.Services.IRecoveryJournal.{Append, Clear, DetectPending}` — Task 6(에디터)/Task 9(DI 루트 등록·시작 복구)가 소비.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/RecoveryJournalTests.cs`:
```csharp
using System;
using System.IO;
using FluentAssertions;
using Memoria.App.Services;
using Xunit;

namespace Memoria.Tests.App;

public class RecoveryJournalTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "memoria-rec-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Append_then_DetectPending_returns_last_snapshot_per_note()
    {
        var journal = new RecoveryJournal(TempDir());
        journal.Append(new RecoverySnapshot(7, "T", "B1", DateTimeOffset.UnixEpoch));
        journal.Append(new RecoverySnapshot(7, "T", "B2", DateTimeOffset.UnixEpoch));

        var pending = journal.DetectPending();

        pending.Should().ContainSingle();
        pending[0].NoteId.Should().Be(7);
        pending[0].Body.Should().Be("B2");
    }

    [Fact]
    public void Clear_removes_pending_for_note()
    {
        var journal = new RecoveryJournal(TempDir());
        journal.Append(new RecoverySnapshot(3, null, "x", DateTimeOffset.UnixEpoch));

        journal.Clear(3);

        journal.DetectPending().Should().BeEmpty();
    }

    [Fact]
    public void DetectPending_returns_one_per_note_across_files()
    {
        var journal = new RecoveryJournal(TempDir());
        journal.Append(new RecoverySnapshot(1, null, "a", DateTimeOffset.UnixEpoch));
        journal.Append(new RecoverySnapshot(2, null, "b", DateTimeOffset.UnixEpoch));

        journal.DetectPending().Should().HaveCount(2);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~RecoveryJournalTests"
```
예상 실패: `error CS0246: The type or namespace name 'RecoveryJournal' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.App/Services/IRecoveryJournal.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace Memoria.App.Services;

/// 편집 중인 미저장 본문 스냅샷. 정상 저장 전 비정상 종료 대비용.
public sealed record RecoverySnapshot(int NoteId, string? Title, string? Body, DateTimeOffset CapturedAt);

public interface IRecoveryJournal
{
    /// recovery/{noteId}.json 에 스냅샷을 append(JSON Lines). 디바운스보다 빠르게 호출.
    void Append(RecoverySnapshot snapshot);

    /// 정상 저장 성공 시 해당 note의 저널 파일 삭제.
    void Clear(int noteId);

    /// 시작 시 보류 중인 복구 후보(노트별 최신 스냅샷) 목록.
    IReadOnlyList<RecoverySnapshot> DetectPending();
}
```

`src/Memoria.App/Services/RecoveryJournal.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Memoria.App.Services;

public sealed class RecoveryJournal : IRecoveryJournal
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };
    private readonly string _dir;

    public RecoveryJournal(string recoveryDirectory)
    {
        _dir = recoveryDirectory;
        Directory.CreateDirectory(_dir);
    }

    private string PathFor(int noteId) => Path.Combine(_dir, $"{noteId}.json");

    public void Append(RecoverySnapshot snapshot)
    {
        var line = JsonSerializer.Serialize(snapshot, JsonOpts);
        File.AppendAllText(PathFor(snapshot.NoteId), line + Environment.NewLine);
    }

    public void Clear(int noteId)
    {
        var path = PathFor(noteId);
        if (File.Exists(path)) File.Delete(path);
    }

    public IReadOnlyList<RecoverySnapshot> DetectPending()
    {
        if (!Directory.Exists(_dir)) return Array.Empty<RecoverySnapshot>();

        var result = new List<RecoverySnapshot>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            string? last = null;
            foreach (var line in File.ReadLines(file))
                if (!string.IsNullOrWhiteSpace(line)) last = line;

            if (last is null) continue;
            var snap = JsonSerializer.Deserialize<RecoverySnapshot>(last, JsonOpts);
            if (snap is not null) result.Add(snap);
        }
        return result;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~RecoveryJournalTests"
```
예상: `Passed!  - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" commit -m "feat(app): add crash recovery journal (JSON-lines, per-note latest snapshot)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: 디바운스 자동저장 서비스(IAutosaveService)
**Files:**
- Create: `src/Memoria.App/Services/IAutosaveService.cs`, `src/Memoria.App/Services/DebounceAutosaveService.cs`
- Test: `tests/Memoria.Tests/App/DebounceAutosaveServiceTests.cs`

**Interfaces:**
- Consumes: `System.TimeProvider`(BCL) — 결정적 테스트는 `Microsoft.Extensions.Time.Testing.FakeTimeProvider`.
- Produces: `Memoria.App.Services.IAutosaveService.{Register, Unregister, NotifyChanged, FlushAll}`, `Memoria.App.Services.DebounceAutosaveService(TimeProvider, int debounceMs)` — Task 6(에디터)/Task 9(DI 루트 등록·OnExit FlushAll)가 소비.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/DebounceAutosaveServiceTests.cs`:
```csharp
using System;
using FluentAssertions;
using Memoria.App.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class DebounceAutosaveServiceTests
{
    [Fact]
    public void Save_fires_only_after_debounce_window_elapses()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        var saves = 0;
        svc.Register(1, () => saves++);

        svc.NotifyChanged(1);
        time.Advance(TimeSpan.FromMilliseconds(300));
        saves.Should().Be(0);

        time.Advance(TimeSpan.FromMilliseconds(200));
        saves.Should().Be(1);
    }

    [Fact]
    public void Repeated_changes_reset_the_debounce_timer()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        var saves = 0;
        svc.Register(1, () => saves++);

        svc.NotifyChanged(1);
        time.Advance(TimeSpan.FromMilliseconds(300));
        svc.NotifyChanged(1);                       // 타이머 리셋
        time.Advance(TimeSpan.FromMilliseconds(300));
        saves.Should().Be(0);                       // 리셋 후 아직 500ms 미경과

        time.Advance(TimeSpan.FromMilliseconds(200));
        saves.Should().Be(1);
    }

    [Fact]
    public void FlushAll_runs_pending_save_immediately()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);
        var saves = 0;
        svc.Register(1, () => saves++);

        svc.NotifyChanged(1);
        svc.FlushAll();

        saves.Should().Be(1);
    }

    [Fact]
    public void NotifyChanged_without_registration_is_ignored()
    {
        var time = new FakeTimeProvider();
        var svc = new DebounceAutosaveService(time, 500);

        svc.NotifyChanged(99);
        time.Advance(TimeSpan.FromMilliseconds(1000));
        // 예외 없이 통과하면 성공.
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~DebounceAutosaveServiceTests"
```
예상 실패: `error CS0246: The type or namespace name 'DebounceAutosaveService' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.App/Services/IAutosaveService.cs`:
```csharp
using System;

namespace Memoria.App.Services;

public interface IAutosaveService
{
    /// 에디터에서 note를 열 때 저장 콜백 등록.
    void Register(int noteId, Action saveAction);

    /// note를 닫을 때 등록 해제(보류 저장 취소).
    void Unregister(int noteId);

    /// 콘텐츠 변경 알림. debounceMs 만큼 입력이 멈추면 등록된 save 콜백 1회 실행.
    void NotifyChanged(int noteId);

    /// 모든 보류 저장을 즉시 실행(창 종료/숨김/SessionEnding 시).
    void FlushAll();
}
```

`src/Memoria.App/Services/DebounceAutosaveService.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Threading;

namespace Memoria.App.Services;

public sealed class DebounceAutosaveService : IAutosaveService, IDisposable
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly Dictionary<int, Action> _saves = new();
    private readonly Dictionary<int, ITimer> _timers = new();
    private readonly HashSet<int> _pending = new();

    public DebounceAutosaveService(TimeProvider timeProvider, int debounceMs)
    {
        _time = timeProvider;
        _debounce = TimeSpan.FromMilliseconds(debounceMs);
    }

    public void Register(int noteId, Action saveAction)
    {
        lock (_gate) { _saves[noteId] = saveAction; }
    }

    public void Unregister(int noteId)
    {
        lock (_gate)
        {
            if (_timers.TryGetValue(noteId, out var t)) { t.Dispose(); _timers.Remove(noteId); }
            _saves.Remove(noteId);
            _pending.Remove(noteId);
        }
    }

    public void NotifyChanged(int noteId)
    {
        lock (_gate)
        {
            if (!_saves.ContainsKey(noteId)) return;
            _pending.Add(noteId);
            if (_timers.TryGetValue(noteId, out var existing)) existing.Dispose();
            _timers[noteId] = _time.CreateTimer(_ => Fire(noteId), null, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire(int noteId)
    {
        Action? action = null;
        lock (_gate)
        {
            if (_timers.TryGetValue(noteId, out var t)) { t.Dispose(); _timers.Remove(noteId); }
            if (_pending.Remove(noteId) && _saves.TryGetValue(noteId, out var a)) action = a;
        }
        action?.Invoke();
    }

    public void FlushAll()
    {
        var toRun = new List<Action>();
        lock (_gate)
        {
            foreach (var kv in _timers) kv.Value.Dispose();
            _timers.Clear();
            foreach (var id in _pending)
                if (_saves.TryGetValue(id, out var a)) toRun.Add(a);
            _pending.Clear();
        }
        foreach (var a in toRun) a();
    }

    public void Dispose() => FlushAll();
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~DebounceAutosaveServiceTests"
```
예상: `Passed!  - Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" commit -m "feat(app): add TimeProvider-based debounce autosave service

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: 사이드바 그룹 트리(MainViewModel.LoadGroups)
**Files:**
- Create: `src/Memoria.App/ViewModels/SidebarNodeViewModel.cs`, `src/Memoria.App/ViewModels/MainViewModel.cs`, `tests/Memoria.Tests/App/Fakes/FakeGroupRepository.cs`
- Test: `tests/Memoria.Tests/App/MainViewModelSidebarTests.cs`

**Interfaces:**
- Consumes: `Memoria.Core.Data.IGroupRepository.GetAll()` → `IReadOnlyList<Group>`(SortOrder 정렬, 시스템 그룹 포함); `Memoria.Core.Models.Group.{Id, Name, IsSystem, SortOrder}`.
- Produces: `Memoria.App.ViewModels.SidebarNodeKind{Group, Unclassified, System}`, `SidebarNodeViewModel(string Name, int? GroupId, SidebarNodeKind Kind)`, `MainViewModel.{SidebarNodes, SelectedNode, LoadGroups()}`.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/Fakes/FakeGroupRepository.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Tests.App.Fakes;

internal sealed class FakeGroupRepository : IGroupRepository
{
    public List<Group> Items { get; } = new();

    public int Create(Group group) { group.Id = Items.Count + 1; Items.Add(group); return group.Id; }
    public void Update(Group group) { }
    public void Delete(int id) => Items.RemoveAll(g => g.Id == id);
    public Group? Get(int id) => Items.FirstOrDefault(g => g.Id == id);
    public IReadOnlyList<Group> GetAll() => Items.OrderBy(g => g.SortOrder).ToList();
}
```

`tests/Memoria.Tests/App/MainViewModelSidebarTests.cs`:
```csharp
using System;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelSidebarTests
{
    [Fact]
    public void LoadGroups_orders_userGroups_then_unclassified_then_systemGroups()
    {
        var groups = new FakeGroupRepository();
        groups.Create(new Group { Name = "업무", IsSystem = false, SortOrder = 1 });
        groups.Create(new Group { Name = "개인", IsSystem = false, SortOrder = 2 });
        groups.Create(new Group { Name = "일일업무일지", IsSystem = true, SortOrder = 10 });
        groups.Create(new Group { Name = "주간보고", IsSystem = true, SortOrder = 11 });
        var vm = new MainViewModel(groups);

        vm.LoadGroups();

        vm.SidebarNodes.Select(n => n.Name).Should()
            .ContainInOrder("업무", "개인", "(미분류)", "일일업무일지", "주간보고");
        vm.SidebarNodes[2].Kind.Should().Be(SidebarNodeKind.Unclassified);
        vm.SidebarNodes[2].GroupId.Should().BeNull();
        vm.SidebarNodes[3].Kind.Should().Be(SidebarNodeKind.System);
    }
}
```
> `Select`/`ContainInOrder` 사용을 위해 파일 상단에 `using System.Linq;` 추가.

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelSidebarTests"
```
예상 실패: `error CS0246: The type or namespace name 'MainViewModel' / 'SidebarNodeViewModel' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.App/ViewModels/SidebarNodeViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Memoria.App.ViewModels;

public enum SidebarNodeKind { Group, Unclassified, System }

public sealed partial class SidebarNodeViewModel : ObservableObject
{
    public string Name { get; }
    public int? GroupId { get; }                 // null = (미분류) 가상 노드
    public SidebarNodeKind Kind { get; }

    public SidebarNodeViewModel(string name, int? groupId, SidebarNodeKind kind)
    {
        Name = name;
        GroupId = groupId;
        Kind = kind;
    }
}
```

`src/Memoria.App/ViewModels/MainViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Memoria.Core.Data;

namespace Memoria.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IGroupRepository _groupRepo;

    public ObservableCollection<SidebarNodeViewModel> SidebarNodes { get; } = new();

    [ObservableProperty]
    private SidebarNodeViewModel? selectedNode;

    public MainViewModel(IGroupRepository groupRepo)
    {
        _groupRepo = groupRepo;
    }

    public void LoadGroups()
    {
        SidebarNodes.Clear();
        var groups = _groupRepo.GetAll();

        foreach (var g in groups.Where(g => !g.IsSystem))
            SidebarNodes.Add(new SidebarNodeViewModel(g.Name, g.Id, SidebarNodeKind.Group));

        SidebarNodes.Add(new SidebarNodeViewModel("(미분류)", null, SidebarNodeKind.Unclassified));

        foreach (var g in groups.Where(g => g.IsSystem))
            SidebarNodes.Add(new SidebarNodeViewModel(g.Name, g.Id, SidebarNodeKind.System));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelSidebarTests"
```
예상: `Passed!  - Failed: 0, Passed: 1`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" commit -m "feat(app): build sidebar group tree with unclassified + system nodes

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: 메모 목록 로딩 + 제목 표시 규칙 + 새 일반 메모
**Files:**
- Create: `src/Memoria.App/ViewModels/NoteListItemViewModel.cs`, `src/Memoria.App/ViewModels/NoteTitleResolver.cs`, `tests/Memoria.Tests/App/Fakes/FakeNoteRepository.cs`
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs`
- Test: `tests/Memoria.Tests/App/MainViewModelNotesTests.cs`, `tests/Memoria.Tests/App/NoteTitleResolverTests.cs`

**Interfaces:**
- Consumes: `INoteRepository.{GetByGroup(int?), Create(Note)}`; `Note.{Id, GroupId, Type, Title, Body, Pinned, UpdatedAt, CreatedAt, DeletedAt}`; `NoteType.Plain`; `System.TimeProvider.GetUtcNow()`.
- Produces: `NoteTitleResolver.Resolve(Note) → string`; `NoteListItemViewModel(int Id, string DisplayTitle, bool Pinned, DateTimeOffset UpdatedAt)`; `MainViewModel.{Notes, LoadNotes(), NewPlainNoteCommand}` + 확장된 생성자.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/Fakes/FakeNoteRepository.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Tests.App.Fakes;

internal sealed class FakeNoteRepository : INoteRepository
{
    public List<Note> Items { get; } = new();
    public List<int> UpdatedIds { get; } = new();

    public int Create(Note note) { note.Id = Items.Count + 1; Items.Add(note); return note.Id; }

    public void Update(Note note)
    {
        var i = Items.FindIndex(n => n.Id == note.Id);
        if (i >= 0) Items[i] = note;
        UpdatedIds.Add(note.Id);
    }

    public void SoftDelete(int id) { }
    public void Restore(int id) { }
    public void Purge(int id) { }
    public void PurgeExpiredTrash(int retentionDays) { }
    public Note? Get(int id) => Items.FirstOrDefault(n => n.Id == id);

    public IReadOnlyList<Note> GetByGroup(int? groupId) =>
        Items.Where(n => n.DeletedAt == null && n.GroupId == groupId).ToList();

    public IReadOnlyList<Note> GetTrash() => Items.Where(n => n.DeletedAt != null).ToList();
    public IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday) => new List<Note>();
    public Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format) => null;
}
```

`tests/Memoria.Tests/App/NoteTitleResolverTests.cs`:
```csharp
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.App;

public class NoteTitleResolverTests
{
    [Fact]
    public void Uses_title_when_present()
    {
        var note = new Note { Title = "  명시 제목 ", Body = "첫 줄" };
        NoteTitleResolver.Resolve(note).Should().Be("명시 제목");
    }

    [Fact]
    public void Falls_back_to_first_nonempty_body_line_when_title_blank()
    {
        var note = new Note { Title = "   ", Body = "\n\n  본문 첫 줄\n둘째 줄" };
        NoteTitleResolver.Resolve(note).Should().Be("본문 첫 줄");
    }

    [Fact]
    public void Returns_placeholder_when_title_and_body_empty()
    {
        var note = new Note { Title = null, Body = "" };
        NoteTitleResolver.Resolve(note).Should().Be("(제목 없음)");
    }
}
```

`tests/Memoria.Tests/App/MainViewModelNotesTests.cs`:
```csharp
using System;
using System.Linq;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelNotesTests
{
    private static MainViewModel Build(FakeGroupRepository groups, FakeNoteRepository notes, TimeProvider time)
        => new MainViewModel(groups, notes, time);

    [Fact]
    public void Selecting_group_loads_notes_pinned_first_then_updated_desc()
    {
        var groups = new FakeGroupRepository();
        var gid = groups.Create(new Group { Name = "업무", SortOrder = 1 });
        var notes = new FakeNoteRepository();
        var t0 = DateTimeOffset.UnixEpoch;
        notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "오래됨", Pinned = false, UpdatedAt = t0 });
        notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "최신",   Pinned = false, UpdatedAt = t0.AddDays(2) });
        notes.Create(new Note { GroupId = gid, Type = NoteType.Plain, Title = "고정",   Pinned = true,  UpdatedAt = t0.AddDays(1) });
        var vm = Build(groups, notes, new FakeTimeProvider());
        vm.LoadGroups();

        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == gid);

        vm.Notes.Select(n => n.DisplayTitle).Should().ContainInOrder("고정", "최신", "오래됨");
    }

    [Fact]
    public void Unclassified_node_loads_notes_with_null_group()
    {
        var groups = new FakeGroupRepository();
        var notes = new FakeNoteRepository();
        notes.Create(new Note { GroupId = null, Type = NoteType.Plain, Title = "미분류 메모", UpdatedAt = DateTimeOffset.UnixEpoch });
        var vm = Build(groups, notes, new FakeTimeProvider());
        vm.LoadGroups();

        vm.SelectedNode = vm.SidebarNodes.First(n => n.Kind == SidebarNodeKind.Unclassified);

        vm.Notes.Should().ContainSingle().Which.DisplayTitle.Should().Be("미분류 메모");
    }

    [Fact]
    public void NewPlainNote_creates_plain_note_in_selected_group_and_reloads()
    {
        var groups = new FakeGroupRepository();
        var gid = groups.Create(new Group { Name = "업무", SortOrder = 1 });
        var notes = new FakeNoteRepository();
        var time = new FakeTimeProvider();
        var vm = Build(groups, notes, time);
        vm.LoadGroups();
        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == gid);

        vm.NewPlainNoteCommand.Execute(null);

        notes.Items.Should().ContainSingle();
        notes.Items[0].Type.Should().Be(NoteType.Plain);
        notes.Items[0].GroupId.Should().Be(gid);
        notes.Items[0].CreatedAt.Should().Be(time.GetUtcNow());
        vm.Notes.Should().ContainSingle();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelNotesTests|FullyQualifiedName~NoteTitleResolverTests"
```
예상 실패: `NoteTitleResolver` 미존재 + `MainViewModel` 생성자(2→3 인자) 불일치로 컴파일 실패(`error CS1729: 'MainViewModel' does not contain a constructor that takes 3 arguments`).

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.App/ViewModels/NoteTitleResolver.cs`:
```csharp
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

/// 제목 표시 규칙(§5.1): title이 비면 body 첫 비어있지 않은 줄을 표시용 제목으로.
public static class NoteTitleResolver
{
    public static string Resolve(Note note)
    {
        if (!string.IsNullOrWhiteSpace(note.Title))
            return note.Title!.Trim();

        if (!string.IsNullOrEmpty(note.Body))
        {
            foreach (var line in note.Body.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0) return trimmed;
            }
        }
        return "(제목 없음)";
    }
}
```

`src/Memoria.App/ViewModels/NoteListItemViewModel.cs`:
```csharp
using System;

namespace Memoria.App.ViewModels;

public sealed class NoteListItemViewModel
{
    public int Id { get; }
    public string DisplayTitle { get; }
    public bool Pinned { get; }
    public DateTimeOffset UpdatedAt { get; }

    public NoteListItemViewModel(int id, string displayTitle, bool pinned, DateTimeOffset updatedAt)
    {
        Id = id;
        DisplayTitle = displayTitle;
        Pinned = pinned;
        UpdatedAt = updatedAt;
    }
}
```

`MainViewModel.cs` 수정 — using/필드/생성자/멤버 추가:
```csharp
// 상단 using 추가:
using System;
using CommunityToolkit.Mvvm.Input;
using Memoria.Core.Models;
```
```csharp
// 필드 추가:
    private readonly INoteRepository _noteRepo;
    private readonly TimeProvider _time;

    public ObservableCollection<NoteListItemViewModel> Notes { get; } = new();
```
```csharp
// 생성자 교체(인자 확장):
    public MainViewModel(IGroupRepository groupRepo, INoteRepository noteRepo, TimeProvider time)
    {
        _groupRepo = groupRepo;
        _noteRepo = noteRepo;
        _time = time;
    }
```
```csharp
// 멤버 추가:
    partial void OnSelectedNodeChanged(SidebarNodeViewModel? value) => LoadNotes();

    public void LoadNotes()
    {
        Notes.Clear();
        if (SelectedNode is null) return;

        var notes = _noteRepo.GetByGroup(SelectedNode.GroupId)
            .OrderByDescending(n => n.Pinned)
            .ThenByDescending(n => n.UpdatedAt);

        foreach (var n in notes)
            Notes.Add(new NoteListItemViewModel(n.Id, NoteTitleResolver.Resolve(n), n.Pinned, n.UpdatedAt));
    }

    [RelayCommand]
    private void NewPlainNote()
    {
        var now = _time.GetUtcNow();
        var note = new Note
        {
            Type = NoteType.Plain,
            GroupId = SelectedNode?.GroupId,
            Title = null,
            Body = "",
            CreatedAt = now,
            UpdatedAt = now,
        };
        _noteRepo.Create(note);
        LoadNotes();
    }
```
> Task 4의 `MainViewModelSidebarTests`는 2-인자 생성자를 쓰므로, 해당 테스트의 `new MainViewModel(groups)` 호출을 `new MainViewModel(groups, new FakeNoteRepository(), new FakeTimeProvider())`로 갱신한다(테스트 상단에 `using Microsoft.Extensions.Time.Testing;` 추가).

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModel|FullyQualifiedName~NoteTitleResolverTests"
```
예상: `Passed!  - Failed: 0, Passed: 7` (사이드바 1 + 노트 3 + 제목 3).

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" commit -m "feat(app): load note list (pinned/updated sort), title display rule, new plain note

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: 일반 에디터 — 자동저장/복구 연동 + updated_at 규칙 + 헤더 + 복구 적용
**Files:**
- Create: `src/Memoria.App/ViewModels/EditorHeaderFormatter.cs`, `tests/Memoria.Tests/App/Fakes/FakeRecoveryJournal.cs`
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs`
- Test: `tests/Memoria.Tests/App/MainViewModelEditorTests.cs`, `tests/Memoria.Tests/App/EditorHeaderFormatterTests.cs`

**Interfaces:**
- Consumes: `INoteRepository.{Get(int), Update(Note)}`; `IAutosaveService.{Register, NotifyChanged, FlushAll}`(Task 3); `IRecoveryJournal.{Append, Clear, DetectPending}`(Task 2) + `RecoverySnapshot`; `TimeProvider.GetUtcNow()`.
- Produces: `EditorHeaderFormatter.Format(DateTimeOffset, DateTimeOffset) → string`; `MainViewModel.{EditorTitle, EditorBody, HeaderText, IsEditorVisible, OpenNote(int), ApplyRecovery(IReadOnlyList<RecoverySnapshot>)}` + 최종 생성자.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/Fakes/FakeRecoveryJournal.cs`:
```csharp
using System;
using System.Collections.Generic;
using Memoria.App.Services;

namespace Memoria.Tests.App.Fakes;

internal sealed class FakeRecoveryJournal : IRecoveryJournal
{
    public List<RecoverySnapshot> Appended { get; } = new();
    public List<int> Cleared { get; } = new();
    public List<RecoverySnapshot> Pending { get; } = new();

    public void Append(RecoverySnapshot snapshot) => Appended.Add(snapshot);
    public void Clear(int noteId) => Cleared.Add(noteId);
    public IReadOnlyList<RecoverySnapshot> DetectPending() => Pending;
}
```

`tests/Memoria.Tests/App/EditorHeaderFormatterTests.cs`:
```csharp
using System;
using FluentAssertions;
using Memoria.App.ViewModels;
using Xunit;

namespace Memoria.Tests.App;

public class EditorHeaderFormatterTests
{
    [Fact]
    public void Formats_created_and_updated_in_their_own_offset()
    {
        var created = new DateTimeOffset(2026, 6, 22, 14, 3, 0, TimeSpan.FromHours(9));
        var updated = new DateTimeOffset(2026, 6, 26, 9, 41, 0, TimeSpan.FromHours(9));

        EditorHeaderFormatter.Format(created, updated)
            .Should().Be("생성 2026-06-22 14:03 · 수정 2026-06-26 09:41");
    }
}
```

`tests/Memoria.Tests/App/MainViewModelEditorTests.cs`:
```csharp
using System;
using FluentAssertions;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelEditorTests
{
    private static (MainViewModel vm, FakeNoteRepository notes, FakeRecoveryJournal rec, FakeTimeProvider time, IAutosaveService autosave)
        Build(int debounceMs = 500)
    {
        var groups = new FakeGroupRepository();
        var notes = new FakeNoteRepository();
        var rec = new FakeRecoveryJournal();
        var time = new FakeTimeProvider();
        var autosave = new DebounceAutosaveService(time, debounceMs);
        var vm = new MainViewModel(groups, notes, autosave, rec, time);
        return (vm, notes, rec, time, autosave);
    }

    [Fact]
    public void OpenNote_loads_fields_and_header()
    {
        var (vm, notes, _, time, _) = Build();
        var created = new DateTimeOffset(2026, 6, 22, 14, 3, 0, TimeSpan.Zero);
        notes.Create(new Note { Type = NoteType.Plain, Title = "제목", Body = "본문", CreatedAt = created, UpdatedAt = created });

        vm.OpenNote(1);

        vm.EditorTitle.Should().Be("제목");
        vm.EditorBody.Should().Be("본문");
        vm.IsEditorVisible.Should().BeTrue();
        vm.HeaderText.Should().StartWith("생성 ");
    }

    [Fact]
    public void Editing_body_appends_recovery_and_autosaves_with_updated_at()
    {
        var (vm, notes, rec, time, _) = Build();
        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Title = null, Body = "old", CreatedAt = t0, UpdatedAt = t0 });
        vm.OpenNote(1);
        time.Advance(TimeSpan.FromMinutes(1));        // updated_at 이 created 와 달라지도록

        vm.EditorBody = "new content";

        rec.Appended.Should().ContainSingle();        // §8.1 복구 저널 append
        time.Advance(TimeSpan.FromMilliseconds(500)); // 디바운스 경과 → 저장

        notes.Items[0].Body.Should().Be("new content");
        notes.Items[0].UpdatedAt.Should().Be(time.GetUtcNow());  // §7.7 콘텐츠 변경 시 갱신
        rec.Cleared.Should().Contain(1);              // 정상 저장 후 저널 삭제
    }

    [Fact]
    public void Blank_title_is_saved_as_null()
    {
        var (vm, notes, _, time, _) = Build();
        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Title = "x", Body = "b", CreatedAt = t0, UpdatedAt = t0 });
        vm.OpenNote(1);

        vm.EditorTitle = "   ";
        time.Advance(TimeSpan.FromMilliseconds(500));

        notes.Items[0].Title.Should().BeNull();
    }

    [Fact]
    public void ApplyRecovery_writes_snapshot_back_and_clears_journal()
    {
        var (vm, notes, rec, time, _) = Build();
        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Title = null, Body = "stale", CreatedAt = t0, UpdatedAt = t0 });

        vm.ApplyRecovery(new[] { new RecoverySnapshot(1, null, "recovered body", t0.AddMinutes(5)) });

        notes.Items[0].Body.Should().Be("recovered body");
        rec.Cleared.Should().Contain(1);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelEditorTests|FullyQualifiedName~EditorHeaderFormatterTests"
```
예상 실패: `EditorHeaderFormatter` 미존재 + `MainViewModel` 생성자(3→5 인자) 불일치(`error CS1729`).

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.App/ViewModels/EditorHeaderFormatter.cs`:
```csharp
using System;

namespace Memoria.App.ViewModels;

/// 에디터 헤더(R5): "생성 yyyy-MM-dd HH:mm · 수정 yyyy-MM-dd HH:mm".
/// 전달된 DateTimeOffset의 자체 시각(offset)을 그대로 포맷한다(VM이 로컬로 변환 후 전달).
public static class EditorHeaderFormatter
{
    public static string Format(DateTimeOffset created, DateTimeOffset updated)
        => $"생성 {created:yyyy-MM-dd HH:mm} · 수정 {updated:yyyy-MM-dd HH:mm}";
}
```

`FakeNoteRepository`는 이미 `Update`를 구현(Task 5)했으므로 추가 변경 없음.

`MainViewModel.cs` 수정 — using/필드/생성자/멤버 추가:
```csharp
// 상단 using 추가:
using System.Collections.Generic;
using Memoria.App.Services;
```
```csharp
// 필드 추가:
    private readonly IAutosaveService _autosave;
    private readonly IRecoveryJournal _recovery;
    private Note? _current;
    private bool _suppressDirty;

    [ObservableProperty] private string editorTitle = "";
    [ObservableProperty] private string editorBody = "";
    [ObservableProperty] private string headerText = "";
    [ObservableProperty] private bool isEditorVisible;
```
```csharp
// 생성자 교체(최종 5-인자):
    public MainViewModel(
        IGroupRepository groupRepo,
        INoteRepository noteRepo,
        IAutosaveService autosave,
        IRecoveryJournal recovery,
        TimeProvider time)
    {
        _groupRepo = groupRepo;
        _noteRepo = noteRepo;
        _autosave = autosave;
        _recovery = recovery;
        _time = time;
    }
```
```csharp
// 멤버 추가:
    public void OpenNote(int noteId)
    {
        _autosave.FlushAll();                       // 이전 노트의 보류 저장 확정

        var note = _noteRepo.Get(noteId);
        if (note is null) return;

        _current = note;
        _suppressDirty = true;
        EditorTitle = note.Title ?? "";
        EditorBody = note.Body ?? "";
        _suppressDirty = false;

        HeaderText = EditorHeaderFormatter.Format(note.CreatedAt.ToLocalTime(), note.UpdatedAt.ToLocalTime());
        IsEditorVisible = true;
        _autosave.Register(noteId, () => SaveCurrent(noteId));
    }

    partial void OnEditorTitleChanged(string value) => OnContentChanged();
    partial void OnEditorBodyChanged(string value) => OnContentChanged();

    private void OnContentChanged()
    {
        if (_suppressDirty || _current is null) return;
        _recovery.Append(new RecoverySnapshot(_current.Id, EditorTitle, EditorBody, _time.GetUtcNow()));
        _autosave.NotifyChanged(_current.Id);
    }

    // 자동저장 콜백(백그라운드 스레드). 리포지토리/저널만 접근하고 ObservableCollection은 건드리지 않는다.
    private void SaveCurrent(int noteId)
    {
        var note = _noteRepo.Get(noteId);
        if (note is null) return;

        note.Title = string.IsNullOrWhiteSpace(EditorTitle) ? null : EditorTitle;
        note.Body = EditorBody;
        note.UpdatedAt = _time.GetUtcNow();         // §7.7 콘텐츠 변경 시에만 갱신
        _noteRepo.Update(note);
        _recovery.Clear(noteId);
    }

    // 시작 시 감지된 미저장 스냅샷을 DB에 반영(§8.1).
    public void ApplyRecovery(IReadOnlyList<RecoverySnapshot> snapshots)
    {
        foreach (var s in snapshots)
        {
            var note = _noteRepo.Get(s.NoteId);
            if (note is null) { _recovery.Clear(s.NoteId); continue; }

            note.Title = string.IsNullOrWhiteSpace(s.Title) ? null : s.Title;
            note.Body = s.Body;
            note.UpdatedAt = _time.GetUtcNow();
            _noteRepo.Update(note);
            _recovery.Clear(s.NoteId);
        }
    }
```
> Task 5의 `MainViewModelNotesTests`와 Task 4의 (갱신된) 사이드바 테스트는 3-인자 생성자를 쓰므로, 두 테스트 파일의 `new MainViewModel(...)` 호출을 최종 5-인자 형태로 갱신한다. 헬퍼로 묶으면 편하다:
> ```csharp
> // 두 테스트 클래스에 공통 헬퍼 추가
> private static MainViewModel NewVm(FakeGroupRepository g, FakeNoteRepository n) =>
>     new MainViewModel(g, n,
>         new Memoria.App.Services.DebounceAutosaveService(new FakeTimeProvider(), 500),
>         new FakeRecoveryJournal(),
>         new FakeTimeProvider());
> ```
> (`NewPlainNote` 시각 검증 테스트처럼 `time`을 직접 단언하는 케이스는 동일한 `FakeTimeProvider` 인스턴스를 autosave와 vm에 공유하도록 인라인 구성한다.)

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests"
```
예상: 전체 통과 — `Passed!  - Failed: 0` (Task 1~6 누적: AppPaths 2 + Recovery 3 + Autosave 4 + Sidebar 1 + Notes 3 + Title 3 + Editor 4 + Header 1).

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" commit -m "feat(app): plain editor with debounce autosave, recovery journal, updated_at rule, header

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: MainViewModel 스텁 명령 + M9 호스팅용 멤버 노출
**Files:**
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs`
- Test: `tests/Memoria.Tests/App/MainViewModelStubCommandTests.cs`

**Interfaces:**
- Consumes: `Memoria.Core.Data.SearchHit`(계약 §4 — 이미 `Memoria.Core.Data` 참조 중); `Memoria.Core.Models.NoteType`(Task 5에서 using 추가됨).
- Produces (계약 §9.3 — 이후 M6/M7/M9가 Consumes):
  - `MainViewModel.NewPlainNoteCommand` (Task 5에서 이미 Produces — **유지**, `NewNoteCommand` 아님).
  - 스텁 명령(모두 `[RelayCommand]`, 접미사 `Command`): `NewChecklistCommand`/`OpenWeeklyReportCommand`/`OpenSettingsCommand`/`SearchCommand`/`OpenSearchHitCommand(SearchHit)`.
  - 멤버: `string SearchText`, `ObservableCollection<SearchHit> SearchResults`, `NoteListItemViewModel? SelectedNote`, `NoteType CurrentNoteType`.
  - 본문은 M7(설정)/M9(체크리스트·주간보고·검색)에서 채운다. **M2는 예외를 던지지 않는 안전한 no-op 스텁**(빈 본문/`NotImplementedException` 금지).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/MainViewModelStubCommandTests.cs`:
```csharp
using System;
using FluentAssertions;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelStubCommandTests
{
    private static MainViewModel NewVm(FakeNoteRepository? notes = null, FakeTimeProvider? time = null)
    {
        time ??= new FakeTimeProvider();
        return new MainViewModel(
            new FakeGroupRepository(),
            notes ?? new FakeNoteRepository(),
            new DebounceAutosaveService(time, 500),
            new FakeRecoveryJournal(),
            time);
    }

    [Fact]
    public void Stub_commands_execute_without_throwing()
    {
        var vm = NewVm();

        vm.NewChecklistCommand.Execute(null);
        vm.OpenWeeklyReportCommand.Execute(null);
        vm.OpenSettingsCommand.Execute(null);
        vm.SearchCommand.Execute(null);
        vm.OpenSearchHitCommand.Execute(new SearchHit(1, "title", "snippet"));

        vm.SearchResults.Should().BeEmpty();
        vm.SearchText.Should().BeEmpty();
    }

    [Fact]
    public void OpenNote_sets_CurrentNoteType()
    {
        var notes = new FakeNoteRepository();
        var time = new FakeTimeProvider();
        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Title = "t", Body = "b", CreatedAt = t0, UpdatedAt = t0 });
        var vm = NewVm(notes, time);

        vm.OpenNote(1);

        vm.CurrentNoteType.Should().Be(NoteType.Plain);
    }

    [Fact]
    public void Setting_SelectedNote_opens_that_note()
    {
        var notes = new FakeNoteRepository();
        var time = new FakeTimeProvider();
        var t0 = time.GetUtcNow();
        notes.Create(new Note { Type = NoteType.Plain, Title = "sel", Body = "body", CreatedAt = t0, UpdatedAt = t0 });
        var vm = NewVm(notes, time);

        vm.SelectedNote = new NoteListItemViewModel(1, "sel", false, t0);

        vm.IsEditorVisible.Should().BeTrue();
        vm.EditorBody.Should().Be("body");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelStubCommandTests"
```
예상 실패: `error CS1061`/`CS0246` — `NewChecklistCommand`/`OpenWeeklyReportCommand`/`OpenSettingsCommand`/`SearchCommand`/`OpenSearchHitCommand`/`SearchResults`/`SearchText`/`SelectedNote`/`CurrentNoteType` 미존재로 컴파일 실패.

- [ ] **Step 3: Write minimal implementation**

`MainViewModel.cs` 수정 — Task 7의 멤버를 추가한다(상단 using은 Task 5/6에서 `Memoria.Core.Data`/`Memoria.Core.Models`/`CommunityToolkit.Mvvm.Input`이 이미 존재하므로 추가 불필요):
```csharp
// 멤버 추가(스텁 명령 + M9 뷰 호스팅/검색용 노출):
    [ObservableProperty] private NoteListItemViewModel? selectedNote;   // 계약 §9.3
    [ObservableProperty] private NoteType currentNoteType;              // 현재 편집 중 NoteType(M9 뷰 호스팅용)
    [ObservableProperty] private string searchText = "";

    public ObservableCollection<SearchHit> SearchResults { get; } = new();

    partial void OnSelectedNoteChanged(NoteListItemViewModel? value)
    {
        if (value is not null) OpenNote(value.Id);
    }

    // --- 스텁 명령(안전한 no-op). 본문은 이후 마일스톤에서 채운다. ---
    [RelayCommand] private void NewChecklist() { /* M9: 체크리스트 노트 생성 + 호스팅 */ }
    [RelayCommand] private void OpenWeeklyReport() { /* M9: 주간보고 뷰 호스팅 */ }
    [RelayCommand] private void OpenSettings() { /* M7: 설정 창(이전엔 M6 트레이 메뉴가 안전하게 Consumes) */ }
    [RelayCommand] private void Search() { /* M9: SearchText로 ISearchService 조회 → SearchResults 채움 */ }
    [RelayCommand] private void OpenSearchHit(SearchHit hit) { /* M9: hit.NoteId 열기 */ }
```
그리고 Task 6의 `OpenNote`에 한 줄을 추가해 `CurrentNoteType`를 노출한다(`_current = note;` 직후):
```csharp
        _current = note;
        CurrentNoteType = note.Type;   // ← 추가: M9 뷰 호스팅용
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~MainViewModelStubCommandTests"
```
예상: `Passed!  - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" commit -m "feat(app): add MainViewModel stub commands + SelectedNote/CurrentNoteType for later milestones

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: App 서비스 로케이터(AppServices, 계약 §9.2)
**Files:**
- Create: `src/Memoria.App/AppServices.cs`
- Modify: `src/Memoria.App/Memoria.App.csproj` (InternalsVisibleTo)
- Test: `tests/Memoria.Tests/App/AppServicesTests.cs`

**Interfaces:**
- Consumes: `Microsoft.Extensions.DependencyInjection.GetRequiredService<T>()`(BCL/DI).
- Produces (계약 §9.2): `Memoria.App.AppServices.{Provider, Resolve<T>(), Initialize(IServiceProvider)}` — Task 9 부트스트랩이 `Initialize`를 호출하고, 이후 마일스톤의 View/code-behind가 `Resolve<T>()`로 소비.

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/App/AppServicesTests.cs`:
```csharp
using System;
using FluentAssertions;
using Memoria.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Memoria.Tests.App;

public class AppServicesTests
{
    [Fact]
    public void Resolve_returns_service_from_initialized_provider()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton("hello");
        var provider = sc.BuildServiceProvider();

        AppServices.Initialize(provider);

        AppServices.Provider.Should().BeSameAs(provider);
        AppServices.Resolve<string>().Should().Be("hello");
    }
}
```
> `Initialize`는 `internal`(계약 §9.2)이므로 `Memoria.App`이 `InternalsVisibleTo("Memoria.Tests")`를 노출해야 컴파일된다.

- [ ] **Step 2: Run test to verify it fails**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~AppServicesTests"
```
예상 실패: `error CS0103: The name 'AppServices' does not exist` 또는 `Initialize` 접근 불가로 빌드 실패.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.App/AppServices.cs`:
```csharp
using System;
using Microsoft.Extensions.DependencyInjection;

namespace Memoria.App;

/// App 전역 서비스 로케이터(계약 §9.2). 컴포지션 루트(App.xaml.cs)가 Initialize 하고,
/// View code-behind/부트스트랩에서만 Resolve<T>()로 사용한다(ViewModel은 생성자 주입 유지).
public static class AppServices
{
    private static IServiceProvider? _provider;

    public static IServiceProvider Provider =>
        _provider ?? throw new InvalidOperationException("AppServices.Initialize가 먼저 호출되어야 합니다.");

    public static T Resolve<T>() where T : notnull => Provider.GetRequiredService<T>();

    internal static void Initialize(IServiceProvider provider) => _provider = provider;
}
```

`src/Memoria.App/Memoria.App.csproj`에 테스트가 `internal Initialize`를 호출할 수 있도록 추가:
```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Memoria.Tests" />
  </ItemGroup>
```

- [ ] **Step 4: Run test to verify it passes**
```bash
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~AppServicesTests"
```
예상: `Passed!  - Failed: 0, Passed: 1`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" commit -m "feat(app): add AppServices static locator (contract 9.2)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: DI 컴포지션 루트(App.xaml.cs) + MainWindow + 시작 복구 배선 (수동 검증)
**Files:**
- Modify: `src/Memoria.App/App.xaml`, `src/Memoria.App/App.xaml.cs`, `src/Memoria.App/MainWindow.xaml`, `src/Memoria.App/MainWindow.xaml.cs`
- Test: (자동 테스트 없음 — UI 통합/창/렌더는 수동 검증. 로직은 Task 1~8에서 자동 테스트 완료.)

**Interfaces:**
- Consumes: `IServiceCollection.AddMemoriaCore(string)`(M1 가정), `IDatabaseInitializer.EnsureReady()`, `ISettingsRepository.GetOrDefault(string,string)`, `SettingsKeys.AutosaveDebounceMs`; `AppServices.Initialize(IServiceProvider)`(Task 8); Task 2/3/6/7의 App 타입; `MainViewModel.{LoadGroups, ApplyRecovery, SelectedNote, NewPlainNoteCommand}`(Task 4~7).
- Produces: 계약 §9.4 부트스트랩 순서의 **기반(누적 패치 대상)** — `App.xaml.cs`(`OnStartup` 1~11 / `OnExit`), 실행 가능한 WPF 셸(MainWindow + `ViewModel` 접근자), 시작 시 복구 적용, 종료 시 `FlushAll` + `wal_checkpoint(TRUNCATE)`. 이후 **M5/M6/M7/M9는 기존 호출을 보존한 채 표시된 위치에 자기 배선만 '추가'한다**.

- [ ] **Step 1: 화면/배선 구현(App.xaml / App.xaml.cs)**

`src/Memoria.App/App.xaml` (StartupUri 제거, 기본 브러시 키만 — 색은 전부 DynamicResource로 참조):
```xml
<Application x:Class="Memoria.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <!-- 계약 §10 테마 브러시 키(단일 진리원천). M7 테마가 이 사전을 교체.
             M2는 light 기본값만 둔다. 모든 View는 이 Brush.* 키만 DynamicResource로 참조(StaticResource 금지). -->
        <SolidColorBrush x:Key="Brush.WindowBackground" Color="#FFFFFFFF" />
        <SolidColorBrush x:Key="Brush.Surface" Color="#FFFAFAFA" />
        <SolidColorBrush x:Key="Brush.SidebarBackground" Color="#FFF3F3F3" />
        <SolidColorBrush x:Key="Brush.ToolbarBackground" Color="#FFF7F7F7" />
        <SolidColorBrush x:Key="Brush.EditorBackground" Color="#FFFFFFFF" />
        <SolidColorBrush x:Key="Brush.Foreground" Color="#FF1B1B1B" />
        <SolidColorBrush x:Key="Brush.SecondaryForeground" Color="#FF6B6B6B" />
        <SolidColorBrush x:Key="Brush.Border" Color="#FFDDDDDD" />
        <SolidColorBrush x:Key="Brush.Accent" Color="#FF2D7D9A" />
        <SolidColorBrush x:Key="Brush.AccentForeground" Color="#FFFFFFFF" />
    </Application.Resources>
</Application>
```

`src/Memoria.App/App.xaml.cs`:
```csharp
using System;
using System.Windows;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.Core;
using Memoria.Core.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Memoria.App;

/// 컴포지션 루트 + 계약 §9.4 부트스트랩 순서의 '기반'(누적 패치 대상).
/// 각 마일스톤(M5/M6/M7/M9)은 기존 호출을 보존하고, 표시된 위치에 자기 배선만 '추가'한다.
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // (1) M2 — 데이터 디렉터리 보장.
        AppPaths.EnsureDirectories();

        // (2) M6 — SingleInstance: 두 번째 인스턴스면 pipe로 인자 전송 후 Shutdown. (M6에서 추가)

        // (3) M2 — DI 합성 + 서비스 로케이터 초기화. (M6/M7이 자기 서비스 등록을 이 블록에 추가)
        var sc = new ServiceCollection();
        sc.AddMemoriaCore(AppPaths.DatabaseFile);   // M1 Core: 초기화/리포지토리/서비스 + 단일 직렬 라이터(busy_timeout=5000)
        sc.AddSingleton<TimeProvider>(TimeProvider.System);
        sc.AddSingleton<IRecoveryJournal>(_ => new RecoveryJournal(AppPaths.RecoveryDirectory));
        sc.AddSingleton<IAutosaveService>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsRepository>();
            var ms = int.Parse(settings.GetOrDefault(SettingsKeys.AutosaveDebounceMs, "500"));
            return new DebounceAutosaveService(sp.GetRequiredService<TimeProvider>(), ms);
        });
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<MainWindow>();
        _services = sc.BuildServiceProvider();
        AppServices.Initialize(_services);          // 계약 §9.2 — 이후 View/code-behind가 AppServices.Resolve<T>() 사용

        // (4) M2 — DB 준비(파일/PRAGMA/마이그레이션/시드).
        _services.GetRequiredService<IDatabaseInitializer>().EnsureReady();

        // (5) M9 — 무결성 점검 실패 시 최신 백업 복원(+사용자 확인):
        //     if (!_services.GetRequiredService<IBackupService>().IsDatabaseHealthy())
        //         _services.GetRequiredService<IBackupService>().TryRestoreFromLatestBackup();   (M9에서 추가)
        // (6) M9 — 일일 백업:
        //     _services.GetRequiredService<IBackupService>().BackupIfDue(retentionCount);        (M9에서 추가)
        // (7) M7 — _services.GetRequiredService<IThemeService>().Initialize();                   (M7에서 추가)

        var vm = _services.GetRequiredService<MainViewModel>();
        vm.LoadGroups();

        // (8) M2 — 크래시 복구 저널 적용(§8.1): 보류 스냅샷이 있으면 사용자 확인 후 DB에 반영.
        var recovery = _services.GetRequiredService<IRecoveryJournal>();
        var pending = recovery.DetectPending();
        if (pending.Count > 0)
        {
            var answer = MessageBox.Show(
                $"비정상 종료로 저장되지 않은 메모 {pending.Count}건이 있습니다. 복구하시겠습니까?",
                "Memoria 복구", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) vm.ApplyRecovery(pending);
            else foreach (var s in pending) recovery.Clear(s.NoteId);
        }

        // (9) M5 — _services.GetRequiredService<INoteRepository>().PurgeExpiredTrash(trashRetentionDays); (M5에서 추가)

        // (10) M2 — MainWindow 생성/표시. (M6 Tray/Hotkey, M7 SystemThemeSource 구독을 이 위치에 추가)
        var window = _services.GetRequiredService<MainWindow>();
        window.DataContext = vm;
        MainWindow = window;

        // (11) M2 — 표시. (M6에서 closeToTray/autostart 정책에 따라 트레이 시작으로 분기)
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 계약 §9.4 OnExit
        _services?.GetService<IAutosaveService>()?.FlushAll();   // (M2) 보류 저장 즉시 확정(§7.7)
        _services?.Dispose();                                    // (M2/M9) SqliteConnectionFactory.Dispose가 PRAGMA wal_checkpoint(TRUNCATE) 후 연결 종료
        // (M6) Tray/Hotkey/Pipe Dispose는 M6에서 추가
        base.OnExit(e);
    }
}
```
> `<StartupUri>` 가 `App.xaml`에 없어야 한다(위 XAML에서 제거됨). `dotnet new wpf` 기본 `App.xaml`에 남아 있으면 삭제.
> 종료 시 `wal_checkpoint(TRUNCATE)`는 M1의 `SqliteConnectionFactory.Dispose()`가 단일 쓰기 연결을 닫기 직전 수행한다(계약 §8). App은 `FlushAll()` 후 `_services.Dispose()`로 이를 트리거한다. M9가 명시적 checkpoint API를 노출하면 (10)/(11) 사이 또는 OnExit에 그 호출을 추가한다(기존 호출 보존).

- [ ] **Step 2: MainWindow 구현(얇은 code-behind)**

`src/Memoria.App/MainWindow.xaml`:
```xml
<Window x:Class="Memoria.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Memoria" Height="640" Width="980"
        Background="{DynamicResource Brush.WindowBackground}"
        Foreground="{DynamicResource Brush.Foreground}">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- 상단 툴바 (M9가 [+ 체크리스트]/[📋 주간보고]/검색창을 추가) -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="8"
                    Background="{DynamicResource Brush.ToolbarBackground}">
            <Button Content="+ 새 메모" Command="{Binding NewPlainNoteCommand}" Padding="10,4" />
        </StackPanel>

        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="220" />
                <ColumnDefinition Width="240" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <!-- 사이드바: 그룹 트리 -->
            <ListBox Grid.Column="0"
                     Background="{DynamicResource Brush.SidebarBackground}"
                     ItemsSource="{Binding SidebarNodes}"
                     SelectedItem="{Binding SelectedNode, Mode=TwoWay}"
                     DisplayMemberPath="Name" />

            <!-- 메모 목록 (SelectedNote 바인딩 → VM.OnSelectedNoteChanged가 OpenNote 호출) -->
            <ListBox Grid.Column="1"
                     Background="{DynamicResource Brush.Surface}"
                     ItemsSource="{Binding Notes}"
                     SelectedItem="{Binding SelectedNote, Mode=TwoWay}"
                     DisplayMemberPath="DisplayTitle" />

            <!-- 에디터 (M9가 NoteType별 View 호스팅 ContentControl로 교체) -->
            <Grid Grid.Column="2" Margin="12"
                  Background="{DynamicResource Brush.EditorBackground}"
                  Visibility="{Binding IsEditorVisible, Converter={x:Static System.Windows.Controls.BooleanToVisibilityConverter+Default}, FallbackValue=Collapsed}">
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
        </Grid>
    </Grid>
</Window>
```
> 색/브러시는 계약 §10 `Brush.*` 키만 `DynamicResource`로 사용한다(StaticResource 금지). `BooleanToVisibilityConverter`를 `x:Static`로 직접 쓰기 어렵다면 `App.xaml`에 `<BooleanToVisibilityConverter x:Key="BoolToVis"/>`를 등록하고 `Converter={StaticResource BoolToVis}`로 바꾼다(Converter 자체는 색이 아니므로 StaticResource 허용).

`src/Memoria.App/MainWindow.xaml.cs` (얇은 code-behind. 메모 선택→열기는 `SelectedNote` 바인딩이 처리하므로 SelectionChanged 핸들러 불필요):
```csharp
using System.Windows;
using Memoria.App.ViewModels;

namespace Memoria.App;

public partial class MainWindow : Window
{
    /// 계약 §9.3 — code-behind/이후 마일스톤이 ViewModel에 접근.
    public MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: 빌드 검증**
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\Memoria.sln"
```
예상: `Build succeeded. 0 Error(s)`. (M1의 `AddMemoriaCore`가 없으면 여기서 컴파일 에러 → Global Constraints의 "M1 의존 가정"대로 각 interface→concrete를 개별 등록으로 대체.)

- [ ] **Step 4: 수동 검증 체크포인트(Windows 실제 실행)**

앱 실행:
```bash
dotnet.exe run --project "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App"
```
다음을 눈으로 확인한다:
- [ ] **MV-1 창 표시**: MainWindow가 뜨고 좌측 사이드바 / 가운데 목록 / 우측 에디터 3분할이 보인다.
- [ ] **MV-2 그룹 트리 순서**: 사이드바에 (사용자 그룹들) → `(미분류)` → `일일업무일지` → `주간보고` 순으로 표시된다(시스템 그룹은 시드되어 있어야 함).
- [ ] **MV-3 새 메모**: `+ 새 메모` 클릭 시 목록에 새 항목(`(제목 없음)`)이 추가되고, 클릭하면 우측 에디터가 열린다.
- [ ] **MV-4 제목 규칙(§5.1)**: 제목을 비운 채 본문 첫 줄을 입력하면, (목록을 다른 그룹으로 갔다 오거나 재시작 후) 목록 표시 제목이 본문 첫 줄로 나온다.
- [ ] **MV-5 헤더(R5)**: 에디터 상단에 `생성 … · 수정 …` 형식의 생성/수정 시각이 보인다.
- [ ] **MV-6 자동저장**: 본문을 입력하고 ~0.5초 멈춘 뒤, 앱을 정상 종료했다가 다시 실행하면 입력 내용이 유지된다(`%LOCALAPPDATA%\Memoria\memoria.db`에 영속).
- [ ] **MV-7 updated_at 갱신**: 본문 편집 후 헤더의 "수정" 시각이 갱신된다(편집 전 대비 변경).
- [ ] **MV-8 크래시 복구(§8.1)**: 본문을 입력하고 0.5초 이내(저장 전)에 작업 관리자로 프로세스를 강제 종료 → 재실행 시 "저장되지 않은 메모 N건 … 복구하시겠습니까?" 다이얼로그가 뜨고, `예` 선택 시 입력 내용이 복원된다. `recovery\{noteId}.json`은 정상 저장/복구 후 삭제되어 있다.

위 8개 체크포인트가 모두 통과하면 M2 완료.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" add -A
git -C "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled" commit -m "feat(app): DI composition root, MainWindow shell, startup recovery wiring

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 완료 기준 (Definition of Done)
- `dotnet.exe build "...\Memoria.sln"` 성공(0 Error).
- `dotnet.exe test "...\tests\Memoria.Tests"` 전체 통과(Task 1~8의 자동 테스트).
- Task 9의 수동 검증 체크포인트 MV-1~MV-8 모두 통과.
- Produces 산출물 확정(계약 §9): App DI 루트(`App.xaml.cs` — §9.4 누적 부트스트랩 기반), `AppServices`(§9.2), `MainWindow.ViewModel`(§9.3), `MainViewModel`(NewPlainNoteCommand + §9.3 스텁 명령/SelectedNote/CurrentNoteType), `IAutosaveService`, `IRecoveryJournal`. 모든 View 색상은 §10 `Brush.*` 키만 사용.

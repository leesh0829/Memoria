# M5 — Group CRUD + Trash Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 사이드바 그룹 CRUD(추가/이름변경/색상/드래그 순서변경/삭제, 시스템 그룹 보호, 삭제 시 메모를 미분류로)와 메모 휴지통(SoftDelete/Restore/Purge, Undo 토스트, 보존기간 만료 자동 영구삭제, 그룹 이동 시 updated_at 미갱신)을 ViewModel 로직으로 분리해 xUnit으로 검증하고 WPF UI로 노출한다.

**Architecture:** M1이 제공한 `IGroupRepository`/`INoteRepository`/`ISettingsRepository` 위에 `GroupManagementViewModel`과 `TrashViewModel`(+`TrashItemViewModel`)을 얹는다. 모든 비즈니스 로직(시스템 그룹 보호 / 순서 재배치 / Undo 상태 / 만료일 계산)은 `CommunityToolkit.Mvvm`만 의존하는 VM에 두어 `net9.0-windows` 테스트 프로젝트에서 in-memory 페이크 리포지토리로 자동 검증한다. XAML/code-behind는 얇게 유지하고 드래그·토스트 등 시각/전역 동작만 수동 검증한다.

**Tech Stack:** C# / .NET 9, WPF(`net9.0-windows`), `CommunityToolkit.Mvvm`(ObservableObject·ObservableProperty·RelayCommand), `Microsoft.Extensions.DependencyInjection`, xUnit + FluentAssertions, `System.TimeProvider`(BCL).

## Global Constraints
- .NET 9.
- TFM: `Memoria.Core` = `net9.0`, `Memoria.App` = `net9.0-windows`, `Memoria.Tests` = `net9.0-windows`.
- DB 위치는 `%LOCALAPPDATA%\Memoria` (M1 책임; M5는 리포지토리 인터페이스만 소비).
- WPF는 `PublishTrimmed` 절대 금지, `EnableCompressionInSingleFile` 미사용.
- 빌드/테스트는 Windows .NET 9 SDK(`dotnet.exe`) + Windows 절대경로로만 수행(WPF는 Linux dotnet 불가).
- ViewModel은 `CommunityToolkit.Mvvm`만 의존하고 WPF 타입을 참조하지 않는다. code-behind는 얇게 유지.
- 모든 색/브러시는 XAML에서 `DynamicResource`만 사용(StaticResource 금지).
- 시스템 그룹(`is_system=1`: 일일업무일지/주간보고)은 **삭제·이름변경 불가**(색상 변경은 허용).
- 그룹 삭제 시 `notes.group_id`는 `ON DELETE SET NULL` — 메모는 삭제되지 않고 (미분류)로 이동(M1/DB 책임, M5는 `IGroupRepository.Delete` 호출).
- 메모 그룹 이동은 메타 조작이므로 **`updated_at`을 갱신하지 않는다**(§7.7) — VM은 `Note`의 `UpdatedAt`을 건드리지 않고 `INoteRepository.Update`에 그대로 전달.
- 휴지통 보존기간은 `settings`의 `trash.retentionDays`(기본 30)를 따르고, `INoteRepository.PurgeExpiredTrash(retentionDays)`를 **앱 시작 시 호출**한다.
- 메모 삭제는 소프트 삭제(`deleted_at`)이며 삭제 직후 Undo를 제공한다. 영구삭제 시 `checklist_items`는 CASCADE(M1/DB 책임).

---

### Task 1: Test fakes + `GroupManagementViewModel` 로드

**Files:**
- Create: `src/Memoria.App/ViewModels/GroupManagementViewModel.cs`
- Test: `tests/Memoria.Tests/Fakes/FakeGroupRepository.cs`, `tests/Memoria.Tests/Fakes/FakeNoteRepository.cs`, `tests/Memoria.Tests/Fakes/FakeSettingsRepository.cs`, `tests/Memoria.Tests/Fakes/FixedTimeProvider.cs`, `tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs`

**Interfaces:**
- Consumes: `Memoria.Core.Data.IGroupRepository.GetAll() : IReadOnlyList<Group>`(SortOrder 정렬), `Memoria.Core.Data.INoteRepository`, `Memoria.Core.Models.Group`.
- Produces: `Memoria.App.ViewModels.GroupManagementViewModel`(ctor `(IGroupRepository, INoteRepository)`, `ObservableCollection<Group> Groups`, `void Load()`, `Group? SelectedGroup`).

- [ ] **Step 1: Write the failing test**

테스트 페이크를 먼저 만든다. (이후 모든 Task가 재사용한다.)

`tests/Memoria.Tests/Fakes/FakeGroupRepository.cs`:
```csharp
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Tests.Fakes;

public sealed class FakeGroupRepository : IGroupRepository
{
    public readonly List<Group> Items = new();
    private int _nextId = 1;

    public int Create(Group group)
    {
        group.Id = _nextId++;
        Items.Add(group);
        return group.Id;
    }

    public void Update(Group group)
    {
        var i = Items.FindIndex(g => g.Id == group.Id);
        if (i >= 0) Items[i] = group;
    }

    public void Delete(int id) => Items.RemoveAll(g => g.Id == id);

    public Group? Get(int id) => Items.FirstOrDefault(g => g.Id == id);

    public IReadOnlyList<Group> GetAll() => Items.OrderBy(g => g.SortOrder).ToList();
}
```

`tests/Memoria.Tests/Fakes/FakeNoteRepository.cs`:
```csharp
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Tests.Fakes;

public sealed class FakeNoteRepository : INoteRepository
{
    public readonly List<Note> Items = new();
    private int _nextId = 1;
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    public int Create(Note note)
    {
        note.Id = _nextId++;
        var now = Clock.GetUtcNow();
        note.CreatedAt = now;
        note.UpdatedAt = now;
        Items.Add(note);
        return note.Id;
    }

    public void Update(Note note)
    {
        var i = Items.FindIndex(n => n.Id == note.Id);
        if (i >= 0) Items[i] = note; // 전달된 Note 그대로 저장(updated_at 갱신은 호출자 정책)
    }

    public void SoftDelete(int id)
    {
        var n = Items.FirstOrDefault(x => x.Id == id);
        if (n != null) n.DeletedAt = Clock.GetUtcNow();
    }

    public void Restore(int id)
    {
        var n = Items.FirstOrDefault(x => x.Id == id);
        if (n != null) n.DeletedAt = null;
    }

    public void Purge(int id) => Items.RemoveAll(n => n.Id == id);

    public void PurgeExpiredTrash(int retentionDays)
    {
        var cutoff = Clock.GetUtcNow().AddDays(-retentionDays);
        Items.RemoveAll(n => n.DeletedAt is { } d && d <= cutoff);
    }

    public Note? Get(int id) => Items.FirstOrDefault(n => n.Id == id);

    public IReadOnlyList<Note> GetByGroup(int? groupId) =>
        Items.Where(n => n.DeletedAt == null && n.GroupId == groupId).ToList();

    public IReadOnlyList<Note> GetTrash() =>
        Items.Where(n => n.DeletedAt != null).ToList();

    public IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday) =>
        Items.Where(n => n.DeletedAt == null && n.Type == NoteType.Checklist
                         && n.LogDate is { } d && d >= monday && d <= friday).ToList();

    public Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format) =>
        Items.FirstOrDefault(n => n.Type == NoteType.WeeklyReport
                                  && n.ReportWeekStart == weekStart && n.ReportFormat == format);
}
```

`tests/Memoria.Tests/Fakes/FakeSettingsRepository.cs`:
```csharp
using Memoria.Core.Data;

namespace Memoria.Tests.Fakes;

public sealed class FakeSettingsRepository : ISettingsRepository
{
    public readonly Dictionary<string, string> Store = new();

    public string? Get(string key) => Store.TryGetValue(key, out var v) ? v : null;

    public string GetOrDefault(string key, string fallback) =>
        Store.TryGetValue(key, out var v) ? v : fallback;

    public void Set(string key, string value) => Store[key] = value;

    public IReadOnlyDictionary<string, string> GetAll() => Store;
}
```

`tests/Memoria.Tests/Fakes/FixedTimeProvider.cs`:
```csharp
namespace Memoria.Tests.Fakes;

public sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _now;
    public FixedTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
```

`tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs`:
```csharp
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.Fakes;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class GroupManagementViewModelTests
{
    private static (GroupManagementViewModel vm, FakeGroupRepository groups, FakeNoteRepository notes) CreateSut()
    {
        var groups = new FakeGroupRepository();
        var notes = new FakeNoteRepository();
        var vm = new GroupManagementViewModel(groups, notes);
        return (vm, groups, notes);
    }

    [Fact]
    public void Load_populates_groups_in_sort_order()
    {
        var (vm, groups, _) = CreateSut();
        groups.Create(new Group { Name = "개인", SortOrder = 2, IsSystem = false });
        groups.Create(new Group { Name = "업무", SortOrder = 1, IsSystem = false });
        groups.Create(new Group { Name = "일일업무일지", SortOrder = 0, IsSystem = true });

        vm.Load();

        vm.Groups.Select(g => g.Name).Should().Equal("일일업무일지", "업무", "개인");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.Load_populates_groups_in_sort_order"
```
예상: 컴파일 실패 — `error CS0246: The type or namespace name 'GroupManagementViewModel' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.App/ViewModels/GroupManagementViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public partial class GroupManagementViewModel : ObservableObject
{
    private readonly IGroupRepository _groups;
    private readonly INoteRepository _notes;

    public GroupManagementViewModel(IGroupRepository groups, INoteRepository notes)
    {
        _groups = groups;
        _notes = notes;
    }

    public ObservableCollection<Group> Groups { get; } = new();

    [ObservableProperty]
    private Group? _selectedGroup;

    public void Load()
    {
        Groups.Clear();
        foreach (var g in _groups.GetAll())
            Groups.Add(g);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.Load_populates_groups_in_sort_order"
```
예상: `Passed!  - Failed: 0, Passed: 1`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/GroupManagementViewModel.cs tests/Memoria.Tests/Fakes tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs
git commit -m "feat(m5): add GroupManagementViewModel load + test fakes

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: 그룹 추가 (`AddGroup`)

**Files:**
- Modify: `src/Memoria.App/ViewModels/GroupManagementViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs`

**Interfaces:**
- Consumes: `IGroupRepository.Create(Group) : int`.
- Produces: `GroupManagementViewModel.AddGroupCommand`(`IRelayCommand<string>`), `void AddGroup(string name)`.

- [ ] **Step 1: Write the failing test**

`GroupManagementViewModelTests.cs`에 추가:
```csharp
    [Fact]
    public void AddGroup_persists_non_system_group_with_next_sort_order()
    {
        var (vm, groups, _) = CreateSut();
        groups.Create(new Group { Name = "업무", SortOrder = 0, IsSystem = false });
        vm.Load();

        vm.AddGroup("신규 프로젝트");

        var created = groups.Items.Single(g => g.Name == "신규 프로젝트");
        created.IsSystem.Should().BeFalse();
        created.SortOrder.Should().Be(1);
        created.Color.Should().NotBeNull();
        vm.Groups.Should().Contain(g => g.Name == "신규 프로젝트");
    }

    [Fact]
    public void AddGroup_into_empty_list_uses_sort_order_zero()
    {
        var (vm, groups, _) = CreateSut();
        vm.Load();

        vm.AddGroup("첫 그룹");

        groups.Items.Single().SortOrder.Should().Be(0);
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.AddGroup"
```
예상: 컴파일 실패 — `error CS1061: 'GroupManagementViewModel' does not contain a definition for 'AddGroup'`.

- [ ] **Step 3: Write minimal implementation**

`GroupManagementViewModel.cs`에 using 및 멤버 추가:
```csharp
using System.Linq;
using CommunityToolkit.Mvvm.Input;
```
```csharp
    public const string DefaultGroupColor = "#9E9E9E";

    [RelayCommand]
    public void AddGroup(string name)
    {
        var nextOrder = Groups.Count == 0 ? 0 : Groups.Max(g => g.SortOrder) + 1;
        var group = new Group
        {
            Name = name,
            IsSystem = false,
            SortOrder = nextOrder,
            Color = DefaultGroupColor,
            CreatedAt = DateTimeOffset.UtcNow
        };
        group.Id = _groups.Create(group);
        Load();
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.AddGroup"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/GroupManagementViewModel.cs tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs
git commit -m "feat(m5): add AddGroup command to GroupManagementViewModel

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: 그룹 이름변경 + 시스템 그룹 보호

**Files:**
- Modify: `src/Memoria.App/ViewModels/GroupManagementViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs`

**Interfaces:**
- Consumes: `IGroupRepository.Update(Group)`.
- Produces: `GroupManagementViewModel.RenameGroupCommand`(`IRelayCommand<string>`, CanExecute=비시스템 선택), `void RenameGroup(string newName)`.

- [ ] **Step 1: Write the failing test**

`GroupManagementViewModelTests.cs`에 추가:
```csharp
    [Fact]
    public void RenameGroup_updates_user_group_name()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "업무", SortOrder = 0, IsSystem = false });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.RenameGroup("업무(수정)");

        groups.Get(id)!.Name.Should().Be("업무(수정)");
    }

    [Fact]
    public void RenameGroup_is_disabled_for_system_group()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "주간보고", SortOrder = 0, IsSystem = true });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.RenameGroupCommand.CanExecute("x").Should().BeFalse();

        // 직접 호출해도 시스템 그룹은 변경되지 않는다.
        vm.RenameGroup("변경시도");
        groups.Get(id)!.Name.Should().Be("주간보고");
    }

    [Fact]
    public void RenameGroup_is_enabled_for_user_group()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "개인", SortOrder = 0, IsSystem = false });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.RenameGroupCommand.CanExecute("x").Should().BeTrue();
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.RenameGroup"
```
예상: 컴파일 실패 — `error CS1061: ... does not contain a definition for 'RenameGroup'`.

- [ ] **Step 3: Write minimal implementation**

`SelectedGroup` 필드에 CanExecute 알림 어트리뷰트를 추가하고 명령을 구현한다.
```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameGroupCommand))]
    private Group? _selectedGroup;
```
```csharp
    private bool CanModifySelected() => SelectedGroup is { IsSystem: false };

    [RelayCommand(CanExecute = nameof(CanModifySelected))]
    public void RenameGroup(string newName)
    {
        if (SelectedGroup is null || SelectedGroup.IsSystem) return;
        SelectedGroup.Name = newName;
        _groups.Update(SelectedGroup);
        Load();
    }
```
(주의: 기존 `[ObservableProperty] private Group? _selectedGroup;` 선언을 위 어트리뷰트 포함 버전으로 교체한다.)

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.RenameGroup"
```
예상: `Passed!  - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/GroupManagementViewModel.cs tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs
git commit -m "feat(m5): rename group with system-group protection

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: 그룹 색상 지정 (`SetGroupColor`)

**Files:**
- Modify: `src/Memoria.App/ViewModels/GroupManagementViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs`

**Interfaces:**
- Consumes: `IGroupRepository.Update(Group)`.
- Produces: `GroupManagementViewModel.SetGroupColorCommand`(`IRelayCommand<string>`, CanExecute=선택 존재), `void SetGroupColor(string color)`.

> 색상 변경은 시스템 그룹에도 허용한다(스펙은 삭제·이름변경만 금지). CanExecute는 "선택 존재" 조건만 본다.

- [ ] **Step 1: Write the failing test**

`GroupManagementViewModelTests.cs`에 추가:
```csharp
    [Fact]
    public void SetGroupColor_persists_color()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "업무", SortOrder = 0, IsSystem = false });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.SetGroupColor("#FF5722");

        groups.Get(id)!.Color.Should().Be("#FF5722");
    }

    [Fact]
    public void SetGroupColor_is_allowed_for_system_group()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "일일업무일지", SortOrder = 0, IsSystem = true });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.SetGroupColorCommand.CanExecute("#000000").Should().BeTrue();
        vm.SetGroupColor("#000000");
        groups.Get(id)!.Color.Should().Be("#000000");
    }

    [Fact]
    public void SetGroupColor_is_disabled_without_selection()
    {
        var (vm, _, _) = CreateSut();
        vm.Load();

        vm.SetGroupColorCommand.CanExecute("#000000").Should().BeFalse();
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.SetGroupColor"
```
예상: 컴파일 실패 — `error CS1061: ... does not contain a definition for 'SetGroupColor'`.

- [ ] **Step 3: Write minimal implementation**

`SelectedGroup` 필드에 CanExecute 알림을 추가하고 명령을 구현한다.
```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameGroupCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetGroupColorCommand))]
    private Group? _selectedGroup;
```
```csharp
    private bool HasSelection() => SelectedGroup is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void SetGroupColor(string color)
    {
        if (SelectedGroup is null) return;
        SelectedGroup.Color = color;
        _groups.Update(SelectedGroup);
        Load();
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.SetGroupColor"
```
예상: `Passed!  - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/GroupManagementViewModel.cs tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs
git commit -m "feat(m5): set group color command

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: 그룹 삭제 + 시스템 그룹 보호 (메모는 미분류로)

**Files:**
- Modify: `src/Memoria.App/ViewModels/GroupManagementViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs`

**Interfaces:**
- Consumes: `IGroupRepository.Delete(int id)`(`notes.group_id ON DELETE SET NULL`), `INoteRepository.GetByGroup(int? groupId)`.
- Produces: `GroupManagementViewModel.DeleteGroupCommand`(`IRelayCommand`, CanExecute=비시스템 선택), `void DeleteGroup()`.

> SET NULL 자체는 DB 제약(M1)으로 보장된다. 본 Task의 페이크는 그 제약을 흉내내지 않으므로, VM 테스트는 "시스템 그룹은 삭제되지 않고 사용자 그룹은 `Delete` 호출됨"을 검증한다. SET NULL의 실제 DB 동작은 M1 통합 테스트 책임이다.

- [ ] **Step 1: Write the failing test**

`GroupManagementViewModelTests.cs`에 추가:
```csharp
    [Fact]
    public void DeleteGroup_removes_user_group()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "업무", SortOrder = 0, IsSystem = false });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.DeleteGroup();

        groups.Get(id).Should().BeNull();
        vm.Groups.Should().NotContain(g => g.Id == id);
    }

    [Fact]
    public void DeleteGroup_is_disabled_for_system_group()
    {
        var (vm, groups, _) = CreateSut();
        var id = groups.Create(new Group { Name = "일일업무일지", SortOrder = 0, IsSystem = true });
        vm.Load();
        vm.SelectedGroup = vm.Groups.Single(g => g.Id == id);

        vm.DeleteGroupCommand.CanExecute(null).Should().BeFalse();

        vm.DeleteGroup();
        groups.Get(id).Should().NotBeNull();
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.DeleteGroup"
```
예상: 컴파일 실패 — `error CS1061: ... does not contain a definition for 'DeleteGroup'`.

- [ ] **Step 3: Write minimal implementation**

`SelectedGroup` 필드에 알림을 추가하고 명령을 구현한다.
```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameGroupCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetGroupColorCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteGroupCommand))]
    private Group? _selectedGroup;
```
```csharp
    [RelayCommand(CanExecute = nameof(CanModifySelected))]
    public void DeleteGroup()
    {
        if (SelectedGroup is null || SelectedGroup.IsSystem) return;
        _groups.Delete(SelectedGroup.Id); // notes.group_id ON DELETE SET NULL → 메모는 (미분류)로
        Load();
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.DeleteGroup"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/GroupManagementViewModel.cs tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs
git commit -m "feat(m5): delete group with system-group protection (notes set null)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: 그룹 드래그 순서변경 (`MoveGroup`)

**Files:**
- Modify: `src/Memoria.App/ViewModels/GroupManagementViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs`

**Interfaces:**
- Consumes: `IGroupRepository.Update(Group)`.
- Produces: `GroupManagementViewModel.MoveGroup(int fromIndex, int toIndex)`.

> 드래그 제스처 자체는 Task 12에서 수동 검증한다. 본 Task는 인덱스 기반 재배치 + `SortOrder` 재할당 + 변경분 영속화 로직을 자동 검증한다.

- [ ] **Step 1: Write the failing test**

`GroupManagementViewModelTests.cs`에 추가:
```csharp
    [Fact]
    public void MoveGroup_reorders_and_reassigns_sort_order()
    {
        var (vm, groups, _) = CreateSut();
        groups.Create(new Group { Name = "A", SortOrder = 0 });
        groups.Create(new Group { Name = "B", SortOrder = 1 });
        groups.Create(new Group { Name = "C", SortOrder = 2 });
        vm.Load();

        vm.MoveGroup(0, 2); // A를 맨 뒤로

        vm.Groups.Select(g => g.Name).Should().Equal("B", "C", "A");
        groups.Items.Single(g => g.Name == "B").SortOrder.Should().Be(0);
        groups.Items.Single(g => g.Name == "C").SortOrder.Should().Be(1);
        groups.Items.Single(g => g.Name == "A").SortOrder.Should().Be(2);
    }

    [Fact]
    public void MoveGroup_ignores_out_of_range_or_noop()
    {
        var (vm, groups, _) = CreateSut();
        groups.Create(new Group { Name = "A", SortOrder = 0 });
        groups.Create(new Group { Name = "B", SortOrder = 1 });
        vm.Load();

        vm.MoveGroup(0, 0);   // no-op
        vm.MoveGroup(-1, 1);  // 범위 밖
        vm.MoveGroup(0, 5);   // 범위 밖

        vm.Groups.Select(g => g.Name).Should().Equal("A", "B");
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.MoveGroup"
```
예상: 컴파일 실패 — `error CS1061: ... does not contain a definition for 'MoveGroup'`.

- [ ] **Step 3: Write minimal implementation**

`GroupManagementViewModel.cs`에 추가:
```csharp
    public void MoveGroup(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Groups.Count) return;
        if (toIndex < 0 || toIndex >= Groups.Count) return;
        if (fromIndex == toIndex) return;

        var item = Groups[fromIndex];
        Groups.RemoveAt(fromIndex);
        Groups.Insert(toIndex, item);

        for (var i = 0; i < Groups.Count; i++)
        {
            if (Groups[i].SortOrder != i)
            {
                Groups[i].SortOrder = i;
                _groups.Update(Groups[i]); // sort_order는 메타 조작
            }
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.MoveGroup"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/GroupManagementViewModel.cs tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs
git commit -m "feat(m5): reorder groups via MoveGroup with sort_order persistence

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: 메모 그룹 이동 (`MoveNoteToGroup`, updated_at 미갱신)

**Files:**
- Modify: `src/Memoria.App/ViewModels/GroupManagementViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs`

**Interfaces:**
- Consumes: `INoteRepository.Get(int id) : Note?`, `INoteRepository.Update(Note)`(Note 그대로 저장).
- Produces: `GroupManagementViewModel.MoveNoteToGroup(int noteId, int? targetGroupId)`.

> §7.7: 그룹 이동은 메타 조작 → `updated_at` 미갱신. VM은 `Note.UpdatedAt`을 건드리지 않고 `Update`에 그대로 전달해야 한다.

- [ ] **Step 1: Write the failing test**

`GroupManagementViewModelTests.cs`에 추가:
```csharp
    [Fact]
    public void MoveNoteToGroup_changes_group_without_touching_updated_at()
    {
        var (vm, _, notes) = CreateSut();
        var fixedClock = new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero));
        notes.Clock = fixedClock;
        var noteId = notes.Create(new Memoria.Core.Models.Note
        {
            Type = Memoria.Core.Models.NoteType.Plain,
            Title = "메모",
            GroupId = null
        });
        var originalUpdatedAt = notes.Get(noteId)!.UpdatedAt;

        // 시간이 흐른 뒤 이동해도 updated_at은 그대로여야 한다.
        fixedClock.Advance(TimeSpan.FromHours(5));
        vm.MoveNoteToGroup(noteId, 42);

        var moved = notes.Get(noteId)!;
        moved.GroupId.Should().Be(42);
        moved.UpdatedAt.Should().Be(originalUpdatedAt);
    }

    [Fact]
    public void MoveNoteToGroup_to_unclassified_sets_group_null()
    {
        var (vm, _, notes) = CreateSut();
        var noteId = notes.Create(new Memoria.Core.Models.Note
        {
            Type = Memoria.Core.Models.NoteType.Plain,
            GroupId = 7
        });

        vm.MoveNoteToGroup(noteId, null);

        notes.Get(noteId)!.GroupId.Should().BeNull();
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.MoveNoteToGroup"
```
예상: 컴파일 실패 — `error CS1061: ... does not contain a definition for 'MoveNoteToGroup'`.

- [ ] **Step 3: Write minimal implementation**

`GroupManagementViewModel.cs`에 추가:
```csharp
    public void MoveNoteToGroup(int noteId, int? targetGroupId)
    {
        var note = _notes.Get(noteId);
        if (note is null) return;
        if (note.GroupId == targetGroupId) return;

        note.GroupId = targetGroupId;
        // §7.7: 그룹 이동은 메타 조작 → UpdatedAt을 갱신하지 않고 그대로 저장한다.
        _notes.Update(note);
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GroupManagementViewModelTests.MoveNoteToGroup"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/GroupManagementViewModel.cs tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs
git commit -m "feat(m5): move note between groups without bumping updated_at

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: `TrashViewModel` 로드 + `TrashItemViewModel` 만료일 계산

**Files:**
- Create: `src/Memoria.App/ViewModels/TrashItemViewModel.cs`, `src/Memoria.App/ViewModels/TrashViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/TrashItemViewModelTests.cs`, `tests/Memoria.Tests/ViewModels/TrashViewModelTests.cs`

**Interfaces:**
- Consumes: `INoteRepository.GetTrash() : IReadOnlyList<Note>`, `ISettingsRepository.GetOrDefault(string, string)`, `Memoria.Core.SettingsKeys.TrashRetentionDays`, `Memoria.Core.Models.Note`.
- Produces: `Memoria.App.ViewModels.TrashItemViewModel`(ctor `(Note, int retentionDays, DateTimeOffset now)`, `int DaysUntilPurge`, `bool IsExpired`, `int Id`, `string DisplayTitle`), `Memoria.App.ViewModels.TrashViewModel`(ctor `(INoteRepository, ISettingsRepository, TimeProvider?)`, `ObservableCollection<TrashItemViewModel> Items`, `int RetentionDays`, `void Load()`).

- [ ] **Step 1: Write the failing test**

`tests/Memoria.Tests/ViewModels/TrashItemViewModelTests.cs`:
```csharp
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class TrashItemViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DaysUntilPurge_ceils_remaining_days()
    {
        var note = new Note { Id = 1, Title = "T", DeletedAt = Now.AddDays(-5) };
        var vm = new TrashItemViewModel(note, retentionDays: 30, now: Now);

        vm.DaysUntilPurge.Should().Be(25);
        vm.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void DaysUntilPurge_is_zero_and_expired_when_past_retention()
    {
        var note = new Note { Id = 2, Title = "T", DeletedAt = Now.AddDays(-31) };
        var vm = new TrashItemViewModel(note, retentionDays: 30, now: Now);

        vm.DaysUntilPurge.Should().Be(0);
        vm.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void DisplayTitle_falls_back_when_blank()
    {
        var note = new Note { Id = 3, Title = "   ", DeletedAt = Now };
        var vm = new TrashItemViewModel(note, retentionDays: 30, now: Now);

        vm.DisplayTitle.Should().Be("(제목 없음)");
        vm.Id.Should().Be(3);
    }
}
```

`tests/Memoria.Tests/ViewModels/TrashViewModelTests.cs`:
```csharp
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core;
using Memoria.Core.Models;
using Memoria.Tests.Fakes;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class TrashViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 26, 0, 0, 0, TimeSpan.Zero);

    private static (TrashViewModel vm, FakeNoteRepository notes, FakeSettingsRepository settings) CreateSut()
    {
        var notes = new FakeNoteRepository { Clock = new FixedTimeProvider(Now) };
        var settings = new FakeSettingsRepository();
        var vm = new TrashViewModel(notes, settings, new FixedTimeProvider(Now));
        return (vm, notes, settings);
    }

    [Fact]
    public void RetentionDays_defaults_to_30_when_setting_absent()
    {
        var (vm, _, _) = CreateSut();
        vm.RetentionDays.Should().Be(30);
    }

    [Fact]
    public void RetentionDays_reads_setting()
    {
        var (vm, _, settings) = CreateSut();
        settings.Set(SettingsKeys.TrashRetentionDays, "14");
        vm.RetentionDays.Should().Be(14);
    }

    [Fact]
    public void Load_lists_only_trashed_notes()
    {
        var (vm, notes, _) = CreateSut();
        var trashedId = notes.Create(new Note { Type = NoteType.Plain, Title = "삭제됨" });
        notes.Create(new Note { Type = NoteType.Plain, Title = "활성" });
        notes.SoftDelete(trashedId);

        vm.Load();

        vm.Items.Should().HaveCount(1);
        vm.Items[0].Id.Should().Be(trashedId);
        vm.Items[0].DisplayTitle.Should().Be("삭제됨");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~TrashItemViewModelTests|FullyQualifiedName~TrashViewModelTests"
```
예상: 컴파일 실패 — `error CS0246: The type or namespace name 'TrashItemViewModel'/'TrashViewModel' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`src/Memoria.App/ViewModels/TrashItemViewModel.cs`:
```csharp
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public sealed class TrashItemViewModel
{
    private readonly DateTimeOffset _now;

    public TrashItemViewModel(Note note, int retentionDays, DateTimeOffset now)
    {
        Note = note;
        RetentionDays = retentionDays;
        _now = now;
    }

    public Note Note { get; }
    public int RetentionDays { get; }

    public int Id => Note.Id;

    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(Note.Title) ? "(제목 없음)" : Note.Title!;

    public DateTimeOffset? DeletedAt => Note.DeletedAt;

    public int DaysUntilPurge
    {
        get
        {
            if (Note.DeletedAt is not { } deletedAt) return RetentionDays;
            var purgeAt = deletedAt.AddDays(RetentionDays);
            var remaining = (purgeAt - _now).TotalDays;
            return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining);
        }
    }

    public bool IsExpired => DaysUntilPurge <= 0;
}
```

`src/Memoria.App/ViewModels/TrashViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Memoria.Core;
using Memoria.Core.Data;

namespace Memoria.App.ViewModels;

public partial class TrashViewModel : ObservableObject
{
    private readonly INoteRepository _notes;
    private readonly ISettingsRepository _settings;
    private readonly TimeProvider _clock;

    public TrashViewModel(INoteRepository notes, ISettingsRepository settings, TimeProvider? clock = null)
    {
        _notes = notes;
        _settings = settings;
        _clock = clock ?? TimeProvider.System;
    }

    public ObservableCollection<TrashItemViewModel> Items { get; } = new();

    public int RetentionDays =>
        int.TryParse(_settings.GetOrDefault(SettingsKeys.TrashRetentionDays, "30"), out var d) ? d : 30;

    public void Load()
    {
        var now = _clock.GetUtcNow();
        var days = RetentionDays;
        Items.Clear();
        foreach (var n in _notes.GetTrash())
            Items.Add(new TrashItemViewModel(n, days, now));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~TrashItemViewModelTests|FullyQualifiedName~TrashViewModelTests"
```
예상: `Passed!  - Failed: 0, Passed: 6`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/TrashItemViewModel.cs src/Memoria.App/ViewModels/TrashViewModel.cs tests/Memoria.Tests/ViewModels/TrashItemViewModelTests.cs tests/Memoria.Tests/ViewModels/TrashViewModelTests.cs
git commit -m "feat(m5): add TrashViewModel load + TrashItemViewModel purge-countdown

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: 메모 SoftDelete + Undo 상태

**Files:**
- Modify: `src/Memoria.App/ViewModels/TrashViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/TrashViewModelTests.cs`

**Interfaces:**
- Consumes: `INoteRepository.SoftDelete(int id)`, `INoteRepository.Restore(int id)`.
- Produces: `TrashViewModel.DeleteNoteCommand`(`IRelayCommand<int>`), `void DeleteNote(int noteId)`, `TrashViewModel.UndoCommand`(`IRelayCommand`), `void Undo()`, `bool IsUndoAvailable`, `string? UndoMessage`.

> Undo 토스트의 시각 표시는 Task 12에서 수동 검증한다. 본 Task는 SoftDelete 호출 + Undo 상태 전이 + Restore 호출을 자동 검증한다.

- [ ] **Step 1: Write the failing test**

`TrashViewModelTests.cs`에 추가:
```csharp
    [Fact]
    public void DeleteNote_soft_deletes_and_sets_undo_state()
    {
        var (vm, notes, _) = CreateSut();
        var id = notes.Create(new Note { Type = NoteType.Plain, Title = "메모" });

        vm.DeleteNote(id);

        notes.Get(id)!.DeletedAt.Should().NotBeNull();
        vm.IsUndoAvailable.Should().BeTrue();
        vm.UndoMessage.Should().NotBeNullOrEmpty();
        vm.UndoCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Undo_restores_last_deleted_and_clears_state()
    {
        var (vm, notes, _) = CreateSut();
        var id = notes.Create(new Note { Type = NoteType.Plain, Title = "메모" });
        vm.DeleteNote(id);

        vm.Undo();

        notes.Get(id)!.DeletedAt.Should().BeNull();
        vm.IsUndoAvailable.Should().BeFalse();
        vm.UndoMessage.Should().BeNull();
        vm.UndoCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Undo_is_noop_without_pending_deletion()
    {
        var (vm, notes, _) = CreateSut();
        var id = notes.Create(new Note { Type = NoteType.Plain });
        notes.SoftDelete(id);

        vm.Undo(); // 대기 중인 삭제 없음

        notes.Get(id)!.DeletedAt.Should().NotBeNull(); // 복원되지 않음
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~TrashViewModelTests.DeleteNote|FullyQualifiedName~TrashViewModelTests.Undo"
```
예상: 컴파일 실패 — `error CS1061: ... does not contain a definition for 'DeleteNote'`.

- [ ] **Step 3: Write minimal implementation**

`TrashViewModel.cs`에 using 및 멤버 추가:
```csharp
using CommunityToolkit.Mvvm.Input;
```
```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _isUndoAvailable;

    [ObservableProperty]
    private string? _undoMessage;

    private int _lastDeletedNoteId;

    [RelayCommand]
    public void DeleteNote(int noteId)
    {
        _notes.SoftDelete(noteId);
        _lastDeletedNoteId = noteId;
        IsUndoAvailable = true;
        UndoMessage = "메모를 휴지통으로 옮겼습니다.";
    }

    private bool CanUndo() => IsUndoAvailable;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo()
    {
        if (!IsUndoAvailable) return;
        _notes.Restore(_lastDeletedNoteId);
        IsUndoAvailable = false;
        UndoMessage = null;
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~TrashViewModelTests.DeleteNote|FullyQualifiedName~TrashViewModelTests.Undo"
```
예상: `Passed!  - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/TrashViewModel.cs tests/Memoria.Tests/ViewModels/TrashViewModelTests.cs
git commit -m "feat(m5): soft-delete note with undo state in TrashViewModel

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: 휴지통 복원 / 영구삭제 (`Restore` / `Purge`)

**Files:**
- Modify: `src/Memoria.App/ViewModels/TrashViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/TrashViewModelTests.cs`

**Interfaces:**
- Consumes: `INoteRepository.Restore(int id)`, `INoteRepository.Purge(int id)`(`checklist_items` CASCADE).
- Produces: `TrashViewModel.RestoreCommand`(`IRelayCommand<int>`), `void Restore(int noteId)`, `TrashViewModel.PurgeCommand`(`IRelayCommand<int>`), `void Purge(int noteId)`.

- [ ] **Step 1: Write the failing test**

`TrashViewModelTests.cs`에 추가:
```csharp
    [Fact]
    public void Restore_removes_from_trash_list()
    {
        var (vm, notes, _) = CreateSut();
        var id = notes.Create(new Note { Type = NoteType.Plain, Title = "메모" });
        notes.SoftDelete(id);
        vm.Load();
        vm.Items.Should().HaveCount(1);

        vm.Restore(id);

        notes.Get(id)!.DeletedAt.Should().BeNull();
        vm.Items.Should().BeEmpty();
    }

    [Fact]
    public void Purge_permanently_deletes_and_reloads()
    {
        var (vm, notes, _) = CreateSut();
        var id = notes.Create(new Note { Type = NoteType.Plain, Title = "메모" });
        notes.SoftDelete(id);
        vm.Load();

        vm.Purge(id);

        notes.Get(id).Should().BeNull();
        vm.Items.Should().BeEmpty();
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~TrashViewModelTests.Restore|FullyQualifiedName~TrashViewModelTests.Purge"
```
예상: 컴파일 실패 — `error CS1061: ... does not contain a definition for 'Restore'`.

- [ ] **Step 3: Write minimal implementation**

`TrashViewModel.cs`에 추가:
```csharp
    [RelayCommand]
    public void Restore(int noteId)
    {
        _notes.Restore(noteId);
        Load();
    }

    [RelayCommand]
    public void Purge(int noteId)
    {
        _notes.Purge(noteId); // checklist_items CASCADE
        Load();
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~TrashViewModelTests.Restore|FullyQualifiedName~TrashViewModelTests.Purge"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/TrashViewModel.cs tests/Memoria.Tests/ViewModels/TrashViewModelTests.cs
git commit -m "feat(m5): restore and purge notes from trash

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 11: 시작 시 만료 휴지통 영구삭제 (`PurgeExpiredOnStartup`)

**Files:**
- Modify: `src/Memoria.App/ViewModels/TrashViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/TrashViewModelTests.cs`

**Interfaces:**
- Consumes: `INoteRepository.PurgeExpiredTrash(int retentionDays)`, `ISettingsRepository.GetOrDefault(string, string)`, `SettingsKeys.TrashRetentionDays`.
- Produces: `TrashViewModel.PurgeExpiredOnStartup()`.

> 앱 시작 시 1회 호출(Task 12에서 부트스트랩에 연결). 만료 판정의 실제 날짜 컷오프는 `INoteRepository.PurgeExpiredTrash`(M1)가 수행하며, 본 Task는 VM이 설정값을 정확히 읽어 전달하고 만료분만 사라짐을 검증한다.

- [ ] **Step 1: Write the failing test**

`TrashViewModelTests.cs`에 추가:
```csharp
    [Fact]
    public void PurgeExpiredOnStartup_uses_retention_setting_and_removes_only_expired()
    {
        var (vm, notes, settings) = CreateSut();
        settings.Set(SettingsKeys.TrashRetentionDays, "30");

        var expiredId = notes.Create(new Note { Type = NoteType.Plain, Title = "오래됨" });
        var recentId = notes.Create(new Note { Type = NoteType.Plain, Title = "최근" });
        notes.Get(expiredId)!.DeletedAt = Now.AddDays(-31); // 만료
        notes.Get(recentId)!.DeletedAt = Now.AddDays(-5);   // 유효

        vm.PurgeExpiredOnStartup();

        notes.Get(expiredId).Should().BeNull();
        notes.Get(recentId).Should().NotBeNull();
    }

    [Fact]
    public void PurgeExpiredOnStartup_defaults_to_30_days_when_setting_absent()
    {
        var (vm, notes, _) = CreateSut();
        var id = notes.Create(new Note { Type = NoteType.Plain });
        notes.Get(id)!.DeletedAt = Now.AddDays(-40);

        vm.PurgeExpiredOnStartup();

        notes.Get(id).Should().BeNull();
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~TrashViewModelTests.PurgeExpiredOnStartup"
```
예상: 컴파일 실패 — `error CS1061: ... does not contain a definition for 'PurgeExpiredOnStartup'`.

- [ ] **Step 3: Write minimal implementation**

`TrashViewModel.cs`에 추가:
```csharp
    public void PurgeExpiredOnStartup()
    {
        _notes.PurgeExpiredTrash(RetentionDays);
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~TrashViewModelTests.PurgeExpiredOnStartup"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/TrashViewModel.cs tests/Memoria.Tests/ViewModels/TrashViewModelTests.cs
git commit -m "feat(m5): purge expired trash on startup using retention setting

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 12: DI 등록 + WPF UI 통합 (그룹 사이드바 메뉴 / 휴지통 뷰 / Undo 토스트 / 드래그) — 수동 검증

**Files:**
- Modify: `src/Memoria.App/App.xaml.cs`(DI 등록 + 시작 시 `PurgeExpiredOnStartup` 호출)
- Create: `src/Memoria.App/Views/TrashView.xaml` + `src/Memoria.App/Views/TrashView.xaml.cs`
- Modify: `src/Memoria.App/Views/MainWindow.xaml`(그룹 사이드바 컨텍스트 메뉴, 휴지통 진입점, Undo 토스트 영역) + `src/Memoria.App/Views/MainWindow.xaml.cs`(드래그 순서변경 → `MoveGroup`, 메모 드롭 → `MoveNoteToGroup` 얇은 핸들러)

**Interfaces:**
- Consumes: `GroupManagementViewModel`(`AddGroup`/`RenameGroup`/`SetGroupColor`/`DeleteGroup`/`MoveGroup`/`MoveNoteToGroup`), `TrashViewModel`(`DeleteNote`/`Undo`/`Restore`/`Purge`/`PurgeExpiredOnStartup`/`Items`/`IsUndoAvailable`/`UndoMessage`), 기존 M2 DI 컨테이너(`IServiceCollection`).
- Produces: 화면에 노출된 그룹 CRUD/휴지통 UI. (자동 테스트 대상 아님 — 로직은 Task 1~11에서 검증 완료.)

> 이 Task는 시각/드래그/토스트 등 자동 테스트 불가 영역이다. code-behind는 ViewModel 메서드 호출만 하는 얇은 위임으로 유지하고, 모든 색/브러시는 `DynamicResource`만 사용한다.

- [ ] **Step 1: DI 등록 + 시작 시 만료 정리 (App.xaml.cs 누적 패치)**

> **누적 패치 원칙(계약 §9.4):** `App.xaml.cs`를 **전체 재작성하지 않는다.** M2가 만든 `App.xaml.cs`의 기존 `OnStartup`/서비스 구성부를 그대로 보존하고, M5는 아래 두 가지(서비스 등록 1줄·1줄, 부트스트랩 step 9 호출 1줄)만 **추가 삽입**한다. 다른 마일스톤(M2/M6/M7/M9)이 등록·배선한 기존 호출은 건드리지 않는다.

(1) M2가 만든 서비스 구성부(`ConfigureServices`/`ServiceCollection`)에 **두 줄만 추가**:
```csharp
services.AddTransient<GroupManagementViewModel>();
services.AddSingleton<TrashViewModel>();
```

(2) 부트스트랩 순서의 **§9.4 step 9** (`INoteRepository.PurgeExpiredTrash(trashRetentionDays) (M5)`) 위치에, `IDatabaseInitializer.EnsureReady()`(step 4) 이후·`MainWindow` 생성(step 10) 이전 지점에 **한 줄만 추가**:
```csharp
// §9.4 step 9 (M5): 시작 시 보존기간 만료된 휴지통 항목을 영구삭제
serviceProvider.GetRequiredService<TrashViewModel>().PurgeExpiredOnStartup();
```
(`using Memoria.App.ViewModels;` 가 없으면 추가. M2/M9가 정의해 둔 기존 `OnStartup` 본문·서비스 등록·다른 step 호출은 수정·삭제하지 않고 위 두 추가만 반영한다.)

- [ ] **Step 2: 휴지통 뷰 작성**

`src/Memoria.App/Views/TrashView.xaml` — `Items` 바인딩 리스트 + 항목별 `DisplayTitle`, `DaysUntilPurge`, 복원/영구삭제 버튼. 모든 색/브러시는 **계약 §10 브러시 키만** `DynamicResource`로 사용(StaticResource·임의 키 금지). 예:
```xml
<UserControl x:Class="Memoria.App.Views.TrashView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="{DynamicResource Brush.WindowBackground}">
    <ListBox ItemsSource="{Binding Items}" Background="{DynamicResource Brush.Surface}">
        <ListBox.ItemTemplate>
            <DataTemplate>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="{Binding DisplayTitle}"
                               Foreground="{DynamicResource Brush.Foreground}"/>
                    <TextBlock Grid.Column="1" Margin="8,0"
                               Text="{Binding DaysUntilPurge, StringFormat='{}{0}일 후 삭제'}"
                               Foreground="{DynamicResource Brush.SecondaryForeground}"/>
                    <Button Grid.Column="2" Content="복원"
                            Command="{Binding DataContext.RestoreCommand, RelativeSource={RelativeSource AncestorType=ListBox}}"
                            CommandParameter="{Binding Id}"/>
                    <Button Grid.Column="3" Content="영구삭제"
                            Command="{Binding DataContext.PurgeCommand, RelativeSource={RelativeSource AncestorType=ListBox}}"
                            CommandParameter="{Binding Id}"/>
                </Grid>
            </DataTemplate>
        </ListBox.ItemTemplate>
    </ListBox>
</UserControl>
```
`TrashView.xaml.cs`는 `InitializeComponent()`만 두는 얇은 code-behind. `DataContext`는 DI의 `TrashViewModel` 주입.

- [ ] **Step 3: 메인 창 그룹 컨텍스트 메뉴 + Undo 토스트 + 드래그 핸들러**

`MainWindow.xaml` 사이드바 그룹 항목에 컨텍스트 메뉴(추가/이름변경/색상/삭제) 바인딩. `RenameMenuItem`/`DeleteMenuItem`의 `IsEnabled`는 `RenameGroupCommand.CanExecute`/`DeleteGroupCommand.CanExecute`로 자동 비활성 → 시스템 그룹에서 회색. Undo 토스트는 `IsUndoAvailable`로 `Visibility` 토글하는 Border + `UndoMessage` 텍스트 + `UndoCommand` 버튼.
code-behind는 드래그 드롭만 얇게 위임:
```csharp
// 그룹 순서변경: 드롭 시 인덱스 계산 후 위임
private void GroupList_Drop(object sender, DragEventArgs e)
{
    var (from, to) = ResolveDragIndices(sender, e); // 인덱스 계산만
    GroupVm.MoveGroup(from, to);
}

// 메모를 그룹으로 드롭: noteId/targetGroupId 추출 후 위임
private void GroupNode_DropNote(object sender, DragEventArgs e)
{
    var noteId = (int)e.Data.GetData("noteId");
    var targetGroupId = ((Group)((FrameworkElement)sender).DataContext).Id;
    GroupVm.MoveNoteToGroup(noteId, targetGroupId);
}
```

- [ ] **Step 4: 빌드 검증**

```
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\Memoria.sln"
```
예상: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: 수동 검증 체크포인트 (Windows 실행)**

`dotnet.exe run --project "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App"` 로 실행 후 눈으로 확인:
- [ ] 사이드바에서 "새 그룹" → 그룹이 목록 맨 아래에 추가되고 즉시 이름 편집 가능.
- [ ] 사용자 그룹 우클릭 → "이름변경"/"삭제" 활성. **시스템 그룹(일일업무일지/주간보고) 우클릭 → "이름변경"/"삭제" 회색 비활성**, "색상" 변경은 가능.
- [ ] 그룹에 색상 지정 → 사이드바 색 점/막대가 즉시 바뀜(DynamicResource 적용, 테마 전환에도 깨지지 않음).
- [ ] 그룹을 위/아래로 **드래그** → 순서가 바뀌고 앱 재시작 후에도 유지(sort_order 영속).
- [ ] 사용자 그룹 삭제 → 그 안의 메모가 사라지지 않고 **(미분류)** 가상 노드로 이동.
- [ ] 메모를 다른 그룹으로 드래그 → 그룹이 바뀌되 에디터 헤더의 **"수정"(updated_at) 표시가 변하지 않음**.
- [ ] 메모 삭제(🗑) → 사이드바에서 사라지고 **하단에 Undo 토스트** 표시 → "실행취소" 클릭 시 즉시 복원.
- [ ] 휴지통 진입 → 삭제된 메모 목록 + "N일 후 삭제" 카운트다운 표시, "복원"/"영구삭제" 동작.
- [ ] (선택) `trash.retentionDays`를 작게 설정하고 과거 날짜로 삭제된 항목을 둔 뒤 재시작 → 시작 시 만료 항목이 자동으로 사라짐.

- [ ] **Step 6: Commit**

```
git add src/Memoria.App/App.xaml.cs src/Memoria.App/Views/TrashView.xaml src/Memoria.App/Views/TrashView.xaml.cs src/Memoria.App/Views/MainWindow.xaml src/Memoria.App/Views/MainWindow.xaml.cs
git commit -m "feat(m5): wire group CRUD + trash UI with DI and startup purge

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 완료 기준 (Definition of Done)
- Task 1~11의 모든 xUnit 테스트가 Windows `dotnet.exe test`에서 통과(총 24개 테스트).
- `dotnet.exe build Memoria.sln` 성공(0 Error).
- Task 12의 수동 검증 체크포인트 전 항목 확인.
- 산출물: `GroupManagementViewModel`, `TrashViewModel`, `TrashItemViewModel` (모두 `CommunityToolkit.Mvvm`만 의존, code-behind 얇음).
- 전역 제약 준수: 시스템 그룹 보호, 그룹 삭제 SET NULL, 그룹 이동 updated_at 미갱신, 휴지통 보존기간 설정 연동 + 시작 시 만료 정리, 모든 색상 DynamicResource.

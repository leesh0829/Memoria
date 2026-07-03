# 그룹 중첩(폴더 트리) Implementation Plan — Memoria v0.2.0

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 사용자 그룹을 폴더처럼 무제한 중첩(트리)하고, 하위 그룹 생성·드래그 재부모(3존)·펼침/접기·애니메이션을 지원한다.

**Architecture:** 스키마는 그대로(`groups.parent_id` 이미 존재). 리포지토리가 사이클/자기부모의 최종 방어이자 삭제-승격·형제 재번호를 트랜잭션으로 수행. 사이드바 사용자 그룹을 `TreeView`로 바꾸고 선택/펼침을 VM 플래그(`IsSelected`/`IsExpanded`)+`ItemContainerStyle` 양방향으로 구동. 드롭 위치 계산은 순수 함수로 분리해 단위테스트.

**Tech Stack:** C#/.NET 9, WPF(net9.0-windows), CommunityToolkit.Mvvm, Dapper+Microsoft.Data.Sqlite, xUnit+FluentAssertions.

## Global Constraints
- 빌드/테스트는 WSL에서 **Windows `dotnet.exe`** interop로 실행(예: `dotnet.exe build "Memoria.sln" -c Release`, `dotnet.exe test "tests/Memoria.Tests" -c Release`). WPF는 Linux 네이티브 빌드 불가.
- 실행 중 GUI가 파일을 잠그면 빌드 실패 → 빌드 전 `taskkill.exe /IM Memoria.exe /F` (없으면 무시).
- 브랜치: `feature/nested-groups`. 커밋은 이 브랜치에.
- 단일 직렬 라이터: 모든 쓰기는 `lock (_factory.WriteSync)` 안에서 `_factory.Write` 사용. 다중 문장은 `_factory.Write.BeginTransaction()` + `transaction: tx` + `tx.Commit()`.
- `PRAGMA foreign_keys=ON`. 자기참조 FK는 사이클/자기부모를 못 막으므로 **앱 코드가 방어**.
- 테스트 병렬화 비활성(기존 `TestParallelization.cs`). 새 테스트도 그 규약 준수.
- 기존 294개 테스트 그린 유지. 각 태스크 끝에서 관련 테스트 통과 확인.
- 스펙: `docs/superpowers/specs/2026-07-02-nested-groups-design.md`.

---

## File Structure

**수정(Core):**
- `src/Memoria.Core/Data/IGroupRepository.cs` — `SetParent`, `ReorderSiblings`, `IsDescendantOf` 추가.
- `src/Memoria.Core/Data/GroupRepository.cs` — 위 구현 + `Delete` 승격/재번호.

**수정(App VM):**
- `src/Memoria.App/ViewModels/SidebarNodeViewModel.cs` — `Children`/`IsExpanded`/`IsSelected`.
- `src/Memoria.App/ViewModels/GroupManagementViewModel.cs` — `AddSubGroup`, `MoveGroup(groupId,newParentId,index)`.
- `src/Memoria.App/ViewModels/MainViewModel.cs` — `LoadGroups` 트리 구성 + 스냅샷/복원 + 삭제후 선택.

**신규(App):**
- `src/Memoria.App/ViewModels/GroupDropCalculator.cs` — 순수 드롭 위치 계산(테스트 대상).
- `src/Memoria.App/Windows/DragAdorner.cs` — 드래그 고스트 어도너(애니메이션 A).

**수정(App UI):**
- `src/Memoria.App/MainWindow.xaml` — 사용자 그룹 `TreeView` + 컨텍스트 메뉴 + 드래그 훅.
- `src/Memoria.App/MainWindow.xaml.cs` — 3존 드래그 핸들러(포인터 히트테스트), 선택 동기화(2표면), 최상위 이동, 애니메이션 배선.
- `src/Memoria.App/Themes/Base.xaml` — `TreeView`/`TreeViewItem` 플랫 템플릿, `ListItemTextTree` 스타일, 펼침/페이드 애니메이션 리소스.

**신규(Tests):**
- `tests/Memoria.Tests/Data/GroupRepositoryNestingTests.cs`
- `tests/Memoria.Tests/App/GroupDropCalculatorTests.cs`
- 기존 수정: `tests/Memoria.Tests/App/MainViewModelSidebarTests.cs`, `tests/Memoria.Tests/App/Fakes/FakeGroupRepository.cs`(신규 메서드), `tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs`(있으면).

---

## N1 — 리포지토리: 후손판정 / SetParent(백스톱) / 형제재번호 / 삭제-승격

### Task N1.1: `IsDescendantOf` (사이클 판정, 방문집합 가드)

**Files:**
- Modify: `src/Memoria.Core/Data/IGroupRepository.cs`
- Modify: `src/Memoria.Core/Data/GroupRepository.cs`
- Test: `tests/Memoria.Tests/Data/GroupRepositoryNestingTests.cs` (create)

**Interfaces:**
- Produces: `bool IGroupRepository.IsDescendantOf(int nodeId, int ancestorId)` — `nodeId`가 `ancestorId`의 (직·간접) 후손이면 true. `nodeId == ancestorId`는 false(자기 자신은 후손 아님). 사이클이 있어도 방문집합으로 안전 종료.

- [ ] **Step 1: 실패 테스트 작성** — `tests/Memoria.Tests/Data/GroupRepositoryNestingTests.cs`

```csharp
using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Data;

public class GroupRepositoryNestingTests
{
    [Fact]
    public void IsDescendantOf_TrueForChildAndGrandchild_FalseForSelfAndUnrelated()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);
        var a = sut.Create(new Group { Name = "A" });
        var b = sut.Create(new Group { Name = "B", ParentId = a });
        var c = sut.Create(new Group { Name = "C", ParentId = b });
        var x = sut.Create(new Group { Name = "X" });

        sut.IsDescendantOf(b, a).Should().BeTrue();   // B는 A의 후손
        sut.IsDescendantOf(c, a).Should().BeTrue();   // C는 A의 후손(손자)
        sut.IsDescendantOf(a, a).Should().BeFalse();  // 자기 자신은 후손 아님
        sut.IsDescendantOf(x, a).Should().BeFalse();  // 무관
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~GroupRepositoryNestingTests.IsDescendantOf"`
Expected: FAIL (컴파일 에러: `IsDescendantOf` 없음).

- [ ] **Step 3: 인터페이스에 선언 추가** — `IGroupRepository.cs`의 인터페이스 본문에 추가:

```csharp
    // nodeId가 ancestorId의 (직·간접) 후손이면 true. 사이클 안전(방문집합).
    bool IsDescendantOf(int nodeId, int ancestorId);
```

- [ ] **Step 4: 구현 추가** — `GroupRepository.cs`에 메서드 추가(클래스 내부, `GetAll` 아래):

```csharp
    public bool IsDescendantOf(int nodeId, int ancestorId)
    {
        // nodeId에서 부모 체인을 따라 올라가며 ancestorId를 만나면 후손.
        var parents = ParentMap();
        var visited = new HashSet<int>();
        var current = nodeId;
        while (parents.TryGetValue(current, out var parent) && parent is int p)
        {
            if (!visited.Add(current)) break;   // 사이클 방어
            if (p == ancestorId) return true;
            current = p;
        }
        return false;
    }

    // 모든 그룹의 id -> parent_id 맵(한 번 조회).
    private Dictionary<int, int?> ParentMap()
    {
        using var conn = _factory.Open();
        var rows = conn.Query<(int Id, int? ParentId)>("SELECT id AS Id, parent_id AS ParentId FROM groups;");
        var map = new Dictionary<int, int?>();
        foreach (var r in rows) map[r.Id] = r.ParentId;
        return map;
    }
```

(파일 상단에 `using System.Collections.Generic;`, `using System.Linq;`, `using Dapper;`가 이미 있는지 확인 — Dapper는 있음. `System.Linq`/`Generic`는 필요 시 추가.)

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~GroupRepositoryNestingTests.IsDescendantOf"`
Expected: PASS.

- [ ] **Step 6: 커밋**

```bash
git add src/Memoria.Core/Data/IGroupRepository.cs src/Memoria.Core/Data/GroupRepository.cs tests/Memoria.Tests/Data/GroupRepositoryNestingTests.cs
git commit -m "feat(core): IGroupRepository.IsDescendantOf (cycle-safe ancestor check)"
```

### Task N1.2: `SetParent` (백스톱: 자기/후손/시스템 거부 + 목적지 형제 재번호)

**Files:**
- Modify: `src/Memoria.Core/Data/IGroupRepository.cs`, `src/Memoria.Core/Data/GroupRepository.cs`
- Test: `tests/Memoria.Tests/Data/GroupRepositoryNestingTests.cs`

**Interfaces:**
- Consumes: `IsDescendantOf` (N1.1).
- Produces:
  - `void IGroupRepository.SetParent(int groupId, int? parentId)` — 무효(자기 자신, 후손을 부모로, 시스템 그룹을 부모로, 시스템 그룹을 이동)면 **no-op**. 유효하면 `parent_id` 변경 + 이동 그룹을 목적지 형제 **끝**에 붙이고 그 형제 집합 sort_order 0..n 재번호.
  - `void IGroupRepository.ReorderSiblings(int? parentId, IReadOnlyList<int> orderedGroupIds)` — 주어진 순서대로 sort_order 0..n.

- [ ] **Step 1: 실패 테스트 작성** — 같은 파일에 추가:

```csharp
    [Fact]
    public void SetParent_RejectsSelf_Descendant_System()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);
        var a = sut.Create(new Group { Name = "A" });
        var b = sut.Create(new Group { Name = "B", ParentId = a });
        var sysId = sut.GetAll().First(g => g.IsSystem).Id;

        sut.SetParent(a, a);        // 자기 자신 → no-op
        sut.Get(a)!.ParentId.Should().BeNull();

        sut.SetParent(a, b);        // 후손(B)을 부모로 → no-op(사이클)
        sut.Get(a)!.ParentId.Should().BeNull();

        sut.SetParent(b, sysId);    // 시스템을 부모로 → no-op
        sut.Get(b)!.ParentId.Should().Be(a);

        sut.SetParent(sysId, a);    // 시스템 이동 → no-op
        sut.Get(sysId)!.ParentId.Should().BeNull();
    }

    [Fact]
    public void SetParent_MovesUnderNewParent_AndRenumbersSiblings()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);
        var p = sut.Create(new Group { Name = "P" });
        var c1 = sut.Create(new Group { Name = "C1", ParentId = p, SortOrder = 0 });
        var c2 = sut.Create(new Group { Name = "C2", ParentId = p, SortOrder = 1 });
        var x = sut.Create(new Group { Name = "X" });

        sut.SetParent(x, p);        // X를 P 하위 끝으로

        sut.Get(x)!.ParentId.Should().Be(p);
        var siblings = sut.GetAll().Where(g => g.ParentId == p).OrderBy(g => g.SortOrder).ToList();
        siblings.Select(g => g.Id).Should().ContainInOrder(c1, c2, x);
        siblings.Select(g => g.SortOrder).Should().ContainInOrder(0, 1, 2); // 0..n 재번호
    }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~GroupRepositoryNestingTests.SetParent"`
Expected: FAIL (`SetParent`/`ReorderSiblings` 없음).

- [ ] **Step 3: 인터페이스 선언 추가** — `IGroupRepository.cs`:

```csharp
    void SetParent(int groupId, int? parentId);
    void ReorderSiblings(int? parentId, IReadOnlyList<int> orderedGroupIds);
```

- [ ] **Step 4: 구현 추가** — `GroupRepository.cs`:

```csharp
    public void SetParent(int groupId, int? parentId)
    {
        var self = Get(groupId);
        if (self is null || self.IsSystem) return;                       // 없는/시스템 그룹 이동 금지
        if (parentId is int pid)
        {
            if (pid == groupId) return;                                  // 자기 자신
            var parent = Get(pid);
            if (parent is null || parent.IsSystem) return;               // 시스템 부모 금지
            if (IsDescendantOf(pid, groupId)) return;                    // 후손을 부모로 → 사이클
        }

        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            using var tx = conn.BeginTransaction();
            // 목적지 형제(자기 제외)를 sort_order 순으로 + 자기를 끝에 붙여 재번호.
            var siblings = conn.Query<int>(
                "SELECT id FROM groups WHERE " +
                (parentId is null ? "parent_id IS NULL" : "parent_id = @parentId") +
                " AND id <> @groupId ORDER BY sort_order, id;",
                new { parentId, groupId }, tx).ToList();
            siblings.Add(groupId);
            conn.Execute("UPDATE groups SET parent_id = @parentId WHERE id = @groupId;",
                new { parentId, groupId }, tx);
            for (var i = 0; i < siblings.Count; i++)
                conn.Execute("UPDATE groups SET sort_order = @i WHERE id = @id;",
                    new { i, id = siblings[i] }, tx);
            tx.Commit();
        }
    }

    public void ReorderSiblings(int? parentId, IReadOnlyList<int> orderedGroupIds)
    {
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            using var tx = conn.BeginTransaction();
            for (var i = 0; i < orderedGroupIds.Count; i++)
                conn.Execute("UPDATE groups SET sort_order = @i, parent_id = @parentId WHERE id = @id;",
                    new { i, parentId, id = orderedGroupIds[i] }, tx);
            tx.Commit();
        }
    }
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~GroupRepositoryNestingTests.SetParent"`
Expected: PASS.

- [ ] **Step 6: 커밋**

```bash
git add src/Memoria.Core/Data/IGroupRepository.cs src/Memoria.Core/Data/GroupRepository.cs tests/Memoria.Tests/Data/GroupRepositoryNestingTests.cs
git commit -m "feat(core): GroupRepository.SetParent (backstop + sibling renumber) + ReorderSiblings"
```

### Task N1.3: `Delete` 승격(자식을 조부모로) + 목적지 형제 재번호 (트랜잭션)

**Files:**
- Modify: `src/Memoria.Core/Data/GroupRepository.cs` (`Delete`)
- Test: `tests/Memoria.Tests/Data/GroupRepositoryNestingTests.cs`

**Interfaces:**
- Produces: `Delete(int id)` 동작 변경 — 자식 그룹 `parent_id`를 삭제 그룹의 부모로 승격, 목적지 형제 재번호, 행 삭제. 노트는 기존 `ON DELETE SET NULL`. 시그니처 불변.

- [ ] **Step 1: 실패 테스트 작성:**

```csharp
    [Fact]
    public void Delete_PromotesChildrenToGrandparent_AndRoot()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);
        var gp = sut.Create(new Group { Name = "GP" });
        var p  = sut.Create(new Group { Name = "P",  ParentId = gp });
        var c1 = sut.Create(new Group { Name = "C1", ParentId = p });
        var c2 = sut.Create(new Group { Name = "C2", ParentId = p });

        sut.Delete(p);                                   // P 삭제 → C1,C2가 GP로 승격

        sut.Get(p).Should().BeNull();
        sut.Get(c1)!.ParentId.Should().Be(gp);
        sut.Get(c2)!.ParentId.Should().Be(gp);

        var root = sut.Create(new Group { Name = "Root" });
        var rc = sut.Create(new Group { Name = "RC", ParentId = root });
        sut.Delete(root);                                // 루트 삭제 → 자식이 루트(parent_id=null)
        sut.Get(rc)!.ParentId.Should().BeNull();
    }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~GroupRepositoryNestingTests.Delete_Promotes"`
Expected: FAIL (현재 Delete는 승격 안 함 → 자식 parent_id가 그대로 p이지만 p행이 삭제됨; FK가 ON이라 자식 parent_id가 삭제된 p를 가리키면 위반 → 실제로는 삭제 자체가 FK 예외로 실패할 수 있음. 어느 쪽이든 테스트 FAIL).

- [ ] **Step 3: `Delete` 구현 교체:**

```csharp
    public void Delete(int id)
    {
        var target = Get(id);
        if (target is null) return;
        var newParent = target.ParentId;   // 승격 목적지(조부모 또는 null=루트)
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            using var tx = conn.BeginTransaction();
            // 1) 자식 승격
            conn.Execute("UPDATE groups SET parent_id = @newParent WHERE parent_id = @id;",
                new { newParent, id }, tx);
            // 2) 행 삭제(노트 group_id는 ON DELETE SET NULL)
            conn.Execute("DELETE FROM groups WHERE id = @id;", new { id }, tx);
            // 3) 목적지 형제 재번호(승격된 자식 + 기존 형제)
            var siblings = conn.Query<int>(
                "SELECT id FROM groups WHERE " +
                (newParent is null ? "parent_id IS NULL" : "parent_id = @newParent") +
                " ORDER BY sort_order, id;", new { newParent }, tx).ToList();
            for (var i = 0; i < siblings.Count; i++)
                conn.Execute("UPDATE groups SET sort_order = @i WHERE id = @sid;",
                    new { i, sid = siblings[i] }, tx);
            tx.Commit();
        }
    }
```

- [ ] **Step 4: 통과 확인 + 기존 Delete 테스트 회귀**

Run: `dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~GroupRepository"`
Expected: PASS (신규 승격 + 기존 `Delete_SetsNoteGroupIdToNull`).

- [ ] **Step 5: 커밋**

```bash
git add src/Memoria.Core/Data/GroupRepository.cs tests/Memoria.Tests/Data/GroupRepositoryNestingTests.cs
git commit -m "feat(core): Delete promotes children to grandparent + renumbers siblings"
```

### Task N1.4: Fake 리포지토리 동기화(App 테스트용)

**Files:**
- Modify: `tests/Memoria.Tests/App/Fakes/FakeGroupRepository.cs` (신규 인터페이스 메서드 구현)

**Interfaces:**
- Produces: `FakeGroupRepository`가 `IsDescendantOf/SetParent/ReorderSiblings` 및 승격 `Delete`를 인메모리로 동일 구현(App VM 테스트가 사용).

- [ ] **Step 1: FakeGroupRepository에 메서드 추가**(실제 파일의 `Items`/컬렉션 필드명에 맞춰 조정). 예시(컬렉션이 `List<Group> Groups`라고 가정 — 실제 필드명 확인 후 사용):

```csharp
    public bool IsDescendantOf(int nodeId, int ancestorId)
    {
        var visited = new System.Collections.Generic.HashSet<int>();
        var cur = nodeId;
        while (true)
        {
            var g = Groups.FirstOrDefault(x => x.Id == cur);
            if (g?.ParentId is not int p) return false;
            if (!visited.Add(cur)) return false;
            if (p == ancestorId) return true;
            cur = p;
        }
    }

    public void SetParent(int groupId, int? parentId)
    {
        var self = Groups.FirstOrDefault(g => g.Id == groupId);
        if (self is null || self.IsSystem) return;
        if (parentId is int pid)
        {
            if (pid == groupId) return;
            var parent = Groups.FirstOrDefault(g => g.Id == pid);
            if (parent is null || parent.IsSystem) return;
            if (IsDescendantOf(pid, groupId)) return;
        }
        self.ParentId = parentId;
        Renumber(parentId);
    }

    public void ReorderSiblings(int? parentId, System.Collections.Generic.IReadOnlyList<int> orderedGroupIds)
    {
        for (var i = 0; i < orderedGroupIds.Count; i++)
        {
            var g = Groups.First(x => x.Id == orderedGroupIds[i]);
            g.ParentId = parentId; g.SortOrder = i;
        }
    }

    private void Renumber(int? parentId)
    {
        var sibs = Groups.Where(g => g.ParentId == parentId).OrderBy(g => g.SortOrder).ThenBy(g => g.Id).ToList();
        for (var i = 0; i < sibs.Count; i++) sibs[i].SortOrder = i;
    }
```

그리고 기존 `Delete`를 승격 버전으로 교체:

```csharp
    public void Delete(int id)
    {
        var t = Groups.FirstOrDefault(g => g.Id == id);
        if (t is null) return;
        var np = t.ParentId;
        foreach (var c in Groups.Where(g => g.ParentId == id)) c.ParentId = np;
        Groups.Remove(t);
        Renumber(np);
        // 노트 group_id=null 처리(있으면 노트 fake와 연동; 없으면 생략)
    }
```

- [ ] **Step 2: 빌드/전체 테스트**

Run: `dotnet.exe test "tests/Memoria.Tests" -c Release`
Expected: PASS(294+신규). 컴파일 에러 없으면 Fake가 인터페이스 충족.

- [ ] **Step 3: 커밋**

```bash
git add tests/Memoria.Tests/App/Fakes/FakeGroupRepository.cs
git commit -m "test: FakeGroupRepository supports nesting (SetParent/IsDescendantOf/ReorderSiblings/promote-delete)"
```

---

## N2 — GroupManagementViewModel: 하위 그룹 생성 / 재부모+위치

### Task N2.1: `AddSubGroup(parentId, name)`

**Files:**
- Modify: `src/Memoria.App/ViewModels/GroupManagementViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs` (없으면 create)

**Interfaces:**
- Consumes: `IGroupRepository.Create`.
- Produces: `void GroupManagementViewModel.AddSubGroup(int parentId, string name)` — 지정 부모 아래 그룹 생성(형제 끝 sort_order), `Load()` 갱신.

- [ ] **Step 1: 실패 테스트**

```csharp
    [Fact]
    public void AddSubGroup_CreatesUnderParent()
    {
        var repo = new FakeGroupRepository();
        var vm = new GroupManagementViewModel(repo, new FakeNoteRepository());
        var parent = repo.Create(new Group { Name = "부모" });

        vm.AddSubGroup(parent, "자식");

        repo.GetAll().Should().Contain(g => g.Name == "자식" && g.ParentId == parent);
    }
```

(정확한 네임스페이스/using·Fake 생성자 시그니처는 기존 `GroupManagementViewModelTests`/다른 VM 테스트를 참고해 맞춘다.)

- [ ] **Step 2: 실패 확인** — Run filter `~GroupManagementViewModelTests.AddSubGroup`. Expected FAIL.

- [ ] **Step 3: 구현** — `GroupManagementViewModel.cs`에 추가:

```csharp
    public void AddSubGroup(int parentId, string name)
    {
        var siblings = _groups.GetAll().Where(g => g.ParentId == parentId).ToList();
        var nextOrder = siblings.Count == 0 ? 0 : siblings.Max(g => g.SortOrder) + 1;
        var group = new Group
        {
            Name = name, ParentId = parentId, IsSystem = false,
            SortOrder = nextOrder, Color = DefaultGroupColor, CreatedAt = DateTimeOffset.UtcNow,
        };
        group.Id = _groups.Create(group);
        Load();
    }
```

- [ ] **Step 4: 통과 확인** — Run filter. Expected PASS.
- [ ] **Step 5: 커밋** — `git commit -m "feat(vm): GroupManagementViewModel.AddSubGroup"`

### Task N2.2: `MoveGroup(groupId, newParentId, siblingIndex)`

**Files:**
- Modify: `src/Memoria.App/ViewModels/GroupManagementViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/GroupManagementViewModelTests.cs`

**Interfaces:**
- Consumes: `IsDescendantOf`, `SetParent`, `ReorderSiblings`.
- Produces: `void MoveGroup(int groupId, int? newParentId, int siblingIndex)` — 유효성(자기/후손/시스템) 확인 후, 목적지 형제 목록에서 이동 노드를 제거→`siblingIndex`에 삽입한 순서로 `ReorderSiblings` 호출(부모도 함께 설정). 무효면 no-op. **기존 `MoveGroup(int fromIndex, int toIndex)`는 제거**(평면 드래그 경로 폐기 — 사용처는 N6에서 교체).

- [ ] **Step 1: 실패 테스트**

```csharp
    [Fact]
    public void MoveGroup_ReparentsAndInsertsAtIndex()
    {
        var repo = new FakeGroupRepository();
        var vm = new GroupManagementViewModel(repo, new FakeNoteRepository());
        var p = repo.Create(new Group { Name = "P" });
        var c1 = repo.Create(new Group { Name = "C1", ParentId = p, SortOrder = 0 });
        var c2 = repo.Create(new Group { Name = "C2", ParentId = p, SortOrder = 1 });
        var x = repo.Create(new Group { Name = "X" });

        vm.MoveGroup(x, p, 1);   // X를 P의 자식, 인덱스 1(C1과 C2 사이)

        var sibs = repo.GetAll().Where(g => g.ParentId == p).OrderBy(g => g.SortOrder).Select(g => g.Id).ToList();
        sibs.Should().ContainInOrder(c1, x, c2);
    }

    [Fact]
    public void MoveGroup_RejectsCycle()
    {
        var repo = new FakeGroupRepository();
        var vm = new GroupManagementViewModel(repo, new FakeNoteRepository());
        var a = repo.Create(new Group { Name = "A" });
        var b = repo.Create(new Group { Name = "B", ParentId = a });

        vm.MoveGroup(a, b, 0);   // A를 후손 B 아래로 → no-op

        repo.GetAll().First(g => g.Id == a).ParentId.Should().BeNull();
    }
```

- [ ] **Step 2: 실패 확인.**

- [ ] **Step 3: 구현** — `GroupManagementViewModel.cs`(기존 `MoveGroup(from,to)` 삭제 후):

```csharp
    public void MoveGroup(int groupId, int? newParentId, int siblingIndex)
    {
        var self = _groups.Get(groupId);
        if (self is null || self.IsSystem) return;
        if (newParentId is int pid)
        {
            if (pid == groupId || _groups.IsDescendantOf(pid, groupId)) return;
            var parent = _groups.Get(pid);
            if (parent is null || parent.IsSystem) return;
        }
        var siblings = _groups.GetAll()
            .Where(g => g.ParentId == newParentId && g.Id != groupId)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
            .Select(g => g.Id).ToList();
        var index = Math.Clamp(siblingIndex, 0, siblings.Count);
        siblings.Insert(index, groupId);
        _groups.ReorderSiblings(newParentId, siblings);
        Load();
    }
```

- [ ] **Step 4: 통과 확인.**
- [ ] **Step 5: 커밋** — `git commit -m "feat(vm): MoveGroup(groupId,newParentId,index) with cycle guard; drop flat MoveGroup"`

---

## N3 — 사이드바 VM: 트리 노드 / 스냅샷·복원

### Task N3.1: `SidebarNodeViewModel` 트리 필드

**Files:**
- Modify: `src/Memoria.App/ViewModels/SidebarNodeViewModel.cs`

**Interfaces:**
- Produces: `ObservableCollection<SidebarNodeViewModel> Children`, `[ObservableProperty] bool isExpanded`, `[ObservableProperty] bool isSelected`.

- [ ] **Step 1: 필드 추가**(테스트는 N3.2에서). 파일 교체:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Memoria.App.ViewModels;

public enum SidebarNodeKind { Group, Unclassified, System }

public sealed partial class SidebarNodeViewModel : ObservableObject
{
    public string Name { get; }
    public int? GroupId { get; }
    public SidebarNodeKind Kind { get; }
    public ObservableCollection<SidebarNodeViewModel> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public SidebarNodeViewModel(string name, int? groupId, SidebarNodeKind kind)
    {
        Name = name; GroupId = groupId; Kind = kind;
    }
}
```

- [ ] **Step 2: 빌드 확인** — `dotnet.exe build "Memoria.sln" -c Release`. Expected 0 errors.
- [ ] **Step 3: 커밋** — `git commit -m "feat(vm): SidebarNodeViewModel gains Children/IsExpanded/IsSelected"`

### Task N3.2: `MainViewModel.LoadGroups` 트리 구성 + 스냅샷/복원 + 삭제후 선택

**Files:**
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs` (`LoadGroups`)
- Test: `tests/Memoria.Tests/App/MainViewModelSidebarTests.cs`

**Interfaces:**
- Consumes: `IGroupRepository.GetAll`.
- Produces: `LoadGroups()` — `SidebarNodes` = 사용자 그룹 트리의 **루트 노드들 + 마지막에 (미분류) 루트**, 형제는 sort_order 순, 각 노드 `Children` 채움. `SystemNodes`는 기존대로. 재구성 전 펼침 GroupId·선택(GroupId/Kind) 스냅샷 → 후 복원(펼침 재적용, 선택 노드 `IsSelected`; 사라졌으면 부모 or (미분류)).

- [ ] **Step 1: 실패 테스트** — `MainViewModelSidebarTests.cs`에 추가:

```csharp
    [Fact]
    public void LoadGroups_BuildsTree_RootsAndChildren_PlusUnclassified()
    {
        var groups = new FakeGroupRepository();
        var p = groups.Create(new Group { Name = "부모", SortOrder = 0 });
        groups.Create(new Group { Name = "자식", ParentId = p, SortOrder = 0 });
        var vm = NewVm(groups, new FakeNoteRepository());

        vm.LoadGroups();

        var root = vm.SidebarNodes.First(n => n.Name == "부모");
        root.Children.Should().ContainSingle(c => c.Name == "자식");
        vm.SidebarNodes.Last().Kind.Should().Be(SidebarNodeKind.Unclassified);
        vm.SidebarNodes.Should().NotContain(n => n.Kind == SidebarNodeKind.System); // 시스템은 SystemNodes로
    }

    [Fact]
    public void LoadGroups_RestoresExpansion_ByGroupId()
    {
        var groups = new FakeGroupRepository();
        var p = groups.Create(new Group { Name = "부모", SortOrder = 0 });
        groups.Create(new Group { Name = "자식", ParentId = p, SortOrder = 0 });
        var vm = NewVm(groups, new FakeNoteRepository());
        vm.LoadGroups();
        vm.SidebarNodes.First(n => n.GroupId == p).IsExpanded = true;

        vm.LoadGroups(); // 재구성

        vm.SidebarNodes.First(n => n.GroupId == p).IsExpanded.Should().BeTrue(); // 펼침 유지
    }
```

(기존 `LoadGroups_puts_userGroups_then_unclassified_in_sidebar_and_systemGroups_separately` 테스트는 평면 전제 → 트리에서 루트만 SidebarNodes 최상위에 오도록 갱신 필요. 이 태스크에서 함께 수정.)

- [ ] **Step 2: 실패 확인.**

- [ ] **Step 3: 구현** — `LoadGroups()` 교체:

```csharp
    public void LoadGroups()
    {
        // 스냅샷: 펼친 그룹 + 선택
        var expanded = CollectExpanded(SidebarNodes);
        var prevGroupId = SelectedNode?.GroupId;
        var prevKind = SelectedNode?.Kind;

        SidebarNodes.Clear();
        SystemNodes.Clear();
        var groups = _groupRepo.GetAll();

        // 사용자 그룹 트리 구성(parent_id → children), 형제 sort_order 순.
        var userGroups = groups.Where(g => !g.IsSystem).OrderBy(g => g.SortOrder).ThenBy(g => g.Id).ToList();
        var nodeById = new Dictionary<int, SidebarNodeViewModel>();
        foreach (var g in userGroups)
            nodeById[g.Id] = new SidebarNodeViewModel(g.Name, g.Id, SidebarNodeKind.Group);
        foreach (var g in userGroups)
        {
            var node = nodeById[g.Id];
            if (g.ParentId is int pid && nodeById.TryGetValue(pid, out var parent))
                parent.Children.Add(node);
            else
                SidebarNodes.Add(node);   // 루트
        }
        // (미분류)를 트리 루트로 마지막에.
        SidebarNodes.Add(new SidebarNodeViewModel("(미분류)", null, SidebarNodeKind.Unclassified));

        foreach (var g in groups.Where(g => g.IsSystem))
            SystemNodes.Add(new SidebarNodeViewModel(g.Name, g.Id, SidebarNodeKind.System));

        // 복원: 펼침
        ApplyExpanded(SidebarNodes, expanded);
        // 복원: 선택
        var target = FindNode(SidebarNodes, prevGroupId, prevKind);
        if (target is null && prevKind == SidebarNodeKind.Group && prevGroupId is int gid)
        {
            // 삭제된 그룹 → 부모(있으면) 아니면 (미분류)
            var parentId = groups.FirstOrDefault(x => x.Id == gid)?.ParentId; // 삭제됐으면 null
            target = FindNode(SidebarNodes, parentId, SidebarNodeKind.Group)
                     ?? SidebarNodes.FirstOrDefault(n => n.Kind == SidebarNodeKind.Unclassified);
        }
        SelectedNode = target;   // OnSelectedNodeChanged → LoadNotes + (code-behind) IsSelected 동기화
    }

    private static HashSet<int> CollectExpanded(IEnumerable<SidebarNodeViewModel> nodes)
    {
        var set = new HashSet<int>();
        void Walk(SidebarNodeViewModel n)
        {
            if (n.IsExpanded && n.GroupId is int id) set.Add(id);
            foreach (var c in n.Children) Walk(c);
        }
        foreach (var n in nodes) Walk(n);
        return set;
    }

    private static void ApplyExpanded(IEnumerable<SidebarNodeViewModel> nodes, HashSet<int> expanded)
    {
        foreach (var n in nodes)
        {
            if (n.GroupId is int id && expanded.Contains(id)) n.IsExpanded = true;
            ApplyExpanded(n.Children, expanded);
        }
    }

    private static SidebarNodeViewModel? FindNode(IEnumerable<SidebarNodeViewModel> nodes, int? groupId, SidebarNodeKind? kind)
    {
        foreach (var n in nodes)
        {
            if (n.Kind == kind && n.GroupId == groupId) return n;
            var hit = FindNode(n.Children, groupId, kind);
            if (hit is not null) return hit;
        }
        return null;
    }
```

(주의: `SelectedNode` 세터가 이전 인스턴스 재선택과 충돌하지 않도록, 기존 selection-restore 로직을 이 트리 버전으로 완전히 대체한다. 기존 `SidebarNodes.Concat(SystemNodes).FirstOrDefault(...)` 복원 블록은 삭제.)

- [ ] **Step 4: 통과 확인 + 기존 사이드바 테스트 갱신**

Run: `dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~MainViewModelSidebarTests"`
Expected: PASS(신규 2 + 갱신된 기존).

- [ ] **Step 5: 커밋** — `git commit -m "feat(vm): LoadGroups builds nested tree + snapshot/restore expansion & selection"`

---

## N4 — 드롭 위치 계산(순수 함수, 테스트 대상)

### Task N4.1: `GroupDropCalculator`

**Files:**
- Create: `src/Memoria.App/ViewModels/GroupDropCalculator.cs`
- Test: `tests/Memoria.Tests/App/GroupDropCalculatorTests.cs`

**Interfaces:**
- Produces:
  - `enum DropZone { Before, Into, After }`
  - `static DropZone GroupDropCalculator.ZoneForOffset(double y, double rowHeight)` — y<25%→Before, y>75%→After, else Into.
  - `static (int? parentId, int index) GroupDropCalculator.Resolve(DropZone zone, int targetGroupId, int? targetParentId, int targetIndexAmongSiblings)` — Into→(targetGroupId, int.MaxValue=끝), Before→(targetParentId, targetIndex), After→(targetParentId, targetIndex+1).

- [ ] **Step 1: 실패 테스트** — `GroupDropCalculatorTests.cs`:

```csharp
using FluentAssertions;
using Memoria.App.ViewModels;
using Xunit;

namespace Memoria.Tests.App;

public class GroupDropCalculatorTests
{
    [Theory]
    [InlineData(2, 100, DropZone.Before)]
    [InlineData(50, 100, DropZone.Into)]
    [InlineData(90, 100, DropZone.After)]
    public void ZoneForOffset_MapsThreeZones(double y, double h, DropZone expected)
        => GroupDropCalculator.ZoneForOffset(y, h).Should().Be(expected);

    [Fact]
    public void Resolve_Into_TargetsGroupAsParent()
    {
        var (parent, index) = GroupDropCalculator.Resolve(DropZone.Into, targetGroupId: 5, targetParentId: 1, targetIndexAmongSiblings: 2);
        parent.Should().Be(5);
        index.Should().Be(int.MaxValue);
    }

    [Fact]
    public void Resolve_BeforeAfter_UseTargetParentAndIndex()
    {
        GroupDropCalculator.Resolve(DropZone.Before, 5, 1, 2).Should().Be(((int?)1, 2));
        GroupDropCalculator.Resolve(DropZone.After, 5, 1, 2).Should().Be(((int?)1, 3));
    }
}
```

- [ ] **Step 2: 실패 확인.**

- [ ] **Step 3: 구현** — `GroupDropCalculator.cs`:

```csharp
namespace Memoria.App.ViewModels;

public enum DropZone { Before, Into, After }

public static class GroupDropCalculator
{
    public static DropZone ZoneForOffset(double y, double rowHeight)
    {
        if (rowHeight <= 0) return DropZone.Into;
        var r = y / rowHeight;
        if (r < 0.25) return DropZone.Before;
        if (r > 0.75) return DropZone.After;
        return DropZone.Into;
    }

    // index=int.MaxValue → 목적지 형제 끝(MoveGroup에서 Clamp).
    public static (int? parentId, int index) Resolve(
        DropZone zone, int targetGroupId, int? targetParentId, int targetIndexAmongSiblings) => zone switch
    {
        DropZone.Into => (targetGroupId, int.MaxValue),
        DropZone.Before => (targetParentId, targetIndexAmongSiblings),
        _ => (targetParentId, targetIndexAmongSiblings + 1),
    };
}
```

- [ ] **Step 4: 통과 확인.**
- [ ] **Step 5: 커밋** — `git commit -m "feat(app): GroupDropCalculator (3-zone drop → parent/index) + tests"`

---

## N5 — TreeView UI + 다크 테마 템플릿

> N5는 XAML 중심으로 단위테스트 대상이 아니다. 각 태스크는 **빌드 성공 + 앱 실행 수동 확인**으로 게이트. 실행 중이면 먼저 `taskkill.exe /IM Memoria.exe /F`.

### Task N5.1: Base.xaml — TreeView/TreeViewItem 플랫 템플릿 + 트리용 텍스트 스타일

**Files:**
- Modify: `src/Memoria.App/Themes/Base.xaml`

**Interfaces:**
- Produces: 암시적 `TreeView`/`TreeViewItem` 스타일(행 전폭 선택=`Brush.ListItemSelected`, 호버=`Brush.ListItemHover`, 펼침 화살표=`Brush.Foreground`, `TreeView.Background=Transparent`), 키드 `ListItemTextTree`(선택 시 `Brush.ListItemSelectedForeground`, `AncestorType=TreeViewItem` 트리거, `TextTrimming=CharacterEllipsis`).

- [ ] **Step 1: Base.xaml에 스타일 추가**(`</ResourceDictionary>` 직전). 아래 XAML 삽입:

```xml
    <!-- 트리 항목 텍스트: 선택 시 전경색 전환(트리용) + 말줄임 -->
    <Style x:Key="ListItemTextTree" TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
        <Setter Property="TextTrimming" Value="CharacterEllipsis" />
        <Style.Triggers>
            <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=TreeViewItem}}" Value="True">
                <Setter Property="Foreground" Value="{DynamicResource Brush.ListItemSelectedForeground}" />
            </DataTrigger>
        </Style.Triggers>
    </Style>

    <Style TargetType="TreeView">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Foreground" Value="{DynamicResource Brush.Foreground}" />
    </Style>

    <Style TargetType="TreeViewItem">
        <Setter Property="Foreground" Value="{DynamicResource Brush.Foreground}" />
        <Setter Property="IsExpanded" Value="{Binding IsExpanded, Mode=TwoWay}" />
        <Setter Property="IsSelected" Value="{Binding IsSelected, Mode=TwoWay}" />
        <Setter Property="Padding" Value="2,3" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TreeViewItem">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                        </Grid.RowDefinitions>
                        <!-- 행 전폭 선택 배경 -->
                        <Border x:Name="bd" Grid.Row="0" Grid.Column="0" Grid.ColumnSpan="2"
                                Background="Transparent" CornerRadius="3" />
                        <!-- 펼침 화살표 -->
                        <ToggleButton x:Name="Expander" Grid.Row="0" Grid.Column="0"
                                      Focusable="False" Width="16"
                                      IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}}"
                                      ClickMode="Press">
                            <ToggleButton.Template>
                                <ControlTemplate TargetType="ToggleButton">
                                    <Border Background="Transparent" Width="16" Height="16">
                                        <Path x:Name="arrow" Data="M 4 2 L 8 6 L 4 10 Z"
                                              Fill="{DynamicResource Brush.Foreground}"
                                              HorizontalAlignment="Center" VerticalAlignment="Center" />
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property="IsChecked" Value="True">
                                            <Setter TargetName="arrow" Property="RenderTransform">
                                                <Setter.Value>
                                                    <RotateTransform Angle="90" CenterX="6" CenterY="6" />
                                                </Setter.Value>
                                            </Setter>
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </ToggleButton.Template>
                        </ToggleButton>
                        <!-- 헤더(항목 콘텐츠) -->
                        <Border Grid.Row="0" Grid.Column="1" Background="Transparent">
                            <ContentPresenter ContentSource="Header" VerticalAlignment="Center" />
                        </Border>
                        <!-- 자식 -->
                        <ItemsPresenter x:Name="ItemsHost" Grid.Row="1" Grid.Column="1" />
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsExpanded" Value="False">
                            <Setter TargetName="ItemsHost" Property="Visibility" Value="Collapsed" />
                        </Trigger>
                        <Trigger Property="HasItems" Value="False">
                            <Setter TargetName="Expander" Property="Visibility" Value="Hidden" />
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="bd" Property="Background" Value="{DynamicResource Brush.ListItemHover}" />
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="bd" Property="Background" Value="{DynamicResource Brush.ListItemSelected}" />
                            <Setter Property="Foreground" Value="{DynamicResource Brush.ListItemSelectedForeground}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

- [ ] **Step 2: 빌드** — `dotnet.exe build "Memoria.sln" -c Release`. Expected 0 errors(XAML 파싱).
- [ ] **Step 3: 커밋** — `git commit -m "feat(theme): flat TreeView/TreeViewItem styles (full-row selection, themed expander) for dark mode"`

### Task N5.2: MainWindow.xaml — 사용자 그룹을 TreeView로 교체 + 컨텍스트 메뉴

**Files:**
- Modify: `src/Memoria.App/MainWindow.xaml`

**Interfaces:**
- Consumes: `SidebarNodes`(트리 루트), `SidebarNodeViewModel.Children`.
- Produces: `x:Name="GroupTree"`(TreeView, `HierarchicalDataTemplate`), 헤더에 메모 드롭/컨텍스트 메뉴(새 그룹/새 하위 그룹/이름변경/색상/삭제/최상위로 이동), 이벤트 훅(`SelectedItemChanged`, `PreviewMouseLeftButtonDown`, `PreviewMouseMove`, `Drop`, `DragOver`).

- [ ] **Step 1: 사이드바 사용자 그룹 `ListBox`(`GroupListBox`) 블록을 TreeView로 교체.** 현재 `<ListBox x:Name="GroupListBox" ...>...</ListBox>` 전체를 아래로 치환:

```xml
                    <TreeView x:Name="GroupTree" Grid.Row="0"
                              Background="Transparent" BorderThickness="0"
                              ItemsSource="{Binding SidebarNodes}"
                              SelectedItemChanged="GroupTree_SelectedItemChanged"
                              AllowDrop="True"
                              DragOver="GroupTree_DragOver"
                              Drop="GroupTree_Drop"
                              PreviewMouseLeftButtonDown="List_PreviewMouseLeftButtonDown"
                              PreviewMouseMove="GroupTree_PreviewMouseMove">
                        <TreeView.ItemTemplate>
                            <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                                <Border Padding="0" Background="Transparent"
                                        AllowDrop="True" Drop="GroupNode_DropNote"
                                        ContextMenuOpening="SidebarItem_ContextMenuOpening"
                                        HorizontalAlignment="Stretch">
                                    <TextBlock Text="{Binding Name}" Style="{StaticResource ListItemTextTree}" />
                                    <Border.ContextMenu>
                                        <ContextMenu>
                                            <MenuItem Header="새 그룹" Click="OnAddGroupMenuItemClick"/>
                                            <MenuItem Header="새 하위 그룹" Tag="Sub" Click="OnAddSubGroupMenuItemClick"/>
                                            <Separator/>
                                            <MenuItem Header="이름변경" Tag="Rename" Click="OnRenameGroupMenuItemClick"/>
                                            <MenuItem Header="색상 변경" Click="OnSetColorMenuItemClick"/>
                                            <MenuItem Header="최상위로 이동" Tag="ToRoot" Click="OnMoveToRootMenuItemClick"/>
                                            <Separator/>
                                            <MenuItem Header="삭제" Tag="Delete" Click="OnDeleteGroupMenuItemClick"/>
                                        </ContextMenu>
                                    </Border.ContextMenu>
                                </Border>
                            </HierarchicalDataTemplate>
                        </TreeView.ItemTemplate>
                    </TreeView>
```

(주의: `Grid.Row="0"`·부모 `Grid`·구분선·`SystemListBox`는 그대로 둔다. TreeView는 헤더 Border가 전폭이 되도록 `HorizontalContentAlignment`가 필요할 수 있음 — N5.1 TreeViewItem 템플릿이 `*` 컬럼으로 처리.)

- [ ] **Step 2: 빌드** — 0 errors. (핸들러 미구현이면 컴파일 에러 → N6에서 추가하므로, 이 태스크는 N6와 함께 커밋하거나 핸들러 스텁을 먼저 추가한다. **권장: N6 핸들러 스텁을 이 태스크 Step 3에서 code-behind에 빈 메서드로 추가 후 빌드**.)
- [ ] **Step 3: code-behind에 핸들러 스텁 추가**(빌드 통과용; 본문은 N6) — `MainWindow.xaml.cs`에:

```csharp
    private void GroupTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) { }
    private void GroupTree_DragOver(object sender, DragEventArgs e) { }
    private void GroupTree_Drop(object sender, DragEventArgs e) { }
    private void GroupTree_PreviewMouseMove(object sender, MouseEventArgs e) { }
    private void OnAddSubGroupMenuItemClick(object sender, RoutedEventArgs e) { }
    private void OnMoveToRootMenuItemClick(object sender, RoutedEventArgs e) { }
```

- [ ] **Step 4: 빌드 0 errors + 실행 수동 확인**(트리 렌더/펼침, 다크 테마 가독성). 실행 중 종료 후 빌드.
- [ ] **Step 5: 커밋** — `git commit -m "feat(ui): sidebar user groups as TreeView + context menu (stubs)"`

---

## N6 — 드래그(3존) / 선택 동기화 / 컨텍스트 메뉴 본문 (code-behind)

### Task N6.1: 선택 동기화(2표면) + 컨텍스트 메뉴 본문

**Files:**
- Modify: `src/Memoria.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `ViewModel.SelectedNode`, `GroupVm.AddSubGroup/RenameGroup/SetGroupColor/DeleteGroup/MoveGroup`.
- Produces: `GroupTree_SelectedItemChanged`(사용자 선택→VM), `OnAddSubGroupMenuItemClick`, `OnMoveToRootMenuItemClick` 본문; 기존 `SyncSidebarSelection`을 TreeView(IsSelected 기반) + SystemListBox 2표면으로.

- [ ] **Step 1: 선택 동기화 교체.** 기존 `GroupListBox_SelectionChanged`/`SyncSidebarSelection`을 TreeView 버전으로. `GroupTree_SelectedItemChanged` 본문:

```csharp
    private void GroupTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingSelection) return;
        if (e.NewValue is SidebarNodeViewModel node)
        {
            _syncingSelection = true;
            SystemListBox.SelectedItem = null;
            _syncingSelection = false;
            ViewModel.SelectedNode = node;
        }
    }
```

`SyncSidebarSelection`은 TreeView에선 VM `IsSelected`로 구동되므로(ItemContainerStyle 양방향), 프로그램적 선택은 `ViewModel.SelectedNode` 설정 시 노드 `IsSelected=true`로 처리한다. `OnViewModelPropertyChanged`(SelectedNode 변경)에서:

```csharp
    private void SyncSidebarSelection()
    {
        _syncingSelection = true;
        var n = ViewModel.SelectedNode;
        // 시스템 목록
        SystemListBox.SelectedItem = (n is { Kind: SidebarNodeKind.System }) ? n : null;
        // 트리: 대상 노드 IsSelected=true, 조상 펼침. 나머지는 TreeView가 단일 선택 유지.
        if (n is not null && n.Kind != SidebarNodeKind.System)
            SelectTreeNode(n);
        _syncingSelection = false;
    }

    private void SelectTreeNode(SidebarNodeViewModel target)
    {
        // 조상 펼침 후 대상 선택(트리 어디에 있든).
        void Walk(System.Collections.Generic.IEnumerable<SidebarNodeViewModel> nodes, System.Collections.Generic.List<SidebarNodeViewModel> path)
        {
            foreach (var n in nodes)
            {
                path.Add(n);
                if (ReferenceEquals(n, target))
                {
                    for (int i = 0; i < path.Count - 1; i++) path[i].IsExpanded = true;
                    target.IsSelected = true;
                    target.BringIntoViewRequested = true; // N7에서 실제 스크롤 훅(없으면 생략)
                    return;
                }
                Walk(n.Children, path);
                path.RemoveAt(path.Count - 1);
            }
        }
        Walk(ViewModel.SidebarNodes, new System.Collections.Generic.List<SidebarNodeViewModel>());
    }
```

(`BringIntoViewRequested`는 선택적 — 없으면 그 줄 제거. ScrollIntoView는 N7에서 다룸.)

- [ ] **Step 2: 컨텍스트 메뉴 본문** — `SidebarItem_ContextMenuOpening`이 `GroupVm.SelectedGroup`을 노드로 설정하도록 유지(기존 로직 재사용, 노드는 `fe.DataContext`). 새 핸들러:

```csharp
    private void OnAddSubGroupMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (GroupVm.SelectedGroup is not { } g || g.IsSystem) return;
        var name = AskInput("새 하위 그룹 이름", "새 그룹");
        if (string.IsNullOrWhiteSpace(name)) return;
        GroupVm.AddSubGroup(g.Id, name.Trim());
        ViewModel.LoadGroups();
    }

    private void OnMoveToRootMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (GroupVm.SelectedGroup is not { } g || g.IsSystem || g.ParentId is null) return;
        GroupVm.MoveGroup(g.Id, null, int.MaxValue);
        ViewModel.LoadGroups();
    }
```

- [ ] **Step 3: 빌드 + 실행 수동**(선택 동기화, 새 하위 그룹, 최상위로 이동 동작).
- [ ] **Step 4: 커밋** — `git commit -m "feat(ui): tree selection sync + sub-group/move-to-root context menu"`

### Task N6.2: 3존 드래그 재부모 (포인터 히트테스트)

**Files:**
- Modify: `src/Memoria.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `GroupDropCalculator`, `GroupVm.MoveGroup`, `ViewModel.SidebarNodes`.
- Produces: `GroupTree_PreviewMouseMove`(그룹 드래그 시작, `groupId` DataObject, 버튼제외+임계값), `GroupTree_DragOver`(피드백), `GroupTree_Drop`(3존 계산→MoveGroup). 메모(`noteId`) 드롭은 기존 `GroupNode_DropNote` 유지.

- [ ] **Step 1: 드래그 시작 + 드롭 구현:**

```csharp
    private void GroupTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (IsOverButton(e.OriginalSource)) return;
        if (!ExceededDragThreshold(e)) return;
        if (FindDataContext<SidebarNodeViewModel>(e.OriginalSource) is not { Kind: SidebarNodeKind.Group } node) return;
        if (node.GroupId is not int groupId) return;
        DragDrop.DoDragDrop(GroupTree, new DataObject("groupId", groupId), DragDropEffects.Move);
    }

    private void GroupTree_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("groupId")) return; // 메모 드롭 등은 기본 처리
        e.Effects = ResolveGroupDrop(e, out _, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupTree_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("groupId")) return;
        if (!ResolveGroupDrop(e, out var newParentId, out var index)) return;
        var groupId = (int)e.Data.GetData("groupId");
        GroupVm.MoveGroup(groupId, newParentId, index);
        ViewModel.LoadGroups();
    }

    // 포인터 아래 TreeViewItem → 3존 → (parentId,index). 무효면 false.
    private bool ResolveGroupDrop(DragEventArgs e, out int? newParentId, out int index)
    {
        newParentId = null; index = 0;
        var groupId = (int)e.Data.GetData("groupId");
        var tvi = FindVisualAncestor<System.Windows.Controls.TreeViewItem>(
            GroupTree.InputHitTest(e.GetPosition(GroupTree)) as DependencyObject);
        if (tvi?.DataContext is not SidebarNodeViewModel target) return false;
        if (target.Kind != SidebarNodeKind.Group || target.GroupId is not int targetId) return false;
        if (targetId == groupId) return false;                       // 자기 자신
        if (GroupVm.RepoIsDescendantOf(targetId, groupId)) return false; // 후손 위로 = 사이클

        var pos = e.GetPosition(tvi);
        var zone = GroupDropCalculator.ZoneForOffset(pos.Y, tvi.ActualHeight);
        var targetGroup = GroupVm.Groups.FirstOrDefault(g => g.Id == targetId);
        var targetParentId = targetGroup?.ParentId;
        var siblings = GroupVm.Groups.Where(g => g.ParentId == targetParentId)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id).Select(g => g.Id).ToList();
        var targetIndex = siblings.IndexOf(targetId);
        (newParentId, index) = GroupDropCalculator.Resolve(zone, targetId, targetParentId, targetIndex);
        return true;
    }
```

`FindDataContext<T>`(원본에서 DataContext가 T인 조상 찾기)와 `GroupVm.RepoIsDescendantOf`(리포지토리 위임) 헬퍼를 추가:

```csharp
    private static T? FindDataContext<T>(object? source) where T : class
    {
        var d = source as DependencyObject;
        while (d is not null)
        {
            if (d is FrameworkElement fe && fe.DataContext is T t) return t;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }
```

`GroupManagementViewModel`에 노출: `public bool RepoIsDescendantOf(int nodeId, int ancestorId) => _groups.IsDescendantOf(nodeId, ancestorId);`

- [ ] **Step 2: 빌드 + 실행 수동**(그룹을 다른 그룹의 상/중/하로 드롭 → 형제앞/하위/형제뒤; 후손/자기 드롭 no-op; 메모 드롭 여전히 동작).
- [ ] **Step 3: 커밋** — `git commit -m "feat(ui): 3-zone drag reparent in group tree (pointer hit-test, cycle no-op)"`

---

## N7 — 애니메이션 (A~E)

> XAML/코드 애니메이션. 빌드 + 수동 확인 게이트. durations는 `Base.xaml`에 `Duration` 상수 리소스로 두어 일괄 조정 가능.

### Task N7.1: 펼침/접기 + 선택·호버 페이드 (C, D)

**Files:**
- Modify: `src/Memoria.App/Themes/Base.xaml` (TreeViewItem 템플릿에 트랜지션/스토리보드)

- [ ] **Step 1: TreeViewItem 템플릿의 `ItemsHost`(자식) 펼침에 Height/Opacity 애니메이션 적용.** `ControlTemplate.Triggers`의 `IsExpanded` 트리거를 EventTrigger/Storyboard로 보강(축소 시 Collapsed 유지). `bd` 배경 전환에 짧은 `ColorAnimation`(~120ms) — `Trigger.EnterActions/ExitActions`로 `Brush.ListItemHover`/`ListItemSelected` 페이드. (구현 시 `Border.Background`는 애니메이션 위해 `SolidColorBrush` 인스턴스로 두고 `Color`를 애니메이션.)

  구체 XAML(핵심 발췌 — `bd`를 애니메이션 가능한 브러시로):
```xml
                        <Border x:Name="bd" ... >
                            <Border.Background>
                                <SolidColorBrush x:Name="bdBrush" Color="Transparent" />
                            </Border.Background>
                        </Border>
```
  트리거 EnterActions 예(호버):
```xml
                        <Trigger Property="IsMouseOver" Value="True">
                            <Trigger.EnterActions>
                                <BeginStoryboard>
                                    <Storyboard>
                                        <ColorAnimation Storyboard.TargetName="bdBrush" Storyboard.TargetProperty="Color"
                                                        To="{Binding Color, Source={StaticResource _hoverColorProxy}}" Duration="0:0:0.12"/>
                                    </Storyboard>
                                </BeginStoryboard>
                            </Trigger.EnterActions>
                            ...
                        </Trigger>
```
  (색을 직접 `To="{DynamicResource ...}"`로 애니메이션할 수 없으므로, 각 팔레트에 `Color.ListItemHover`/`Color.ListItemSelected`(Color 리소스)를 추가하거나, 단순화를 위해 **Setter 방식(N5.1)을 유지하고 페이드는 생략**해도 된다. YAGNI: 페이드(D)가 복잡하면 C(펼침)만 구현.)

- [ ] **Step 2: 빌드 + 실행 수동**(펼침 시 자식 슬라이드, 선택/호버 부드러움).
- [ ] **Step 3: 커밋** — `git commit -m "feat(anim): expand/collapse slide (+optional selection fade) in tree"`

### Task N7.2: 드래그 고스트 어도너 + 드롭 표시자 (A, B)

**Files:**
- Create: `src/Memoria.App/Windows/DragAdorner.cs`
- Modify: `src/Memoria.App/MainWindow.xaml.cs` (드래그 중 어도너 표시/이동, DragOver에서 드롭 표시자)

**Interfaces:**
- Produces: `DragAdorner`(AdornerLayer에 반투명 미리보기), MainWindow 드래그 핸들러가 `GiveFeedback`/`DragOver`에서 위치·표시자 갱신.

- [ ] **Step 1: `DragAdorner` 구현**(반투명 비주얼 브러시 미리보기가 커서 오프셋 따라 이동):

```csharp
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Memoria.App.Windows;

public sealed class DragAdorner : Adorner
{
    private readonly Rectangle... // (VisualBrush로 드래그 요소 스냅샷)
    private Point _offset;
    public DragAdorner(UIElement adornedElement, UIElement dragVisual) : base(adornedElement) { /* VisualBrush 채움 */ }
    public void Update(Point position) { _offset = position; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc) { /* 반투명 그리기 */ }
    // 상세 구현은 표준 WPF DragAdorner 패턴을 따른다(Opacity ~0.7, IsHitTestVisible=false).
}
```

(전체 구현은 표준 패턴 — 이 태스크 실행 시 완성한다. 핵심: `AdornerLayer.GetAdornerLayer(GroupTree)`에 추가, `DragOver`에서 `Update(e.GetPosition(...))`, 드롭/종료 시 제거.)

- [ ] **Step 2: MainWindow 배선** — `GroupTree_PreviewMouseMove`에서 `DoDragDrop` 전 어도너 생성, `DragOver`에서 위치 갱신 + 3존 표시자(삽입선/하위 강조: `DragOver`의 `ResolveGroupDrop` 결과로 대상 TreeViewItem에 인접한 삽입선 어도너 or 강조 Border), 드롭/`QueryContinueDrag` 종료 시 어도너 제거.

- [ ] **Step 3: 빌드 + 실행 수동**(고스트가 커서 추종, 3존 표시자 정확, 무효 대상 not-allowed 커서).
- [ ] **Step 4: 커밋** — `git commit -m "feat(anim): drag ghost adorner + 3-zone drop indicator"`

### Task N7.3: 스프링로드 펼침 + 이동 fade-in + Undo 토스트 (B보강, E)

**Files:**
- Modify: `src/Memoria.App/MainWindow.xaml.cs` (스프링로드), `src/Memoria.App/MainWindow.xaml`(토스트/새 노드 fade-in)

- [ ] **Step 1: 스프링로드** — `GroupTree_DragOver`에서 접힌 대상 위 hover가 일정시간(예 700ms, `DispatcherTimer`) 지속되면 `target.IsExpanded=true`.
- [ ] **Step 2: Undo 토스트 slide-up+fade** — 기존 토스트 Border에 `RenderTransform`(TranslateTransform)+Opacity 스토리보드(Visibility 표시 시 EventTrigger). 새 노드 fade-in은 선택 사항(간단하면 TreeViewItem `Loaded`에 Opacity 0→1).
- [ ] **Step 3: 빌드 + 실행 수동.**
- [ ] **Step 4: 커밋** — `git commit -m "feat(anim): spring-loaded expand + undo toast slide/fade"`

---

## N8 — 통합 / 릴리스 준비

### Task N8.1: 전체 빌드·테스트·수동 체크리스트

- [ ] **Step 1: 전체 빌드/테스트**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null
dotnet.exe build "Memoria.sln" -c Release -v quiet
dotnet.exe test "tests/Memoria.Tests" -c Release
```
Expected: 0 warnings/errors, 모든 테스트 그린.

- [ ] **Step 2: 수동 체크리스트(Windows 실행)** — 하위 그룹 생성/펼침·접기 유지, 3존 드래그 재부모(사이클 no-op+커서), 최상위로 이동(메뉴), 삭제-승격 후 선택, 메모 드롭, 라이트/다크 렌더·선택 가독성·깊은 중첩 트리밍, 애니메이션(고스트/표시자/펼침/스프링로드/토스트).
- [ ] **Step 3: 스펙 대비 커버리지 자기점검**(§1~§6 각 요구 → 태스크 매핑 확인).

### Task N8.2: CHANGELOG + 버전 + 병합/릴리스

- [ ] **Step 1: `CHANGELOG.md`에 `## [0.2.0]` 섹션 추가**(Added: 그룹 중첩/하위 그룹/드래그 재부모/애니메이션; Changed: 사이드바 TreeView; 링크 refs). 커밋.
- [ ] **Step 2: master 병합**

```bash
git checkout master
git merge --no-ff feature/nested-groups -m "merge: nested groups (folder tree) — v0.2.0"
```
- [ ] **Step 3: 태그·푸시**(Windows git.exe)

```bash
git tag -a v0.2.0 -m "Memoria v0.2.0 — nested groups (folder tree) + animations"
git.exe -C 'C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria' push origin master
git.exe -C 'C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria' push origin v0.2.0
```
- [ ] **Step 4: 릴리스 확인** — release.yml이 v0.2.0에 Memoria.exe 첨부(state=uploaded) 폴링 확인.

---

## Self-Review 메모(작성자)
- 스펙 §4.1~4.4 도메인/데이터/VM/UI/애니메이션 → N1~N7 매핑됨. §5 안전(백스톱/no-op) → N1.2/N6.2. §6 테스트 → N1~N4 단위 + N8 수동.
- 미해결(§8): 3존 임계값(N4 25/50/25로 확정), API 형태(N1.2 SetParent+ReorderSiblings + N2.2 MoveGroup 확정), 스프링로드 지연(N7.3 700ms), 가상화(TreeViewItem 템플릿 기본 off로 단순).
- 타입 일관성: `SetParent(int,int?)`, `ReorderSiblings(int?,IReadOnlyList<int>)`, `IsDescendantOf(int nodeId,int ancestorId)`, `MoveGroup(int,int?,int)`, `AddSubGroup(int,string)`, `GroupDropCalculator.Resolve(DropZone,int,int?,int)` — 태스크 간 동일.
- 주의(구현자): N5.2는 핸들러 스텁을 먼저 넣어 빌드 통과 후 N6에서 본문. `FakeGroupRepository` 실제 컬렉션 필드명 확인. `BringIntoViewRequested`는 실제 속성 아니면 제거.

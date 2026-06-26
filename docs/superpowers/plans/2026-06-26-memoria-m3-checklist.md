# M3 — Checklist (Daily Work Log) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 체크리스트(=일일 업무일지) 메모의 항목 CRUD·체크/취소선·고객사 자동태깅+수동교정·log_date 편집·정렬을 담당하는 테스트 가능한 `ChecklistViewModel`(+`ChecklistItemViewModel`)과 얇은 WPF View를 구현한다.

**Architecture:** 모든 도메인 로직은 `Memoria.App.ViewModels`의 ViewModel에 두고 `CommunityToolkit.Mvvm`만 의존한다(WPF 타입 비의존). 저장은 M1의 `IChecklistRepository`/`INoteRepository`로 위임하고, 분류는 M1의 `ITaggingService.ApplyAutoTag`에 위임한다. 자동태깅은 텍스트 입력 후 디바운스 저장 시점(`FlushSaves()`)에만 적용하고, View(code-behind)는 디바운스 타이머와 바인딩만 가진 얇은 껍데기로 둔다. VM은 `Memoria.Tests`(net9.0-windows)에서 xUnit + FluentAssertions로 자동 검증한다.

**Tech Stack:** C# / .NET 9, WPF(`net9.0-windows`), `CommunityToolkit.Mvvm`(ObservableObject/`[ObservableProperty]`/`[RelayCommand]`), xUnit + FluentAssertions, 손수 작성한 in-memory Fake 리포지토리(Moq 미사용).

## Global Constraints
- 런타임: **.NET 9**.
- TFM: `Memoria.Core` = **net9.0**, `Memoria.App` = **net9.0-windows**, `Memoria.Tests` = **net9.0-windows**.
- DB 위치: `%LOCALAPPDATA%\Memoria\memoria.db` (M3는 직접 접근하지 않고 M1 리포지토리 경유).
- WPF는 **트리밍/단일파일 압축 금지**(`PublishTrimmed`/`EnableCompressionInSingleFile` 사용 안 함).
- 빌드/테스트는 **Windows .NET 9 SDK(`dotnet.exe`)** + **Windows 절대경로**로만 수행(WPF는 Linux dotnet 불가).
- 고객사 분류 우선순위: **자율형공장 > SLD** (M1 `ITaggingService`가 적용; M3는 결과만 표시/저장).
- **이슈(issue) 항목의 `ClientId`는 항상 NULL**(자동/수동 분류 대상 아님). 고객사 드롭다운은 `kind=task`에만 노출.
- 고객사 드롭다운 목록은 `IClientRepository.GetAll(enabledOnly: true)` 결과(표시순 `SortOrder`).
- 수동 교정 시 `IsManual = true`로 보호 → 이후 `ApplyAutoTag`가 덮어쓰지 않는다.
- `updated_at` 갱신 규칙: 콘텐츠 변경(항목 **추가/편집/삭제/체크**)은 부모 Note의 `UpdatedAt`을 갱신하고, **메타 조작(정렬/sort_order)은 갱신하지 않는다**.
- 신규 checklist 메모는 시스템 그룹 **`일일업무일지`**(M1 시드, `IsSystem=1`)에 배치.
- 모든 색상/브러시는 View에서 **계약 §10 `Brush.*` 키를 `DynamicResource`로만** 사용(임의 키 `Memoria.*`/`App.*` 금지, StaticResource 금지).
- ViewModel은 `CommunityToolkit.Mvvm`만 의존하고 code-behind는 얇게 유지한다.
- **셸 통합은 이 계획의 범위가 아니다.** MainWindow의 NoteType별 `ChecklistView` 호스팅(ContentControl+DataTemplate)과 툴바 `[+ 체크리스트]` 진입점(`MainViewModel.NewChecklistCommand` 본문, 계약 §9.3/§11)은 **M9에서 통합**된다. 이 계획은 `ChecklistView`/`ChecklistViewModel`/`ChecklistItemViewModel` 산출에 집중한다.

---

### Task 1: ChecklistItemViewModel (항목 래퍼 VM)

**Files:**
- Create: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\ViewModels\ChecklistItemViewModel.cs`
- Test: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests\ViewModels\ChecklistItemViewModelTests.cs`

**Interfaces:**
- Consumes (계약 §1): `Memoria.Core.Models.ChecklistItem`, `Memoria.Core.Models.ItemKind`.
- Produces: `Memoria.App.ViewModels.ChecklistItemViewModel` — 생성자 `ChecklistItemViewModel(ChecklistItem model)`, 속성 `Id`, `NoteId`, `Kind`, `IsTask`, `ShowCheckbox`, `Text`(observable), `Done`(observable), `ClientId`(int? observable), `IsManual`, `SortOrder`, `DoneAt`, `IsStruck`, `IsUnclassified`, `IsDirty`, 메서드 `ChecklistItem ToModel()`.

전제: M1/M2 산출물(`Memoria.App`, `Memoria.Tests` 프로젝트, `Memoria.App`의 `CommunityToolkit.Mvvm` 패키지 참조, `Memoria.Tests → Memoria.App` ProjectReference)이 이미 존재한다. 없으면 이 Task의 Step 3 전에 `Memoria.Tests.csproj`에 `<ProjectReference Include="..\..\src\Memoria.App\Memoria.App.csproj" />`와 `Memoria.App.csproj`에 `<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />`를 추가한다.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/ViewModels/ChecklistItemViewModelTests.cs
using System;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class ChecklistItemViewModelTests
{
    private static ChecklistItem TaskModel(string text = "SLD 작업", int? clientId = null) => new()
    {
        Id = 7,
        NoteId = 3,
        Kind = ItemKind.Task,
        Text = text,
        Done = false,
        ClientId = clientId,
        IsManual = false,
        SortOrder = 2,
        CreatedAt = new DateTimeOffset(2026, 6, 26, 9, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 6, 26, 9, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Constructor_copies_model_without_marking_dirty()
    {
        var vm = new ChecklistItemViewModel(TaskModel());

        vm.Id.Should().Be(7);
        vm.NoteId.Should().Be(3);
        vm.Kind.Should().Be(ItemKind.Task);
        vm.Text.Should().Be("SLD 작업");
        vm.SortOrder.Should().Be(2);
        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Task_shows_checkbox_issue_does_not()
    {
        new ChecklistItemViewModel(TaskModel()).ShowCheckbox.Should().BeTrue();

        var issue = TaskModel();
        issue.Kind = ItemKind.Issue;
        new ChecklistItemViewModel(issue).ShowCheckbox.Should().BeFalse();
    }

    [Fact]
    public void IsStruck_true_only_when_task_and_done()
    {
        var vm = new ChecklistItemViewModel(TaskModel());
        vm.IsStruck.Should().BeFalse();

        vm.Done = true;
        vm.IsStruck.Should().BeTrue();
    }

    [Fact]
    public void IsUnclassified_true_for_task_with_null_client()
    {
        var vm = new ChecklistItemViewModel(TaskModel(clientId: null));
        vm.IsUnclassified.Should().BeTrue();

        vm.ClientId = 5;
        vm.IsUnclassified.Should().BeFalse();
    }

    [Fact]
    public void Issue_is_never_unclassified_highlight()
    {
        var issue = TaskModel();
        issue.Kind = ItemKind.Issue;
        new ChecklistItemViewModel(issue).IsUnclassified.Should().BeFalse();
    }

    [Fact]
    public void Editing_text_marks_dirty()
    {
        var vm = new ChecklistItemViewModel(TaskModel());
        vm.Text = "코모텍 변경";
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ToModel_round_trips_all_fields()
    {
        var vm = new ChecklistItemViewModel(TaskModel());
        vm.Done = true;
        vm.DoneAt = new DateTimeOffset(2026, 6, 26, 10, 0, 0, TimeSpan.Zero);
        vm.ClientId = 9;

        var model = vm.ToModel();
        model.Id.Should().Be(7);
        model.NoteId.Should().Be(3);
        model.Kind.Should().Be(ItemKind.Task);
        model.Done.Should().BeTrue();
        model.DoneAt.Should().Be(new DateTimeOffset(2026, 6, 26, 10, 0, 0, TimeSpan.Zero));
        model.ClientId.Should().Be(9);
        model.SortOrder.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistItemViewModelTests"
```
예상 실패: `error CS0246: The type or namespace name 'ChecklistItemViewModel' could not be found` (컴파일 실패).

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/ViewModels/ChecklistItemViewModel.cs
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public partial class ChecklistItemViewModel : ObservableObject
{
    public int Id { get; set; }
    public int NoteId { get; }
    public ItemKind Kind { get; }

    public bool IsTask => Kind == ItemKind.Task;
    public bool ShowCheckbox => IsTask;

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStruck))]
    private bool _done;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnclassified))]
    private int? _clientId;

    public DateTimeOffset? DoneAt { get; set; }
    public bool IsManual { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsStruck => IsTask && Done;
    public bool IsUnclassified => IsTask && ClientId is null;

    /// 텍스트가 사용자 입력으로 바뀌었음을 표시(디바운스 FlushSaves 대상).
    public bool IsDirty { get; set; }

    public ChecklistItemViewModel(ChecklistItem model)
    {
        Id = model.Id;
        NoteId = model.NoteId;
        Kind = model.Kind;
        _text = model.Text;          // 필드 직접 대입 → OnTextChanged 미발생(생성 시 dirty 아님)
        _done = model.Done;
        _clientId = model.ClientId;
        DoneAt = model.DoneAt;
        IsManual = model.IsManual;
        SortOrder = model.SortOrder;
        CreatedAt = model.CreatedAt;
        UpdatedAt = model.UpdatedAt;
    }

    partial void OnTextChanged(string value) => IsDirty = true;

    public ChecklistItem ToModel() => new()
    {
        Id = Id,
        NoteId = NoteId,
        Kind = Kind,
        Text = Text,
        Done = Done,
        DoneAt = DoneAt,
        ClientId = ClientId,
        IsManual = IsManual,
        SortOrder = SortOrder,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistItemViewModelTests"
```
예상: `Passed!  - Failed: 0, Passed: 7`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/ChecklistItemViewModel.cs tests/Memoria.Tests/ViewModels/ChecklistItemViewModelTests.cs
git commit -m "feat(checklist): add ChecklistItemViewModel with strikethrough/unclassified state

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Test fakes + ChecklistViewModel.Load (항목/고객사 로드)

**Files:**
- Create: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests\Fakes\ChecklistFakes.cs`
- Create: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\ViewModels\ChecklistViewModel.cs`
- Test: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests\ViewModels\ChecklistViewModelTests.cs`

**Interfaces:**
- Consumes (계약 §4): `IChecklistRepository.GetByNote(int)`, `IClientRepository.GetAll(bool enabledOnly = false)`; (계약 §1) `Note`, `Client`, `ChecklistItem`, `ItemKind`.
- Consumes (계약 §5): `ITaggingService.ApplyAutoTag(ChecklistItem)`; (계약 §4) `INoteRepository.Update(Note)`, `IGroupRepository.GetAll()` (이후 Task에서 사용).
- Produces: `Memoria.App.ViewModels.ChecklistViewModel` — 생성자 `ChecklistViewModel(IChecklistRepository, IClientRepository, ITaggingService, INoteRepository, IGroupRepository)`, `ObservableCollection<ChecklistItemViewModel> Items`, `ObservableCollection<Client> AvailableClients`, `DateOnly LogDate`, `void Load(Note note)`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/Fakes/ChecklistFakes.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Services;

namespace Memoria.Tests.Fakes;

internal sealed class FakeChecklistRepository : IChecklistRepository
{
    public readonly List<ChecklistItem> Items = new();
    private int _seq = 100;

    public int AddItem(ChecklistItem item)
    {
        item.Id = ++_seq;
        Items.Add(Clone(item));
        return item.Id;
    }

    public void UpdateItem(ChecklistItem item)
    {
        var idx = Items.FindIndex(i => i.Id == item.Id);
        if (idx >= 0) Items[idx] = Clone(item);
    }

    public void DeleteItem(int id) => Items.RemoveAll(i => i.Id == id);

    public IReadOnlyList<ChecklistItem> GetByNote(int noteId) =>
        Items.Where(i => i.NoteId == noteId)
             .OrderBy(i => i.SortOrder)
             .Select(Clone)
             .ToList();

    private static ChecklistItem Clone(ChecklistItem i) => new()
    {
        Id = i.Id, NoteId = i.NoteId, Kind = i.Kind, Text = i.Text, Done = i.Done,
        DoneAt = i.DoneAt, ClientId = i.ClientId, IsManual = i.IsManual,
        SortOrder = i.SortOrder, CreatedAt = i.CreatedAt, UpdatedAt = i.UpdatedAt,
    };
}

internal sealed class FakeClientRepository : IClientRepository
{
    public readonly List<Client> Clients = new();

    public int Create(Client client) { client.Id = Clients.Count + 1; Clients.Add(client); return client.Id; }
    public void Update(Client client) { }
    public void Delete(int id) { }

    public IReadOnlyList<Client> GetAll(bool enabledOnly = false) =>
        Clients.Where(c => !enabledOnly || c.Enabled)
               .OrderBy(c => c.SortOrder)
               .ToList();

    public IReadOnlyList<ClientRule> GetRules() => new List<ClientRule>();
    public void ReplaceRules(int clientId, IEnumerable<ClientRule> rules) { }
}

internal sealed class FakeNoteRepository : INoteRepository
{
    public readonly List<Note> Notes = new();
    private int _seq = 0;

    public int Create(Note note) { note.Id = ++_seq; Notes.Add(note); return note.Id; }
    public void Update(Note note)
    {
        var idx = Notes.FindIndex(n => n.Id == note.Id);
        if (idx >= 0) Notes[idx] = note; else Notes.Add(note);
    }
    public void SoftDelete(int id) { }
    public void Restore(int id) { }
    public void Purge(int id) { }
    public void PurgeExpiredTrash(int retentionDays) { }
    public Note? Get(int id) => Notes.FirstOrDefault(n => n.Id == id);
    public IReadOnlyList<Note> GetByGroup(int? groupId) => new List<Note>();
    public IReadOnlyList<Note> GetTrash() => new List<Note>();
    public IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday) => new List<Note>();
    public Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format) => null;
}

internal sealed class FakeGroupRepository : IGroupRepository
{
    public readonly List<Group> Groups = new();
    public int Create(Group group) { group.Id = Groups.Count + 1; Groups.Add(group); return group.Id; }
    public void Update(Group group) { }
    public void Delete(int id) { }
    public Group? Get(int id) => Groups.FirstOrDefault(g => g.Id == id);
    public IReadOnlyList<Group> GetAll() => Groups.OrderBy(g => g.SortOrder).ToList();
}

/// 계약 §5 ITaggingService 의미를 모사: Task & !IsManual 일 때만 키워드로 ClientId 재계산.
internal sealed class FakeTaggingService : ITaggingService
{
    public readonly Dictionary<string, int> KeywordToClient = new(StringComparer.OrdinalIgnoreCase);

    public ChecklistItem ApplyAutoTag(ChecklistItem item)
    {
        if (item.Kind != ItemKind.Task || item.IsManual) return item;
        item.ClientId = null;
        foreach (var pair in KeywordToClient)
        {
            if (item.Text.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                item.ClientId = pair.Value;
                break;
            }
        }
        return item;
    }
}
```

```csharp
// tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs
using System;
using System.Linq;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.Fakes;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class ChecklistViewModelTests
{
    private readonly FakeChecklistRepository _checklist = new();
    private readonly FakeClientRepository _clients = new();
    private readonly FakeTaggingService _tagging = new();
    private readonly FakeNoteRepository _notes = new();
    private readonly FakeGroupRepository _groups = new();

    private ChecklistViewModel CreateSut() =>
        new(_checklist, _clients, _tagging, _notes, _groups);

    private Note SeedNote(int id = 1)
    {
        var note = new Note
        {
            Id = id,
            Type = NoteType.Checklist,
            LogDate = new DateOnly(2026, 6, 26),
            CreatedAt = new DateTimeOffset(2026, 6, 26, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 6, 26, 8, 0, 0, TimeSpan.Zero),
        };
        _notes.Notes.Add(note);
        return note;
    }

    [Fact]
    public void Load_reads_items_sorted_by_sort_order()
    {
        var note = SeedNote();
        _checklist.AddItem(new ChecklistItem { NoteId = 1, Kind = ItemKind.Task, Text = "B", SortOrder = 1 });
        _checklist.AddItem(new ChecklistItem { NoteId = 1, Kind = ItemKind.Issue, Text = "A", SortOrder = 0 });

        var sut = CreateSut();
        sut.Load(note);

        sut.Items.Select(i => i.Text).Should().ContainInOrder("A", "B");
        sut.LogDate.Should().Be(new DateOnly(2026, 6, 26));
    }

    [Fact]
    public void Load_populates_only_enabled_clients_in_display_order()
    {
        var note = SeedNote();
        _clients.Clients.Add(new Client { Id = 1, Name = "SLD", SortOrder = 0, Enabled = true });
        _clients.Clients.Add(new Client { Id = 2, Name = "비활성", SortOrder = 1, Enabled = false });
        _clients.Clients.Add(new Client { Id = 3, Name = "MTP", SortOrder = 2, Enabled = true });

        var sut = CreateSut();
        sut.Load(note);

        sut.AvailableClients.Select(c => c.Name).Should().ContainInOrder("SLD", "MTP");
        sut.AvailableClients.Should().HaveCount(2);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상 실패: `error CS0246: The type or namespace name 'ChecklistViewModel' could not be found`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/ViewModels/ChecklistViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Services;

namespace Memoria.App.ViewModels;

public partial class ChecklistViewModel : ObservableObject
{
    public const string DailyLogGroupName = "일일업무일지";

    private readonly IChecklistRepository _checklist;
    private readonly IClientRepository _clients;
    private readonly ITaggingService _tagging;
    private readonly INoteRepository _notes;
    private readonly IGroupRepository _groups;

    private Note? _note;

    public ObservableCollection<ChecklistItemViewModel> Items { get; } = new();
    public ObservableCollection<Client> AvailableClients { get; } = new();

    [ObservableProperty]
    private DateOnly _logDate;

    public ChecklistViewModel(
        IChecklistRepository checklist,
        IClientRepository clients,
        ITaggingService tagging,
        INoteRepository notes,
        IGroupRepository groups)
    {
        _checklist = checklist;
        _clients = clients;
        _tagging = tagging;
        _notes = notes;
        _groups = groups;
    }

    public void Load(Note note)
    {
        _note = note;
        _logDate = note.LogDate ?? DateOnly.FromDateTime(DateTime.Today);
        OnPropertyChanged(nameof(LogDate));

        AvailableClients.Clear();
        foreach (var client in _clients.GetAll(enabledOnly: true))
            AvailableClients.Add(client);

        Items.Clear();
        foreach (var item in _checklist.GetByNote(note.Id))
            Items.Add(new ChecklistItemViewModel(item));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```
git add tests/Memoria.Tests/Fakes/ChecklistFakes.cs src/Memoria.App/ViewModels/ChecklistViewModel.cs tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs
git commit -m "feat(checklist): load items and enabled clients into ChecklistViewModel

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: 항목 추가/삭제 + 부모 Note updated_at 갱신

**Files:**
- Modify: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\ViewModels\ChecklistViewModel.cs`
- Test: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests\ViewModels\ChecklistViewModelTests.cs`

**Interfaces:**
- Consumes: `IChecklistRepository.AddItem(ChecklistItem)`, `IChecklistRepository.DeleteItem(int)`, `INoteRepository.Update(Note)`.
- Produces: `ChecklistViewModel.AddTask()`(+ `AddTaskCommand`), `ChecklistViewModel.AddIssue()`(+ `AddIssueCommand`), `ChecklistViewModel.RemoveItem(ChecklistItemViewModel)`(+ `RemoveItemCommand`).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs (append inside class)
    [Fact]
    public void AddTask_creates_task_item_with_checkbox_and_persists()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);

        sut.AddTask();

        sut.Items.Should().HaveCount(1);
        sut.Items[0].Kind.Should().Be(ItemKind.Task);
        sut.Items[0].ShowCheckbox.Should().BeTrue();
        _checklist.Items.Should().ContainSingle(i => i.NoteId == 1 && i.Kind == ItemKind.Task);
    }

    [Fact]
    public void AddIssue_creates_issue_item_without_checkbox()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);

        sut.AddIssue();

        sut.Items[0].Kind.Should().Be(ItemKind.Issue);
        sut.Items[0].ShowCheckbox.Should().BeFalse();
    }

    [Fact]
    public void Added_items_get_increasing_sort_order()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);

        sut.AddTask();
        sut.AddTask();

        sut.Items[0].SortOrder.Should().Be(0);
        sut.Items[1].SortOrder.Should().Be(1);
    }

    [Fact]
    public void RemoveItem_deletes_from_collection_and_repository()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var item = sut.Items[0];

        sut.RemoveItem(item);

        sut.Items.Should().BeEmpty();
        _checklist.Items.Should().NotContain(i => i.Id == item.Id);
    }

    [Fact]
    public void AddItem_bumps_parent_note_updated_at()
    {
        var note = SeedNote();
        var before = note.UpdatedAt;
        var sut = CreateSut();
        sut.Load(note);

        sut.AddTask();

        _notes.Get(1)!.UpdatedAt.Should().BeAfter(before);
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상 실패: `error CS1061: 'ChecklistViewModel' does not contain a definition for 'AddTask'`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/ViewModels/ChecklistViewModel.cs (add members)

    [RelayCommand]
    public void AddTask() => AddItem(ItemKind.Task);

    [RelayCommand]
    public void AddIssue() => AddItem(ItemKind.Issue);

    private ChecklistItemViewModel AddItem(ItemKind kind)
    {
        var now = DateTimeOffset.UtcNow;
        var model = new ChecklistItem
        {
            NoteId = _note!.Id,
            Kind = kind,
            Text = "",
            Done = false,
            DoneAt = null,
            ClientId = null,
            IsManual = false,
            SortOrder = NextSortOrder(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        model.Id = _checklist.AddItem(model);

        var vm = new ChecklistItemViewModel(model);
        Items.Add(vm);
        TouchNote();
        return vm;
    }

    [RelayCommand]
    public void RemoveItem(ChecklistItemViewModel item)
    {
        _checklist.DeleteItem(item.Id);
        Items.Remove(item);
        TouchNote();
    }

    private int NextSortOrder() => Items.Count == 0 ? 0 : Items.Max(i => i.SortOrder) + 1;

    /// 콘텐츠 변경 시 부모 Note의 UpdatedAt 갱신(메타 조작 제외).
    private void TouchNote()
    {
        if (_note is null) return;
        _note.UpdatedAt = DateTimeOffset.UtcNow;
        _notes.Update(_note);
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상: `Passed!  - Failed: 0, Passed: 7`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/ChecklistViewModel.cs tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs
git commit -m "feat(checklist): add/remove items and bump parent note updated_at

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: done 토글 (취소선 + done_at + 즉시 저장)

**Files:**
- Modify: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\ViewModels\ChecklistViewModel.cs`
- Test: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests\ViewModels\ChecklistViewModelTests.cs`

**Interfaces:**
- Consumes: `IChecklistRepository.UpdateItem(ChecklistItem)`, `INoteRepository.Update(Note)`.
- Produces: `ChecklistViewModel.ToggleDone(ChecklistItemViewModel)`(+ `ToggleDoneCommand`).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs (append inside class)
    [Fact]
    public void ToggleDone_sets_done_strikethrough_and_done_at()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var item = sut.Items[0];

        sut.ToggleDone(item);

        item.Done.Should().BeTrue();
        item.IsStruck.Should().BeTrue();
        item.DoneAt.Should().NotBeNull();
        _checklist.Items.Single(i => i.Id == item.Id).Done.Should().BeTrue();
    }

    [Fact]
    public void ToggleDone_twice_clears_done_and_done_at()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var item = sut.Items[0];

        sut.ToggleDone(item);
        sut.ToggleDone(item);

        item.Done.Should().BeFalse();
        item.DoneAt.Should().BeNull();
    }

    [Fact]
    public void ToggleDone_ignores_issue_items()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddIssue();
        var item = sut.Items[0];

        sut.ToggleDone(item);

        item.Done.Should().BeFalse();
        item.DoneAt.Should().BeNull();
    }

    [Fact]
    public void ToggleDone_bumps_parent_note_updated_at()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var before = _notes.Get(1)!.UpdatedAt;
        var item = sut.Items[0];

        sut.ToggleDone(item);

        _notes.Get(1)!.UpdatedAt.Should().BeOnOrAfter(before);
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상 실패: `error CS1061: 'ChecklistViewModel' does not contain a definition for 'ToggleDone'`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/ViewModels/ChecklistViewModel.cs (add member)

    [RelayCommand]
    public void ToggleDone(ChecklistItemViewModel item)
    {
        if (!item.IsTask) return;

        item.Done = !item.Done;
        item.DoneAt = item.Done ? DateTimeOffset.UtcNow : null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        _checklist.UpdateItem(item.ToModel());
        TouchNote();
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상: `Passed!  - Failed: 0, Passed: 11`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/ChecklistViewModel.cs tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs
git commit -m "feat(checklist): toggle done with strikethrough and done_at

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: 디바운스 저장 시 자동태깅(FlushSaves) + 수동보호

**Files:**
- Modify: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\ViewModels\ChecklistViewModel.cs`
- Test: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests\ViewModels\ChecklistViewModelTests.cs`

**Interfaces:**
- Consumes: `ITaggingService.ApplyAutoTag(ChecklistItem)`, `IChecklistRepository.UpdateItem(ChecklistItem)`, `INoteRepository.Update(Note)`.
- Produces: `ChecklistViewModel.FlushSaves()` — dirty 항목에 한해 `ApplyAutoTag` 적용 후 영속화. View의 디바운스 타이머 tick에서 호출.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs (append inside class)
    [Fact]
    public void FlushSaves_applies_auto_tag_to_dirty_task()
    {
        _tagging.KeywordToClient["SLD"] = 6;
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var item = sut.Items[0];

        item.Text = "SLD 자율형공장 정리";   // dirty
        sut.FlushSaves();

        item.ClientId.Should().Be(6);
        item.IsDirty.Should().BeFalse();
        _checklist.Items.Single(i => i.Id == item.Id).ClientId.Should().Be(6);
    }

    [Fact]
    public void FlushSaves_does_not_touch_non_dirty_items()
    {
        _tagging.KeywordToClient["SLD"] = 6;
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var item = sut.Items[0];
        // 텍스트를 직접 만들었지만 FlushSaves 전에 dirty 해제 상황을 모사: 한번 flush
        item.Text = "SLD";
        sut.FlushSaves();
        item.IsDirty.Should().BeFalse();

        // 키워드 맵을 바꿔도, 다시 dirty 되지 않았으면 재태깅하지 않음
        _tagging.KeywordToClient.Clear();
        _tagging.KeywordToClient["MTP"] = 2;
        sut.FlushSaves();

        item.ClientId.Should().Be(6);   // 그대로 유지(재계산 안 함)
    }

    [Fact]
    public void FlushSaves_respects_manual_protection()
    {
        _tagging.KeywordToClient["SLD"] = 6;
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var item = sut.Items[0];
        item.IsManual = true;
        item.ClientId = 99;

        item.Text = "SLD";   // dirty
        sut.FlushSaves();

        item.ClientId.Should().Be(99);  // 수동보호: 자동태깅이 덮지 않음
    }

    [Fact]
    public void FlushSaves_keeps_issue_client_null()
    {
        _tagging.KeywordToClient["SLD"] = 6;
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddIssue();
        var item = sut.Items[0];

        item.Text = "SLD 관련 이슈";
        sut.FlushSaves();

        item.ClientId.Should().BeNull();
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상 실패: `error CS1061: 'ChecklistViewModel' does not contain a definition for 'FlushSaves'`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/ViewModels/ChecklistViewModel.cs (add member)

    /// 디바운스 저장 시점에 호출. dirty(텍스트 변경) 항목만 자동태깅 적용 후 영속화한다.
    /// ApplyAutoTag는 Task & !IsManual 일 때만 ClientId를 재계산한다(Issue/수동보호 항목은 보존).
    public void FlushSaves()
    {
        var dirty = Items.Where(i => i.IsDirty).ToList();
        if (dirty.Count == 0) return;

        foreach (var item in dirty)
        {
            var tagged = _tagging.ApplyAutoTag(item.ToModel());
            item.ClientId = tagged.ClientId;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            _checklist.UpdateItem(item.ToModel());
            item.IsDirty = false;
        }
        TouchNote();
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상: `Passed!  - Failed: 0, Passed: 15`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/ChecklistViewModel.cs tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs
git commit -m "feat(checklist): debounced auto-tagging with manual protection (FlushSaves)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: 고객사 수동 교정 (IsManual=1 + 즉시 저장 + 강조 해제)

**Files:**
- Modify: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\ViewModels\ChecklistViewModel.cs`
- Test: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests\ViewModels\ChecklistViewModelTests.cs`

**Interfaces:**
- Consumes: `IChecklistRepository.UpdateItem(ChecklistItem)`, `INoteRepository.Update(Note)`.
- Produces: `ChecklistViewModel.CommitClient(ChecklistItemViewModel)`(+ `CommitClientCommand`). 드롭다운 two-way 바인딩으로 `item.ClientId`가 먼저 갱신된 뒤, SelectionChanged → 이 커맨드가 `IsManual=true` 설정 + 영속화한다.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs (append inside class)
    [Fact]
    public void CommitClient_marks_manual_and_persists()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var item = sut.Items[0];

        item.ClientId = 3;          // 드롭다운 two-way 바인딩이 먼저 설정했다고 가정
        sut.CommitClient(item);

        item.IsManual.Should().BeTrue();
        item.IsUnclassified.Should().BeFalse();
        var saved = _checklist.Items.Single(i => i.Id == item.Id);
        saved.IsManual.Should().BeTrue();
        saved.ClientId.Should().Be(3);
    }

    [Fact]
    public void CommitClient_then_FlushSaves_does_not_overwrite_manual_choice()
    {
        _tagging.KeywordToClient["SLD"] = 6;
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var item = sut.Items[0];

        item.ClientId = 3;
        sut.CommitClient(item);

        item.Text = "SLD 작업";   // dirty
        sut.FlushSaves();

        item.ClientId.Should().Be(3);   // 수동 교정 보호 유지
    }

    [Fact]
    public void CommitClient_to_null_marks_manual_unclassified()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var item = sut.Items[0];

        item.ClientId = null;       // 사용자가 미분류로 명시 지정
        sut.CommitClient(item);

        item.IsManual.Should().BeTrue();
        item.IsUnclassified.Should().BeTrue();
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상 실패: `error CS1061: 'ChecklistViewModel' does not contain a definition for 'CommitClient'`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/ViewModels/ChecklistViewModel.cs (add member)

    /// 드롭다운으로 사용자가 고객사를 교정했을 때 호출(item.ClientId는 바인딩으로 이미 갱신됨).
    /// 수동 교정으로 표시하여 이후 자동 재분류로부터 보호한다.
    [RelayCommand]
    public void CommitClient(ChecklistItemViewModel item)
    {
        if (!item.IsTask) return;

        item.IsManual = true;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        _checklist.UpdateItem(item.ToModel());
        TouchNote();
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상: `Passed!  - Failed: 0, Passed: 18`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/ChecklistViewModel.cs tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs
git commit -m "feat(checklist): manual client correction sets is_manual and protects from re-tagging

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: log_date 편집 + 신규 checklist 노트 생성(시스템 그룹 배치)

**Files:**
- Modify: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\ViewModels\ChecklistViewModel.cs`
- Test: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests\ViewModels\ChecklistViewModelTests.cs`

**Interfaces:**
- Consumes: `INoteRepository.Update(Note)`, `INoteRepository.Create(Note)`, `IGroupRepository.GetAll()`; (계약 §1) `Note`, `NoteType.Checklist`, `Group`.
- Produces: `ChecklistViewModel.LogDate` setter가 Note의 `LogDate` 저장; `static Note ChecklistViewModel.CreateChecklistNote(INoteRepository notes, IGroupRepository groups, DateOnly logDate)`.

가정(명시): `log_date`는 일일업무일지의 핵심 콘텐츠(기본 제목의 근거)이므로 변경 시 Note의 `UpdatedAt`을 갱신한다. (스펙 7.7의 콘텐츠/메타 구분에서 log_date는 명시되지 않았으나 콘텐츠로 분류.)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs (append inside class)
    [Fact]
    public void Changing_log_date_persists_to_note()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);

        sut.LogDate = new DateOnly(2026, 6, 27);

        _notes.Get(1)!.LogDate.Should().Be(new DateOnly(2026, 6, 27));
    }

    [Fact]
    public void Loading_note_does_not_repersist_log_date()
    {
        var note = SeedNote();
        note.LogDate = new DateOnly(2026, 6, 26);
        var before = note.UpdatedAt;
        var sut = CreateSut();

        sut.Load(note);   // 로드 자체는 UpdatedAt를 바꾸지 않아야 함

        _notes.Get(1)!.UpdatedAt.Should().Be(before);
    }

    [Fact]
    public void CreateChecklistNote_places_note_in_daily_log_system_group()
    {
        _groups.Groups.Add(new Group { Id = 1, Name = "일일업무일지", IsSystem = true, SortOrder = 100 });
        _groups.Groups.Add(new Group { Id = 2, Name = "주간보고", IsSystem = true, SortOrder = 101 });

        var note = ChecklistViewModel.CreateChecklistNote(_notes, _groups, new DateOnly(2026, 6, 26));

        note.Type.Should().Be(NoteType.Checklist);
        note.GroupId.Should().Be(1);
        note.LogDate.Should().Be(new DateOnly(2026, 6, 26));
        note.Id.Should().BeGreaterThan(0);
        _notes.Get(note.Id).Should().NotBeNull();
    }

    [Fact]
    public void CreateChecklistNote_leaves_group_null_when_system_group_missing()
    {
        var note = ChecklistViewModel.CreateChecklistNote(_notes, _groups, new DateOnly(2026, 6, 26));
        note.GroupId.Should().BeNull();
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상 실패: `error CS0117: 'ChecklistViewModel' does not contain a definition for 'CreateChecklistNote'` (및 `LogDate` 변경 영속화 단언 실패).

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/ViewModels/ChecklistViewModel.cs (add members)

    partial void OnLogDateChanged(DateOnly value)
    {
        if (_note is null) return;     // Load()는 _logDate 필드 직접 대입이라 여기 오지 않음
        _note.LogDate = value;
        _note.UpdatedAt = DateTimeOffset.UtcNow;
        _notes.Update(_note);
    }

    /// 새 checklist 메모를 시스템 그룹 '일일업무일지'(M1 시드)에 배치하여 생성한다.
    /// 시스템 그룹이 없으면 GroupId=null(미분류)로 둔다.
    public static Note CreateChecklistNote(INoteRepository notes, IGroupRepository groups, DateOnly logDate)
    {
        var group = groups.GetAll()
            .FirstOrDefault(g => g.IsSystem && g.Name == DailyLogGroupName);

        var now = DateTimeOffset.UtcNow;
        var note = new Note
        {
            GroupId = group?.Id,
            Type = NoteType.Checklist,
            Title = null,
            Body = null,
            LogDate = logDate,
            Pinned = false,
            SortOrder = 0,
            DeletedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        note.Id = notes.Create(note);
        return note;
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상: `Passed!  - Failed: 0, Passed: 22`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/ChecklistViewModel.cs tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs
git commit -m "feat(checklist): edit log_date and create checklist note in daily-log system group

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: 항목 정렬(MoveItem, sort_order, updated_at 미갱신)

**Files:**
- Modify: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\ViewModels\ChecklistViewModel.cs`
- Test: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests\ViewModels\ChecklistViewModelTests.cs`

**Interfaces:**
- Consumes: `IChecklistRepository.UpdateItem(ChecklistItem)`.
- Produces: `ChecklistViewModel.MoveItem(ChecklistItemViewModel item, int newIndex)`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs (append inside class)
    [Fact]
    public void MoveItem_reorders_collection_and_renumbers_sort_order()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();   // index 0
        sut.AddTask();   // index 1
        sut.AddTask();   // index 2
        var first = sut.Items[0];

        sut.MoveItem(first, 2);

        sut.Items.IndexOf(first).Should().Be(2);
        sut.Items[0].SortOrder.Should().Be(0);
        sut.Items[1].SortOrder.Should().Be(1);
        sut.Items[2].SortOrder.Should().Be(2);
        _checklist.Items.Single(i => i.Id == first.Id).SortOrder.Should().Be(2);
    }

    [Fact]
    public void MoveItem_does_not_bump_parent_note_updated_at()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        sut.AddTask();
        var beforeMove = _notes.Get(1)!.UpdatedAt;
        var first = sut.Items[0];

        sut.MoveItem(first, 1);

        _notes.Get(1)!.UpdatedAt.Should().Be(beforeMove);  // 메타 조작 → 미갱신
    }

    [Fact]
    public void MoveItem_ignores_out_of_range_index()
    {
        var note = SeedNote();
        var sut = CreateSut();
        sut.Load(note);
        sut.AddTask();
        var only = sut.Items[0];

        sut.MoveItem(only, 5);

        sut.Items.Should().HaveCount(1);
        sut.Items[0].Should().Be(only);
    }
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상 실패: `error CS1061: 'ChecklistViewModel' does not contain a definition for 'MoveItem'`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/ViewModels/ChecklistViewModel.cs (add members)

    /// 항목 순서 변경(드래그 등). sort_order 재부여는 메타 조작이므로 Note.UpdatedAt를 갱신하지 않는다.
    public void MoveItem(ChecklistItemViewModel item, int newIndex)
    {
        var oldIndex = Items.IndexOf(item);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Items.Count) return;

        Items.Move(oldIndex, newIndex);
        Renumber();
    }

    private void Renumber()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].SortOrder != i)
            {
                Items[i].SortOrder = i;
                _checklist.UpdateItem(Items[i].ToModel());   // UpdatedAt 보존(메타)
            }
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ChecklistViewModelTests"
```
예상: `Passed!  - Failed: 0, Passed: 25`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/ChecklistViewModel.cs tests/Memoria.Tests/ViewModels/ChecklistViewModelTests.cs
git commit -m "feat(checklist): reorder items via MoveItem without bumping updated_at

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: ChecklistView (WPF) + 디바운스 배선 + 수동 검증

**Files:**
- Create: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\Views\ChecklistView.xaml`
- Create: `C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\src\Memoria.App\Views\ChecklistView.xaml.cs`
- Test: (자동 테스트 없음 — View/시각 동작. 로직은 Task 1~8에서 검증 완료. 빌드만 검증.)

**Interfaces:**
- Consumes: `ChecklistViewModel`(DataContext), `ChecklistItemViewModel`(항목 바인딩), `Client`(드롭다운).
- Produces: 일일업무일지 편집 View. code-behind는 디바운스 타이머 tick → `FlushSaves()` 호출, 창 비활성/언로드 시 즉시 flush, 드롭다운 SelectionChanged → `CommitClientCommand`만 가진 얇은 껍데기.

> **노트(셸 통합은 M9):** 이 View는 독립 `UserControl`로만 산출한다. MainWindow가 `NoteType.Checklist`일 때 이 View를 띄우는 호스팅(ContentControl+DataTemplate)과 툴바 `[+ 체크리스트]` 진입점(`MainViewModel.NewChecklistCommand`, 계약 §9.3/§11)은 **M9에서 배선**한다. 본 Task는 빌드 통과 + 수동 시각 검증만 책임진다.

- [ ] **Step 1: Write the View (XAML)**

`Memoria.App`의 테마 리소스 사전에 **계약 §10 브러시 키**가 정의되어 있다고 가정한다(M7 팔레트가 정의, M2/M3/M9 View는 이 키만 소비). 이 View가 사용하는 키: `Brush.Surface`(표면), `Brush.Foreground`(기본 전경), `Brush.SecondaryForeground`(보조 텍스트=날짜 라벨), `Brush.Border`(경계), `Brush.UnclassifiedHighlight`(미분류 강조), `Brush.StrikethroughForeground`(완료 취소선). 임의 키(`Memoria.*`/`App.*`)는 사용하지 않는다. 모든 색상은 `DynamicResource`로만 바인딩한다(StaticResource 금지, 계약 §10).

```xml
<!-- src/Memoria.App/Views/ChecklistView.xaml -->
<UserControl x:Class="Memoria.App.Views.ChecklistView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:Memoria.App.ViewModels"
             d:DataContext="{d:DesignInstance Type=vm:ChecklistViewModel}"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             Background="{DynamicResource Brush.Surface}"
             Unloaded="OnUnloaded">
    <DockPanel Margin="12">

        <!-- 헤더: log_date 편집 -->
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="업무일자:" VerticalAlignment="Center"
                       Foreground="{DynamicResource Brush.SecondaryForeground}" Margin="0,0,6,0"/>
            <DatePicker SelectedDate="{Binding LogDate, Converter={StaticResource DateOnlyToDateTimeConverter}, Mode=TwoWay}"/>
        </StackPanel>

        <!-- 추가 버튼 -->
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <Button Content="+ 할 일" Command="{Binding AddTaskCommand}" Margin="0,0,6,0"/>
            <Button Content="+ 이슈"  Command="{Binding AddIssueCommand}"/>
        </StackPanel>

        <!-- 항목 목록 -->
        <ItemsControl ItemsSource="{Binding Items}">
            <ItemsControl.ItemTemplate>
                <DataTemplate DataType="{x:Type vm:ChecklistItemViewModel}">
                    <Border Margin="0,2" Padding="4"
                            BorderThickness="1"
                            BorderBrush="{DynamicResource Brush.Border}">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>

                            <!-- 체크박스: task만 표시 -->
                            <CheckBox Grid.Column="0"
                                      VerticalAlignment="Center" Margin="0,0,6,0"
                                      Visibility="{Binding ShowCheckbox, Converter={StaticResource BoolToVisibilityConverter}}"
                                      IsChecked="{Binding Done, Mode=OneWay}"
                                      Command="{Binding DataContext.ToggleDoneCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                      CommandParameter="{Binding}"/>

                            <!-- 텍스트: 완료 시 취소선 -->
                            <TextBox Grid.Column="1"
                                     Text="{Binding Text, UpdateSourceTrigger=PropertyChanged}"
                                     Foreground="{DynamicResource Brush.Foreground}">
                                <TextBox.Style>
                                    <Style TargetType="TextBox">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding IsStruck}" Value="True">
                                                <Setter Property="TextDecorations" Value="Strikethrough"/>
                                                <Setter Property="Foreground" Value="{DynamicResource Brush.StrikethroughForeground}"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TextBox.Style>
                            </TextBox>

                            <!-- 고객사 드롭다운: task만, 미분류면 강조 -->
                            <ComboBox Grid.Column="2" Width="140" Margin="6,0,0,0"
                                      Visibility="{Binding ShowCheckbox, Converter={StaticResource BoolToVisibilityConverter}}"
                                      ItemsSource="{Binding DataContext.AvailableClients, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                      DisplayMemberPath="Name"
                                      SelectedValuePath="Id"
                                      SelectedValue="{Binding ClientId, Mode=TwoWay}"
                                      SelectionChanged="OnClientSelectionChanged">
                                <ComboBox.Style>
                                    <Style TargetType="ComboBox">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding IsUnclassified}" Value="True">
                                                <Setter Property="Background" Value="{DynamicResource Brush.UnclassifiedHighlight}"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </ComboBox.Style>
                            </ComboBox>

                            <!-- 삭제 -->
                            <Button Grid.Column="3" Content="🗑" Margin="6,0,0,0"
                                    Command="{Binding DataContext.RemoveItemCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                    CommandParameter="{Binding}"/>
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </DockPanel>
</UserControl>
```

```csharp
// src/Memoria.App/Views/ChecklistView.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Memoria.App.ViewModels;

namespace Memoria.App.Views;

public partial class ChecklistView : UserControl
{
    private readonly DispatcherTimer _debounce;

    public ChecklistView()
    {
        InitializeComponent();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            (DataContext as ChecklistViewModel)?.FlushSaves();
        };
        // 텍스트 변경마다 디바운스 재시작: ItemsControl 내부 TextBox 변경을 가로채 타이머 리셋
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(OnAnyTextChanged));
    }

    private void OnAnyTextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnClientSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: ChecklistItemViewModel item }
            && DataContext is ChecklistViewModel vm
            && vm.CommitClientCommand.CanExecute(item))
        {
            vm.CommitClientCommand.Execute(item);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _debounce.Stop();
        (DataContext as ChecklistViewModel)?.FlushSaves();   // 즉시 flush
    }
}
```

- [ ] **Step 2: Build to verify the View compiles**

```
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\Memoria.sln"
```
예상: `Build succeeded.` (경고 0~소수, 오류 0). 만약 `DateOnlyToDateTimeConverter`/`BoolToVisibilityConverter` 리소스가 M2에 없다면 `Memoria.App`의 공용 리소스 사전에 추가한 뒤 재빌드.

- [ ] **Step 3: 전체 회귀 테스트(로직 무결성 확인)**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~Checklist"
```
예상: `Passed!  - Failed: 0, Passed: 32` (ChecklistItemViewModelTests 7 + ChecklistViewModelTests 25).

- [ ] **Step 4: 수동 검증 체크포인트 (실제 Windows 실행으로 눈으로 확인)**

앱을 실행하고 일일업무일지 메모를 연다(또는 새 체크리스트 생성). 다음을 **하나씩 직접 확인**한다:

  1. **항목 추가**: `+ 할 일` 클릭 → 체크박스가 있는 빈 항목이 추가된다. `+ 이슈` 클릭 → 체크박스 **없는** 항목이 추가된다.
  2. **자동 태깅(디바운스)**: 할 일 텍스트에 `SLD 자율형공장 정리` 입력 후 약 0.5초 멈춤 → 고객사 드롭다운이 **자율형 공장**(SLD 아님)으로 자동 설정된다(우선순위 자율형공장>SLD 확인). 강조 배경이 사라진다.
  3. **미분류 강조**: `회의록 작성`처럼 키워드 없는 할 일 입력 → 드롭다운 배경이 `Brush.UnclassifiedHighlight` 색으로 **강조**된다.
  4. **체크/취소선**: 할 일 체크박스 토글 → 텍스트에 **취소선**과 `Brush.StrikethroughForeground` 색이 적용된다. 다시 토글 → 원래대로 복귀.
  5. **수동 교정 보호**: 어떤 할 일의 고객사를 드롭다운에서 다른 값으로 바꾼 뒤, 그 항목 텍스트를 다른 키워드로 수정해도(디바운스 후) 고객사가 **자동으로 바뀌지 않는다**(수동보호).
  6. **이슈 고객사 없음**: 이슈 항목에는 고객사 드롭다운이 보이지 않는다.
  7. **log_date 편집**: 상단 DatePicker에서 날짜 변경 → 사이드바의 해당 일일일지 위치/제목(기본 제목=log_date)이 갱신된다.
  8. **테마 전환 무결성**: 설정에서 라이트↔다크 전환 시, 강조/취소선/표면 색이 **즉시** 따라 바뀐다(모두 `DynamicResource`이므로 깜빡임 없이 적용).
  9. **자동 저장 영속성**: 텍스트 입력 후 앱을 닫았다가 다시 열면 입력 내용이 **유지**된다(언로드 시 즉시 flush).

각 항목이 기대대로 동작하면 체크. 하나라도 실패하면 superpowers:systematic-debugging으로 원인 분석 후 해당 Task로 돌아가 수정/재검증.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/Views/ChecklistView.xaml src/Memoria.App/Views/ChecklistView.xaml.cs
git commit -m "feat(checklist): add ChecklistView with debounced save and unclassified highlight

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 완료 기준 (Milestone Done)
- `dotnet.exe build` 성공(WPF 포함), `dotnet.exe test --filter "FullyQualifiedName~Checklist"` 전부 통과.
- `ChecklistViewModel` / `ChecklistItemViewModel`이 항목 CRUD·done 토글/취소선·디바운스 자동태깅+수동보호·고객사 수동교정·log_date 편집·정렬·시스템 그룹 배치·미분류 강조 상태를 모두 제공하며 xUnit으로 검증됨.
- Task 9 수동 검증 체크포인트 9개 항목 모두 눈으로 확인 완료.

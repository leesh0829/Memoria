# 사이드바/탐색 UX 개선 (스펙 A) 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 긴 제목 말줄임(삭제 버튼 보존), 선택 항목 재클릭 시 선택 해제, 가운데 패널의 탐색기식 드릴다운(하위 그룹 + 메모 + 브레드크럼)을 추가한다.

**Architecture:** 가운데 패널을 [브레드크럼] + [하위 그룹 ItemsControl] + [기존 메모 ListBox]로 구성한다(혼합 리스트 대신 폴더 ItemsControl을 메모 위에 배치 → 기존 `SelectedNote` 바인딩 유지). 탐색 상태는 기존 `SelectedNode`(현재 폴더) 하나만 사용하고, 클릭 시 왼쪽 트리도 동기화한다. 말줄임·선택해제는 선언적 XAML + 코드비하인드 입력 처리.

**Tech Stack:** C#/.NET9, WPF, CommunityToolkit.Mvvm, xUnit + FluentAssertions.

## Global Constraints

- 빌드/테스트는 **Windows `dotnet.exe`를 WSL interop로** 호출. 실행 전 `taskkill.exe /IM Memoria.exe /F 2>/dev/null`.
  - 빌드: `dotnet.exe build "Memoria.sln" -c Release`
  - 테스트: `dotnet.exe test "tests/Memoria.Tests" -c Release`
- 커밋 메시지 끝에 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- 목표: 빌드 **경고 0 / 오류 0**, 기존 **344 테스트 + 신규 테스트 그린**.
- **기존 동작 비파괴**: 왼쪽 트리 그룹 선택 → 가운데 직속 메모 표시는 유지. 하위 그룹이 있을 때만 폴더 행 추가.
- WPF(XAML/입력/렌더)는 WSL 자동 테스트 불가 → **빌드 게이트 + Windows GUI 수동 검증**. VM 로직은 자동 테스트.
- ② 선택 해제는 **드래그를 깨지 않도록 "클릭(드래그 임계값 미만으로 뗌)"일 때만** 수행.

---

## File Structure

- `src/Memoria.App/ViewModels/FolderEntryViewModel.cs` — 신규: 가운데 폴더(하위 그룹) 행.
- `src/Memoria.App/ViewModels/BreadcrumbSegmentViewModel.cs` — 신규: 브레드크럼 조각.
- `src/Memoria.App/ViewModels/MainViewModel.cs` — 수정: `Folders`/`Breadcrumb` 구성 + `NavigateToFolder`/`BuildBreadcrumb` + OnSelectedNodeChanged 확장.
- `src/Memoria.App/MainWindow.xaml` — 수정: 가운데 열에 브레드크럼 행 + 폴더 ItemsControl, 메모 제목 말줄임, 3 리스트 가로스크롤 비활성.
- `src/Memoria.App/MainWindow.xaml.cs` — 수정: 폴더/브레드크럼 클릭 핸들러 + ② 재클릭 해제(트리·시스템·메모).
- `src/Memoria.App/Themes/Base.xaml` — 수정: `ListItemText`에 TextTrimming(또는 템플릿에서 직접).
- 테스트: `tests/Memoria.Tests/App/MainViewModelDrilldownTests.cs` (신규).

---

## A1: ① 긴 텍스트 말줄임 (XAML, 빌드+GUI)

**Files:**
- Modify: `src/Memoria.App/MainWindow.xaml`, `src/Memoria.App/Themes/Base.xaml`

**Interfaces:** 없음(순수 표시).

> WPF 표시 → 자동 테스트 없음. 검증 = 빌드 경고0/오류0 + GM 수동.

- [ ] **Step 1: 메모 제목 말줄임** — `MainWindow.xaml`의 `NoteListBox` ItemTemplate 제목 TextBlock에 `TextTrimming` 추가

```xml
                <TextBlock Grid.Column="0" Text="{Binding DisplayTitle}"
                           Style="{StaticResource ListItemText}"
                           TextTrimming="CharacterEllipsis"
                           VerticalAlignment="Center"/>
```

- [ ] **Step 2: 3개 리스트 가로 스크롤 비활성** — `NoteListBox`, `GroupTree`, `SystemListBox` 각 태그에 속성 추가(긴 텍스트가 가로로 밀리지 않고 말줄임되도록)

```xml
                             ScrollViewer.HorizontalScrollBarVisibility="Disabled"
```
> 세 컨트롤 여는 태그에 각각 추가. (GroupTree는 이미 `ListItemTextTree`로 말줄임됨 → 가로 스크롤만 끄면 폭 안에서 잘림.)

- [ ] **Step 3: 시스템 목록 말줄임** — `SystemListBox`의 ItemTemplate TextBlock(`ListItemText`)에 `TextTrimming="CharacterEllipsis"` 추가(메모와 동일). 해당 TextBlock 태그에 속성 추가.

- [ ] **Step 4: 빌드 확인 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -5
git add src/Memoria.App/MainWindow.xaml src/Memoria.App/Themes/Base.xaml
git commit -m "feat(ux): ellipsis-truncate long titles/group names (keep delete button)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
> Base.xaml 변경이 없으면 add에서 제외. 경고 0 / 오류 0 확인.

---

## A2: ④ 가운데 패널 모델 — 폴더 + 브레드크럼 (App VM, TDD)

**Files:**
- Create: `src/Memoria.App/ViewModels/FolderEntryViewModel.cs`, `src/Memoria.App/ViewModels/BreadcrumbSegmentViewModel.cs`
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs`
- Test: `tests/Memoria.Tests/App/MainViewModelDrilldownTests.cs`

**Interfaces:**
- Produces:
  - `FolderEntryViewModel(SidebarNodeViewModel node)` — `.Node`, `.Name`, `.GroupId`.
  - `BreadcrumbSegmentViewModel(SidebarNodeViewModel node)` — `.Node`, `.Name`.
  - `MainViewModel.Folders` (ObservableCollection<FolderEntryViewModel>), `MainViewModel.Breadcrumb` (ObservableCollection<BreadcrumbSegmentViewModel>), `void MainViewModel.NavigateToFolder(SidebarNodeViewModel node)`.

- [ ] **Step 1: 실패 테스트 작성** — `tests/Memoria.Tests/App/MainViewModelDrilldownTests.cs`

```csharp
using System.Linq;
using FluentAssertions;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Memoria.Tests.App.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Memoria.Tests.App;

public class MainViewModelDrilldownTests
{
    private static MainViewModel NewVm(FakeGroupRepository g, FakeNoteRepository n)
    {
        var time = new FakeTimeProvider();
        return new MainViewModel(g, n,
            new DebounceAutosaveService(time, 500),
            new FakeRecoveryJournal(), time, new FakeSearchService(),
            M9EditorFakes.ChecklistFactory(n, g), M9EditorFakes.WeeklyFactory(n, g, time));
    }

    [Fact]
    public void SelectingGroupWithChildren_PopulatesFolders_AndBreadcrumb()
    {
        var g = new FakeGroupRepository();
        var pId = g.Create(new Group { Name = "부모", SortOrder = 0 });
        var cId = g.Create(new Group { Name = "자식", ParentId = pId, SortOrder = 0 });
        var notes = new FakeNoteRepository();
        var now = System.DateTimeOffset.UtcNow;
        notes.Create(new Note { GroupId = pId, Title = "부모메모", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(g, notes);
        vm.LoadGroups();

        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == pId);

        vm.Folders.Select(f => f.GroupId).Should().Equal(cId);       // 하위 그룹 = 폴더 행
        vm.Notes.Select(n => n.DisplayTitle).Should().Contain("부모메모"); // 직속 메모 유지
        vm.Breadcrumb.Select(b => b.Name).Should().Equal("부모");     // 루트→현재 경로
    }

    [Fact]
    public void SelectingLeafGroup_NoFolders()
    {
        var g = new FakeGroupRepository();
        var pId = g.Create(new Group { Name = "부모", SortOrder = 0 });
        var cId = g.Create(new Group { Name = "자식", ParentId = pId, SortOrder = 0 });
        var vm = NewVm(g, new FakeNoteRepository());
        vm.LoadGroups();
        var parent = vm.SidebarNodes.First(n => n.GroupId == pId);
        parent.IsExpanded = true;

        vm.SelectedNode = parent.Children.First(n => n.GroupId == cId);

        vm.Folders.Should().BeEmpty();
        vm.Breadcrumb.Select(b => b.Name).Should().Equal("부모", "자식");
    }

    [Fact]
    public void NavigateToFolder_MovesSelectionAndRebuilds()
    {
        var g = new FakeGroupRepository();
        var pId = g.Create(new Group { Name = "부모", SortOrder = 0 });
        var cId = g.Create(new Group { Name = "자식", ParentId = pId, SortOrder = 0 });
        var vm = NewVm(g, new FakeNoteRepository());
        vm.LoadGroups();
        vm.SelectedNode = vm.SidebarNodes.First(n => n.GroupId == pId);
        var folder = vm.Folders.First(f => f.GroupId == cId);

        vm.NavigateToFolder(folder.Node);

        vm.SelectedNode!.GroupId.Should().Be(cId);
        vm.Breadcrumb.Select(b => b.Name).Should().Equal("부모", "자식");
    }
}
```

- [ ] **Step 2: 실패 확인**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~MainViewModelDrilldownTests" 2>&1 | tail -8
```
기대: 컴파일 실패(`Folders`/`Breadcrumb`/`NavigateToFolder`/`FolderEntryViewModel` 없음).

- [ ] **Step 3: 뷰모델 2종 생성**

`src/Memoria.App/ViewModels/FolderEntryViewModel.cs`
```csharp
namespace Memoria.App.ViewModels;

/// <summary>가운데 패널의 하위 그룹(폴더) 행. 클릭 시 그 그룹으로 드릴다운.</summary>
public sealed class FolderEntryViewModel
{
    public SidebarNodeViewModel Node { get; }
    public string Name => Node.Name;
    public int? GroupId => Node.GroupId;
    public FolderEntryViewModel(SidebarNodeViewModel node) => Node = node;
}
```

`src/Memoria.App/ViewModels/BreadcrumbSegmentViewModel.cs`
```csharp
namespace Memoria.App.ViewModels;

/// <summary>브레드크럼 한 조각. 클릭 시 그 조상으로 이동.</summary>
public sealed class BreadcrumbSegmentViewModel
{
    public SidebarNodeViewModel Node { get; }
    public string Name => Node.Name;
    public BreadcrumbSegmentViewModel(SidebarNodeViewModel node) => Node = node;
}
```

- [ ] **Step 4: MainViewModel 확장** — 컬렉션 선언(기존 `Notes` 근처)

```csharp
    public System.Collections.ObjectModel.ObservableCollection<FolderEntryViewModel> Folders { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<BreadcrumbSegmentViewModel> Breadcrumb { get; } = new();
```

`OnSelectedNodeChanged`(기존)에 폴더/브레드크럼 재구성 추가:
```csharp
    partial void OnSelectedNodeChanged(SidebarNodeViewModel? value)
    {
        IsUndoAvailable = false;
        LoadNotes();
        BuildFolders();
        BuildBreadcrumb();
    }
```

헬퍼 + 네비게이션 추가:
```csharp
    private void BuildFolders()
    {
        Folders.Clear();
        if (SelectedNode is null) return;
        foreach (var child in SelectedNode.Children.Where(c => c.Kind == SidebarNodeKind.Group))
            Folders.Add(new FolderEntryViewModel(child));
    }

    private void BuildBreadcrumb()
    {
        Breadcrumb.Clear();
        if (SelectedNode is null) return;
        var path = new System.Collections.Generic.List<SidebarNodeViewModel>();
        if (FindPath(SidebarNodes, SelectedNode, path))
            foreach (var n in path) Breadcrumb.Add(new BreadcrumbSegmentViewModel(n));
        else
            Breadcrumb.Add(new BreadcrumbSegmentViewModel(SelectedNode)); // 시스템/미분류 단일 조각
    }

    private static bool FindPath(
        System.Collections.Generic.IEnumerable<SidebarNodeViewModel> nodes,
        SidebarNodeViewModel target,
        System.Collections.Generic.List<SidebarNodeViewModel> path)
    {
        foreach (var n in nodes)
        {
            path.Add(n);
            if (ReferenceEquals(n, target)) return true;
            if (FindPath(n.Children, target, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    /// 가운데 폴더/브레드크럼 클릭 → 그 노드를 현재 폴더로.
    public void NavigateToFolder(SidebarNodeViewModel node) => SelectedNode = node;
```
> `using System.Linq;`가 파일에 있는지 확인(없으면 추가). `SidebarNodeKind`는 이미 참조됨.

- [ ] **Step 5: 통과 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~MainViewModelDrilldownTests" 2>&1 | tail -8
```
기대: PASS 3건.

- [ ] **Step 6: 전체 빌드 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -5
git add src/Memoria.App/ViewModels/FolderEntryViewModel.cs src/Memoria.App/ViewModels/BreadcrumbSegmentViewModel.cs src/Memoria.App/ViewModels/MainViewModel.cs tests/Memoria.Tests/App/MainViewModelDrilldownTests.cs
git commit -m "feat(ux): middle-panel folders + breadcrumb model (drill-down)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## A3: ④ 가운데 패널 XAML — 브레드크럼 + 폴더 행 (App, 빌드+GUI)

**Files:**
- Modify: `src/Memoria.App/MainWindow.xaml`, `src/Memoria.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `MainViewModel.Folders`/`Breadcrumb`/`NavigateToFolder` (A2), `SyncSidebarSelection` (기존 code-behind).

> WPF → 빌드 게이트 + GUI. 자동 테스트 없음.

- [ ] **Step 1: 가운데 열에 행 추가** — `MainWindow.xaml`의 `Grid.Column="1"` 안 Grid.RowDefinitions를 4행으로 변경(브레드크럼·폴더·메모·검색)

```xml
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />  <!-- 브레드크럼 -->
                    <RowDefinition Height="Auto" />  <!-- 하위 그룹(폴더) -->
                    <RowDefinition Height="*" />     <!-- 메모 목록 -->
                    <RowDefinition Height="Auto" />  <!-- 검색 결과 -->
                </Grid.RowDefinitions>
```
그리고 기존 `NoteListBox`는 `Grid.Row="2"`, 검색 Border는 `Grid.Row="3"`으로 변경.

- [ ] **Step 2: 브레드크럼 바(Row 0)** — 위 RowDefinitions 아래에 추가

```xml
                <ItemsControl Grid.Row="0" ItemsSource="{Binding Breadcrumb}" Margin="4,2"
                              Visibility="{Binding Breadcrumb.Count, Converter={StaticResource CountToVisibility}}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate><StackPanel Orientation="Horizontal"/></ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal">
                                <TextBlock Text="›" Margin="2,0" Foreground="{DynamicResource Brush.SecondaryForeground}"/>
                                <Button Content="{Binding Name}" Click="OnBreadcrumbClick" Tag="{Binding}"
                                        Background="Transparent" BorderThickness="0" Padding="2,0"
                                        Foreground="{DynamicResource Brush.Accent}" Cursor="Hand"/>
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
```
> `CountToVisibility` 컨버터는 기존 검색 패널에서 사용 중(있음). 첫 조각 앞 `›`는 사소한 장식 — 그대로 두거나 인덱스 0 숨김은 후속.

- [ ] **Step 3: 폴더 행(Row 1)** — 하위 그룹 목록

```xml
                <ItemsControl Grid.Row="1" ItemsSource="{Binding Folders}"
                              ScrollViewer.HorizontalScrollBarVisibility="Disabled">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Button Click="OnFolderClick" Tag="{Binding}"
                                    HorizontalContentAlignment="Left"
                                    Background="Transparent" BorderThickness="0" Padding="6,4" Cursor="Hand">
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="📁" Margin="0,0,6,0"/>
                                    <TextBlock Text="{Binding Name}" TextTrimming="CharacterEllipsis"
                                               Foreground="{DynamicResource Brush.Foreground}"/>
                                </StackPanel>
                            </Button>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
```

- [ ] **Step 4: 코드비하인드 클릭 핸들러** — `MainWindow.xaml.cs`

```csharp
    private void OnFolderClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: Memoria.App.ViewModels.FolderEntryViewModel f })
        {
            ViewModel.NavigateToFolder(f.Node);
            SyncSidebarSelection();   // 왼쪽 트리도 현재 폴더로 동기화
        }
    }

    private void OnBreadcrumbClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: Memoria.App.ViewModels.BreadcrumbSegmentViewModel b })
        {
            ViewModel.NavigateToFolder(b.Node);
            SyncSidebarSelection();
        }
    }
```

- [ ] **Step 5: 빌드 확인 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -6
git add src/Memoria.App/MainWindow.xaml src/Memoria.App/MainWindow.xaml.cs
git commit -m "feat(ux): breadcrumb bar + subfolder rows in middle panel (drill-down UI)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
기대: 경고 0 / 오류 0.

---

## A4: ② 선택 항목 재클릭 → 선택 해제 (3표면, App 코드비하인드, 빌드+GUI)

**Files:**
- Modify: `src/Memoria.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: 기존 `_dragStartPoint`, `ExceededDragThreshold`, `_syncingSelection`, `ViewModel.SelectedNode`/`SelectedNote`, `SystemListBox`, `GroupTree`, `NoteListBox`, `FindVisualAncestor`/`FindDataContext`.

> 입력 이벤트/드래그 임계값 → 빌드 게이트 + GUI. 해제 후 VM 상태(SelectedNode/Note=null)는 기존 LoadNotes/에디터 경로가 처리.

- [ ] **Step 1: 재클릭 감지 필드 추가** — 클래스 필드부에 추가

```csharp
    // ② 재클릭 해제: 누를 때 이미 선택돼 있었는지 기록(뗄 때 드래그 아니면 해제).
    private bool _wasSelectedOnDown;
```

- [ ] **Step 2: 공용 Down 기록 확장** — 기존 `List_PreviewMouseLeftButtonDown`(시작점 기록) 수정

```csharp
    private void List_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        // 눌린 항목이 이미 선택 상태인지 판정(메모/그룹 공통).
        _wasSelectedOnDown = false;
        if (FindDataContext<NoteListItemViewModel>(e.OriginalSource) is { } note)
            _wasSelectedOnDown = ReferenceEquals(ViewModel.SelectedNote, note);
        else if (FindDataContext<SidebarNodeViewModel>(e.OriginalSource) is { } node)
            _wasSelectedOnDown = node.IsSelected || ReferenceEquals(ViewModel.SelectedNode, node);
    }
```
> `FindDataContext<T>` 헬퍼는 기존 존재(부모 체인 DataContext 조회). 없으면 기존 유사 헬퍼 사용.

- [ ] **Step 3: 메모 목록 Up 핸들러(재클릭 해제)** — `NoteListBox`에 `PreviewMouseLeftButtonUp="NoteList_PreviewMouseLeftButtonUp"` 추가하고 핸들러 구현

```csharp
    private void NoteList_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_wasSelectedOnDown || ExceededDragThreshold(e)) return;   // 드래그면 해제 안 함
        if (FindDataContext<NoteListItemViewModel>(e.OriginalSource) is { } note
            && ReferenceEquals(ViewModel.SelectedNote, note))
        {
            ViewModel.SelectedNote = null;   // 에디터 닫힘(기존 경로)
            e.Handled = true;
        }
    }
```

- [ ] **Step 4: 그룹 트리 Up 핸들러** — `GroupTree`에 `PreviewMouseLeftButtonUp="GroupTree_PreviewMouseLeftButtonUp"` 추가

```csharp
    private void GroupTree_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_wasSelectedOnDown || ExceededDragThreshold(e)) return;
        if (FindDataContext<SidebarNodeViewModel>(e.OriginalSource) is { } node
            && (node.IsSelected || ReferenceEquals(ViewModel.SelectedNode, node)))
        {
            _syncingSelection = true;
            ClearTreeNodeSelection(ViewModel.SidebarNodes);   // 트리 하이라이트 해제
            _syncingSelection = false;
            ViewModel.SelectedNode = null;                    // 가운데 비움(LoadNotes Clear)
            e.Handled = true;
        }
    }
```

- [ ] **Step 5: 시스템 목록 Up 핸들러** — `SystemListBox`에 `PreviewMouseLeftButtonUp="SystemList_PreviewMouseLeftButtonUp"` 추가

```csharp
    private void SystemList_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_wasSelectedOnDown || ExceededDragThreshold(e)) return;
        if (FindDataContext<SidebarNodeViewModel>(e.OriginalSource) is { } node
            && ReferenceEquals(ViewModel.SelectedNode, node))
        {
            _syncingSelection = true;
            SystemListBox.SelectedItem = null;
            _syncingSelection = false;
            ViewModel.SelectedNode = null;
            e.Handled = true;
        }
    }
```
> `List_PreviewMouseLeftButtonDown`은 이미 GroupTree/NoteListBox에 걸려 있음. SystemListBox에도 `PreviewMouseLeftButtonDown="List_PreviewMouseLeftButtonDown"`가 없으면 추가(Down 기록 필요).

- [ ] **Step 6: 빌드 확인 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -6
git add src/Memoria.App/MainWindow.xaml src/Memoria.App/MainWindow.xaml.cs
git commit -m "feat(ux): click-selected-item-again to deselect (tree/system/notes, drag-safe)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## A5: 통합 — 빌드·테스트·퍼블리시 + GUI 체크리스트

**Files:** 없음(검증).

- [ ] **Step 1: 전체 빌드 + 전체 테스트**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null
dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -6
dotnet.exe test "tests/Memoria.Tests" -c Release 2>&1 | tail -4
```
기대: 경고0/오류0, 실패0 / 통과(기존 344 + 신규 3).

- [ ] **Step 2: 자체 포함 단일 파일 퍼블리시**

```bash
dotnet.exe publish "src/Memoria.App/Memoria.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish 2>&1 | tail -3
ls -la --time-style=+%H:%M publish/Memoria.exe
```

- [ ] **Step 3: 사용자 GUI 검증** — `publish/Memoria.exe`:
  1. **①** 아주 긴 메모 제목/그룹명 → `…`로 잘리고 🗑 삭제 버튼이 항상 보임, 가로 스크롤 없음.
  2. **②** 선택된 그룹/시스템그룹/메모를 **다시 클릭 → 해제**(그룹 해제=가운데 비움, 메모 해제=에디터 닫힘). **드래그(메모→그룹, 그룹 재부모)는 여전히 동작**.
  3. **④** 하위 그룹 있는 그룹 클릭 → 가운데에 📁 하위 그룹 + 메모, 폴더 클릭 → 드릴다운, **브레드크럼**으로 상위 이동, 왼쪽 트리도 따라 선택/펼침.
  4. 하위 없는 그룹/(미분류)/시스템 그룹 → 폴더 행 없이 기존처럼 메모만.
  5. 다크/라이트 대비.

- [ ] **Step 4: (통과 후) finishing-a-development-branch로 병합 + v0.5.0 릴리스**

---

## Self-Review (작성자 점검 결과)

- **스펙 커버리지**: §3.1 말줄임→A1, §3.3 폴더/브레드크럼 모델→A2, 그 XAML→A3, §3.2 선택해제→A4, §6 테스트→A2 자동 + A5 수동. 전 항목 매핑.
- **스펙 대비 구현 정제(명시)**: §3.3의 "혼합 목록(DataTemplateSelector)"을 **폴더 ItemsControl + 기존 메모 ListBox 분리**로 구현(기존 `SelectedNote` 바인딩·🗑·선택 하이라이트 보존, 타입 혼합 SelectedItem 문제 회피). 동일 결과(하위그룹+메모 표시), 더 단순. 리뷰어가 spec 편차로 볼 수 있으니 명시.
- **플레이스홀더**: 없음. WPF 태스크(A1/A3/A4)는 자동 테스트 없음(명시).
- **타입 일관성**: `FolderEntryViewModel(.Node/.Name/.GroupId)`, `BreadcrumbSegmentViewModel(.Node/.Name)`, `MainViewModel.Folders/Breadcrumb/NavigateToFolder`, 핸들러 `OnFolderClick/OnBreadcrumbClick`, `NoteList_/GroupTree_/SystemList_PreviewMouseLeftButtonUp`, `_wasSelectedOnDown` — 태스크 간 일치.
- **주의**: A4는 기존 `FindDataContext<T>`/`ExceededDragThreshold`/`ClearTreeNodeSelection`/`_syncingSelection`/`_dragStartPoint`에 의존 — 구현자가 실제 시그니처 확인 후 사용. `List_PreviewMouseLeftButtonDown` 확장이 기존 드래그 시작 로직을 깨지 않는지 확인.

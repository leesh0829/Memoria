# 마크다운 노트 3-모드 (읽기/편집/마크다운) 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 마크다운 노트를 읽기/편집/마크다운 3-모드로 만들고, 열 때 기본=읽기(서식 없는 원본 텍스트 + 이미지만 렌더)로 하며, 렌더 폰트를 개선한다.

**Architecture:** VM의 `bool IsPreviewMode`를 `MarkdownViewMode { Read, Edit, Rendered }` enum으로 바꾼다. 읽기 뷰의 텍스트/이미지 분할은 Core 순수 함수(`MarkdownReadSegmenter`, 테스트 가능), 그 FlowDocument 구성(`RenderRead`)과 폰트는 App(WPF). 상단 3버튼 + 본문 3뷰.

**Tech Stack:** C#/.NET9, WPF, Markdig(기존), CommunityToolkit.Mvvm, xUnit + FluentAssertions.

## Global Constraints

- 빌드/테스트는 **Windows `dotnet.exe`를 WSL interop로** 호출. 실행 전 `taskkill.exe /IM Memoria.exe /F 2>/dev/null`.
  - 빌드: `dotnet.exe build "Memoria.sln" -c Release`
  - 테스트: `dotnet.exe test "tests/Memoria.Tests" -c Release`
- 커밋 메시지 끝에 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- 목표: 빌드 **경고 0 / 오류 0**, 기존 **347 테스트 + 신규 테스트 그린**.
- **비파괴**: plain 노트(비마크다운)는 기존처럼 편집 TextBox만. 자동저장·검색·복구·목록 제목 무변경.
- WPF(렌더/XAML)는 WSL 자동 테스트 불가 → **빌드 게이트 + Windows GUI 수동 검증**. 세그먼터·VM 상태는 자동 테스트.
- **읽기 뷰 = 원본 텍스트 그대로(마크다운 문법 문자 보존) + 이미지 참조만 실제 렌더.** 마크다운 서식(제목/굵게/목록)은 읽기 뷰에서 적용 안 함.

---

## File Structure

- `src/Memoria.Core/Text/MarkdownReadSegmenter.cs` — 신규: 본문 → 텍스트/이미지 세그먼트(순수).
- `src/Memoria.App/ViewModels/MarkdownViewMode.cs` — 신규: enum.
- `src/Memoria.App/ViewModels/MainViewModel.cs` — 수정: IsPreviewMode → ViewMode + Show*/Set* + OpenNote/Convert.
- `src/Memoria.App/Services/IMarkdownRenderer.cs` + `MarkdownRenderer.cs` — 수정: `RenderRead` 추가 + FontFamily.
- `src/Memoria.App/Behaviors/MarkdownPreviewBehavior.cs` — 수정: `RenderMode`(read/rendered) 첨부 속성.
- `src/Memoria.App/MainWindow.xaml` — 수정: 3버튼 + 3뷰.
- 테스트: `tests/Memoria.Tests/Core/MarkdownReadSegmenterTests.cs`(신규), `tests/Memoria.Tests/App/MainViewModelMarkdownModeTests.cs`(신규).

---

## MM1: MarkdownReadSegmenter (Core, TDD)

**Files:**
- Create: `src/Memoria.Core/Text/MarkdownReadSegmenter.cs`
- Test: `tests/Memoria.Tests/Core/MarkdownReadSegmenterTests.cs`

**Interfaces:**
- Produces: `Memoria.Core.Text.ReadSegment(bool IsImage, string Value)`; `static IReadOnlyList<ReadSegment> MarkdownReadSegmenter.Segment(string? body)`.

- [ ] **Step 1: 실패 테스트 작성** — `tests/Memoria.Tests/Core/MarkdownReadSegmenterTests.cs`

```csharp
using FluentAssertions;
using Memoria.Core.Text;
using Xunit;

namespace Memoria.Tests.Core;

public class MarkdownReadSegmenterTests
{
    [Fact]
    public void Segment_SplitsTextAndImages_InOrder()
    {
        var segs = MarkdownReadSegmenter.Segment("hello ![a](x.png) world");
        segs.Should().HaveCount(3);
        segs[0].Should().Be(new ReadSegment(false, "hello "));
        segs[1].Should().Be(new ReadSegment(true, "x.png"));
        segs[2].Should().Be(new ReadSegment(false, " world"));
    }

    [Fact]
    public void Segment_NoImages_SingleTextSegment_PreservesMarkdownSyntax()
    {
        var segs = MarkdownReadSegmenter.Segment("# 제목\n**굵게**");
        segs.Should().ContainSingle();
        segs[0].Should().Be(new ReadSegment(false, "# 제목\n**굵게**"));
    }

    [Fact]
    public void Segment_ConsecutiveImages_NoTextBetween()
    {
        var segs = MarkdownReadSegmenter.Segment("![](a.png)![](b.png)");
        segs.Should().Equal(new ReadSegment(true, "a.png"), new ReadSegment(true, "b.png"));
    }

    [Fact]
    public void Segment_IgnoresAltText_ExtractsPathTrimmed()
    {
        var segs = MarkdownReadSegmenter.Segment("![some alt]( p.png )");
        segs.Should().ContainSingle().Which.Should().Be(new ReadSegment(true, "p.png"));
    }

    [Fact]
    public void Segment_EmptyOrNull_ReturnsEmpty()
    {
        MarkdownReadSegmenter.Segment("").Should().BeEmpty();
        MarkdownReadSegmenter.Segment(null).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: 실패 확인**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~MarkdownReadSegmenterTests" 2>&1 | tail -8
```
기대: 컴파일 실패(`MarkdownReadSegmenter`/`ReadSegment` 없음).

- [ ] **Step 3: 구현** — `src/Memoria.Core/Text/MarkdownReadSegmenter.cs`

```csharp
using System.Text.RegularExpressions;

namespace Memoria.Core.Text;

/// <summary>읽기 뷰용 세그먼트. IsImage면 Value=이미지 상대경로, 아니면 Value=리터럴 텍스트.</summary>
public sealed record ReadSegment(bool IsImage, string Value);

/// <summary>본문을 이미지 참조 `![alt](path)` 기준으로 텍스트/이미지 세그먼트로 분할(순수).</summary>
public static class MarkdownReadSegmenter
{
    private static readonly Regex ImgRx = new(@"!\[[^\]]*\]\(([^)]+)\)", RegexOptions.Compiled);

    public static IReadOnlyList<ReadSegment> Segment(string? body)
    {
        var result = new List<ReadSegment>();
        if (string.IsNullOrEmpty(body)) return result;
        int pos = 0;
        foreach (Match m in ImgRx.Matches(body))
        {
            if (m.Index > pos)
                result.Add(new ReadSegment(false, body.Substring(pos, m.Index - pos)));
            result.Add(new ReadSegment(true, m.Groups[1].Value.Trim()));
            pos = m.Index + m.Length;
        }
        if (pos < body.Length)
            result.Add(new ReadSegment(false, body.Substring(pos)));
        return result;
    }
}
```

- [ ] **Step 4: 통과 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~MarkdownReadSegmenterTests" 2>&1 | tail -8
```
기대: PASS 5건.

- [ ] **Step 5: 커밋**

```bash
git add src/Memoria.Core/Text/MarkdownReadSegmenter.cs tests/Memoria.Tests/Core/MarkdownReadSegmenterTests.cs
git commit -m "feat(md): MarkdownReadSegmenter (text/image split for read view)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## MM2: MainViewModel 3-모드 상태 (App VM, TDD)

**Files:**
- Create: `src/Memoria.App/ViewModels/MarkdownViewMode.cs`
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs`
- Test: `tests/Memoria.Tests/App/MainViewModelMarkdownModeTests.cs`

**Interfaces:**
- Produces: `enum MarkdownViewMode { Read, Edit, Rendered }`; `MainViewModel.ViewMode`, `ShowRead`/`ShowEdit`/`ShowRendered`/`ShowToolbar`, `SetReadModeCommand`/`SetEditModeCommand`/`SetRenderedModeCommand`.

> XAML은 아직 옛 바인딩(IsPreviewMode/ShowPreview/ShowSource/TogglePreview)을 참조 — MM4에서 갱신한다. 그 사이 런타임 바인딩만 어긋나고 빌드는 통과(WPF 바인딩은 런타임). MM2→MM4 연속 실행.

- [ ] **Step 1: enum 생성** — `src/Memoria.App/ViewModels/MarkdownViewMode.cs`

```csharp
namespace Memoria.App.ViewModels;

public enum MarkdownViewMode { Read, Edit, Rendered }
```

- [ ] **Step 2: 실패 테스트 작성** — `tests/Memoria.Tests/App/MainViewModelMarkdownModeTests.cs`

기존 VM 테스트 하네스(예: `MainViewModelSidebarTests`의 NewVm) 패턴을 따른다. 아래는 그 헬퍼를 재사용:

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

public class MainViewModelMarkdownModeTests
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
    public void OpenNote_MarkdownWithContent_OpensInRead()
    {
        var notes = new FakeNoteRepository();
        var now = DateTimeOffset.UtcNow;
        var id = notes.Create(new Note { Type = NoteType.Plain, Body = "내용", BodyFormat = "markdown", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(new FakeGroupRepository(), notes);

        vm.OpenNote(id);

        vm.ViewMode.Should().Be(MarkdownViewMode.Read);
        vm.ShowRead.Should().BeTrue();
        vm.ShowEdit.Should().BeFalse();
    }

    [Fact]
    public void OpenNote_EmptyMarkdown_OpensInEdit()
    {
        var notes = new FakeNoteRepository();
        var now = DateTimeOffset.UtcNow;
        var id = notes.Create(new Note { Type = NoteType.Plain, Body = "", BodyFormat = "markdown", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(new FakeGroupRepository(), notes);

        vm.OpenNote(id);

        vm.ViewMode.Should().Be(MarkdownViewMode.Edit);
        vm.ShowEdit.Should().BeTrue();
        vm.ShowToolbar.Should().BeTrue();
    }

    [Fact]
    public void SetModes_SwitchViewMode()
    {
        var notes = new FakeNoteRepository();
        var now = DateTimeOffset.UtcNow;
        var id = notes.Create(new Note { Type = NoteType.Plain, Body = "x", BodyFormat = "markdown", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(new FakeGroupRepository(), notes);
        vm.OpenNote(id);

        vm.SetEditModeCommand.Execute(null);
        vm.ViewMode.Should().Be(MarkdownViewMode.Edit);
        vm.SetRenderedModeCommand.Execute(null);
        vm.ViewMode.Should().Be(MarkdownViewMode.Rendered);
        vm.ShowRendered.Should().BeTrue();
        vm.SetReadModeCommand.Execute(null);
        vm.ViewMode.Should().Be(MarkdownViewMode.Read);
    }

    [Fact]
    public void PlainNote_AlwaysShowsEdit_NotReadOrRendered()
    {
        var notes = new FakeNoteRepository();
        var now = DateTimeOffset.UtcNow;
        var id = notes.Create(new Note { Type = NoteType.Plain, Body = "hi", BodyFormat = "plain", CreatedAt = now, UpdatedAt = now });
        var vm = NewVm(new FakeGroupRepository(), notes);
        vm.OpenNote(id);

        vm.ShowEdit.Should().BeTrue();
        vm.ShowRead.Should().BeFalse();
        vm.ShowRendered.Should().BeFalse();
    }
}
```

- [ ] **Step 3: 실패 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~MainViewModelMarkdownModeTests" 2>&1 | tail -8
```
기대: 컴파일 실패(`ViewMode`/`ShowRead` 등 없음).

- [ ] **Step 4: VM 수정** — `MainViewModel.cs`의 마크다운 상태 블록(현재 line 57~104 부근)을 교체

`using Memoria.App.ViewModels;`는 같은 네임스페이스라 불필요.

옛 블록:
```csharp
    [ObservableProperty] private bool isPreviewMode = true;
    [ObservableProperty] private string bodyFormat = "plain";

    public bool IsMarkdown => BodyFormat == "markdown";
    public bool ShowToolbar => IsMarkdown && !IsPreviewMode;
    public bool ShowPreview => IsMarkdown && IsPreviewMode;
    public bool ShowSource  => !ShowPreview;
    ...
    partial void OnIsPreviewModeChanged(bool value) { ...ShowToolbar/ShowPreview/ShowSource... }
    partial void OnBodyFormatChanged(string value) { ...IsMarkdown/ShowToolbar/ShowPreview/ShowSource... }
    [RelayCommand] private void TogglePreview() => IsPreviewMode = !IsPreviewMode;
```
새 블록으로 교체:
```csharp
    [ObservableProperty] private MarkdownViewMode viewMode = MarkdownViewMode.Read;
    [ObservableProperty] private string bodyFormat = "plain";

    public bool IsMarkdown  => BodyFormat == "markdown";
    public bool ShowRead     => IsMarkdown && ViewMode == MarkdownViewMode.Read;
    public bool ShowRendered => IsMarkdown && ViewMode == MarkdownViewMode.Rendered;
    public bool ShowEdit     => !IsMarkdown || ViewMode == MarkdownViewMode.Edit;   // plain은 항상 편집
    public bool ShowToolbar  => IsMarkdown && ViewMode == MarkdownViewMode.Edit;

    partial void OnViewModeChanged(MarkdownViewMode value)
    {
        OnPropertyChanged(nameof(ShowRead));
        OnPropertyChanged(nameof(ShowRendered));
        OnPropertyChanged(nameof(ShowEdit));
        OnPropertyChanged(nameof(ShowToolbar));
    }

    partial void OnBodyFormatChanged(string value)
    {
        OnPropertyChanged(nameof(IsMarkdown));
        OnPropertyChanged(nameof(ShowRead));
        OnPropertyChanged(nameof(ShowRendered));
        OnPropertyChanged(nameof(ShowEdit));
        OnPropertyChanged(nameof(ShowToolbar));
    }

    [RelayCommand] private void SetReadMode()     => ViewMode = MarkdownViewMode.Read;
    [RelayCommand] private void SetEditMode()     => ViewMode = MarkdownViewMode.Edit;
    [RelayCommand] private void SetRenderedMode() => ViewMode = MarkdownViewMode.Rendered;
```

`ConvertToMarkdown`의 `IsPreviewMode = false;`(현재 line 101)를 →
```csharp
        ViewMode = MarkdownViewMode.Edit;
```

`OpenNote`의 `IsPreviewMode = note.BodyFormat == "markdown" && !string.IsNullOrEmpty(note.Body);`(현재 line 433)를 →
```csharp
        ViewMode = note.BodyFormat == "markdown" && !string.IsNullOrEmpty(note.Body)
            ? MarkdownViewMode.Read : MarkdownViewMode.Edit;
```

> `CurrentNoteId`(RM7) 등 다른 멤버는 유지. `IsPreviewMode`/`ShowPreview`/`ShowSource`/`TogglePreviewCommand`를 참조하는 곳이 VM 내부에 더 있으면(예: ResolveLiveTitle은 무관) 함께 정리.

- [ ] **Step 5: 통과 확인 + 전체 VM 테스트 회귀**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~MainViewModel" 2>&1 | tail -8
```
기대: 신규 4 + 기존 MainViewModel 테스트 그린. (일부 기존 테스트가 IsPreviewMode를 참조하면 ViewMode로 갱신.)

- [ ] **Step 6: 전체 빌드 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -5
git add src/Memoria.App/ViewModels/MarkdownViewMode.cs src/Memoria.App/ViewModels/MainViewModel.cs tests/Memoria.Tests/App/MainViewModelMarkdownModeTests.cs
git commit -m "feat(md): MarkdownViewMode (Read/Edit/Rendered) replacing IsPreviewMode

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## MM3: RenderRead + 폰트 + Behavior RenderMode (App, 빌드+GUI)

**Files:**
- Modify: `src/Memoria.App/Services/IMarkdownRenderer.cs`, `src/Memoria.App/Services/MarkdownRenderer.cs`, `src/Memoria.App/Behaviors/MarkdownPreviewBehavior.cs`

**Interfaces:**
- Consumes: `Memoria.Core.Text.MarkdownReadSegmenter` (MM1), `IAttachmentService.ResolveToAbsolute`.
- Produces: `FlowDocument IMarkdownRenderer.RenderRead(string? markdown)`; `MarkdownPreviewBehavior.RenderMode` 첨부 속성.

> WPF FlowDocument → 자동 테스트 없음. 빌드 게이트 + GUI(MM5).

- [ ] **Step 1: 인터페이스에 RenderRead 추가** — `IMarkdownRenderer.cs`

```csharp
    FlowDocument RenderRead(string? markdown);
```

- [ ] **Step 2: MarkdownRenderer — 폰트 + RenderRead** — `MarkdownRenderer.cs`

파일 상단(클래스 내부)에 앱 글꼴 상수 추가하고 `Render`의 FlowDocument 생성에 FontFamily 지정:
```csharp
    private static readonly System.Windows.Media.FontFamily UiFont =
        new System.Windows.Media.FontFamily("Segoe UI, Malgun Gothic");
```
기존 `Render`의 `var flow = new FlowDocument { PagePadding = new Thickness(0), FontSize = 14 };` 를 →
```csharp
        var flow = new FlowDocument { PagePadding = new Thickness(0), FontSize = 14, FontFamily = UiFont };
```
그리고 `RenderRead` 추가(세그먼트 → 텍스트/이미지):
```csharp
    public FlowDocument RenderRead(string? markdown)
    {
        var flow = new FlowDocument { PagePadding = new Thickness(0), FontSize = 14, FontFamily = UiFont };
        flow.SetResourceReference(FlowDocument.ForegroundProperty, "Brush.Foreground");
        try
        {
            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var seg in Memoria.Core.Text.MarkdownReadSegmenter.Segment(markdown))
            {
                if (seg.IsImage)
                {
                    para.Inlines.Add(BuildReadImage(seg.Value));
                }
                else
                {
                    // 텍스트 그대로(줄바꿈 보존).
                    var lines = seg.Value.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        para.Inlines.Add(new Run(lines[i]));
                        if (i < lines.Length - 1) para.Inlines.Add(new LineBreak());
                    }
                }
            }
            flow.Blocks.Add(para);
        }
        catch
        {
            flow.Blocks.Clear();
            flow.Blocks.Add(new Paragraph(new Run(markdown ?? "")));
        }
        return flow;
    }

    private WInline BuildReadImage(string relPath)
    {
        try
        {
            var abs = _attachments.ResolveToAbsolute(relPath);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(abs);
            bmp.EndInit();
            var img = new System.Windows.Controls.Image
            {
                Source = bmp, Stretch = Stretch.Uniform,
                MaxWidth = bmp.PixelWidth > 0 ? bmp.PixelWidth : 600, MaxHeight = 400,
            };
            return new InlineUIContainer(img);
        }
        catch { return new Run($"[이미지: {relPath}]"); }
    }
```
> `WInline`은 기존 파일 상단의 `using WInline = System.Windows.Documents.Inline;` 별칭. `BitmapImage`/`BitmapCacheOption`/`Uri`/`Stretch`/`InlineUIContainer`는 기존 `BuildImage`에서 이미 사용 중이라 using 존재. (기존 `BuildImage`와 로직 유사하나 읽기용 별도 — 필요시 공용 헬퍼로 추출 가능하나 이번엔 별도로 둔다.)

- [ ] **Step 3: MarkdownPreviewBehavior — RenderMode 첨부 속성** — `MarkdownPreviewBehavior.cs`

`Active`/`Markdown` 옆에 `RenderMode`(기본 "rendered") 추가하고 OnChanged에서 분기:
```csharp
    public static readonly DependencyProperty RenderModeProperty =
        DependencyProperty.RegisterAttached("RenderMode", typeof(string), typeof(MarkdownPreviewBehavior),
            new PropertyMetadata("rendered", OnChanged));
    public static void SetRenderMode(DependencyObject o, string v) => o.SetValue(RenderModeProperty, v);
    public static string GetRenderMode(DependencyObject o) => (string)o.GetValue(RenderModeProperty);
```
기존 `OnChanged`의 렌더 호출부:
```csharp
        var renderer = AppServices.Resolve<IMarkdownRenderer>();
        viewer.Document = renderer.Render(GetMarkdown(viewer) ?? string.Empty);
```
를 →
```csharp
        var renderer = AppServices.Resolve<IMarkdownRenderer>();
        var text = GetMarkdown(viewer) ?? string.Empty;
        viewer.Document = GetRenderMode(viewer) == "read"
            ? renderer.RenderRead(text)
            : renderer.Render(text);
```

- [ ] **Step 4: 빌드 확인 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -6
git add src/Memoria.App/Services/IMarkdownRenderer.cs src/Memoria.App/Services/MarkdownRenderer.cs src/Memoria.App/Behaviors/MarkdownPreviewBehavior.cs
git commit -m "feat(md): RenderRead (text+images) + UI FontFamily + behavior RenderMode

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
기대: 경고 0 / 오류 0.

---

## MM4: XAML — 3버튼 + 3뷰 (App, 빌드+GUI)

**Files:**
- Modify: `src/Memoria.App/MainWindow.xaml`

**Interfaces:**
- Consumes: MM2 VM(ViewMode/Show*/Set*Mode), MM3 behavior(RenderMode).

- [ ] **Step 1: 모드 버튼 교체** — `MainWindow.xaml`의 마크다운 토글 버튼(현재 line 284~286)을 **3버튼**으로 교체(마크다운 노트일 때만 표시). "마크다운으로 전환" 버튼(287~290)은 유지.

```xml
                                        <!-- markdown 노트: 읽기/편집/마크다운 3-모드 -->
                                        <StackPanel Orientation="Horizontal"
                                                    Visibility="{Binding IsMarkdown, Converter={StaticResource BoolToVis}}">
                                            <Button Content="읽기"   Command="{Binding SetReadModeCommand}"     Padding="8,2" Margin="0,0,2,0"/>
                                            <Button Content="편집"   Command="{Binding SetEditModeCommand}"     Padding="8,2" Margin="0,0,2,0"/>
                                            <Button Content="마크다운" Command="{Binding SetRenderedModeCommand}" Padding="8,2"/>
                                        </StackPanel>
```

- [ ] **Step 2: 본문 3뷰** — 현재 본문 소스 TextBox(308~315)와 미리보기 뷰어(318~323)를 아래로 교체(3개 모두 Grid.Row="3").

```xml
                            <!-- 본문: 읽기 뷰(텍스트 그대로 + 이미지) -->
                            <FlowDocumentScrollViewer Grid.Row="3" Margin="0,8,0,0"
                                     VerticalScrollBarVisibility="Auto" Background="Transparent"
                                     beh:MarkdownPreviewBehavior.RenderMode="read"
                                     beh:MarkdownPreviewBehavior.Active="{Binding ShowRead}"
                                     beh:MarkdownPreviewBehavior.Markdown="{Binding EditorBody}"
                                     Visibility="{Binding ShowRead, Converter={StaticResource BoolToVis}}" />

                            <!-- 본문: 편집(원본 소스) -->
                            <TextBox Grid.Row="3" x:Name="BodyEditor"
                                     Text="{Binding EditorBody, UpdateSourceTrigger=PropertyChanged}"
                                     AcceptsReturn="True" AcceptsTab="True" TextWrapping="Wrap"
                                     VerticalScrollBarVisibility="Auto" Margin="0,8,0,0" Padding="0"
                                     BorderThickness="0" Background="Transparent"
                                     Foreground="{DynamicResource Brush.Foreground}"
                                     PreviewKeyDown="BodyEditor_PreviewKeyDown"
                                     Visibility="{Binding ShowEdit, Converter={StaticResource BoolToVis}}" />

                            <!-- 본문: 마크다운 렌더 -->
                            <FlowDocumentScrollViewer Grid.Row="3" Margin="0,8,0,0"
                                     VerticalScrollBarVisibility="Auto" Background="Transparent"
                                     beh:MarkdownPreviewBehavior.RenderMode="rendered"
                                     beh:MarkdownPreviewBehavior.Active="{Binding ShowRendered}"
                                     beh:MarkdownPreviewBehavior.Markdown="{Binding EditorBody}"
                                     Visibility="{Binding ShowRendered, Converter={StaticResource BoolToVis}}" />
```
> 툴바(295~305)의 `ShowToolbar` 바인딩은 그대로 유효(MM2에서 유지). `PreviewToggleLabel` 컨버터는 더 이상 사용 안 하면 리소스에서 제거 가능(선택).

- [ ] **Step 3: 빌드 확인 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -6
git add src/Memoria.App/MainWindow.xaml
git commit -m "feat(md): 3-mode editor UI (읽기/편집/마크다운) + read/rendered views

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## MM5: 통합 — 빌드·테스트·퍼블리시 + GUI 체크리스트

**Files:** 없음(검증).

- [ ] **Step 1: 전체 빌드 + 전체 테스트**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null
dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -6
dotnet.exe test "tests/Memoria.Tests" -c Release 2>&1 | tail -4
```
기대: 경고0/오류0, 실패0 / 통과(기존 347 + 신규 ~9).

- [ ] **Step 2: 자체 포함 단일 파일 퍼블리시**

```bash
dotnet.exe publish "src/Memoria.App/Memoria.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish 2>&1 | tail -3
ls -la --time-style=+%H:%M publish/Memoria.exe
```

- [ ] **Step 3: 사용자 GUI 검증** — `publish/Memoria.exe`:
  1. **기본 읽기** — 내용 있는 마크다운 노트를 열면 **읽기 모드**: `# 제목`·`**굵게**`가 서식 없이 문자 그대로 보이고, **이미지는 실제로 렌더**됨. 마크다운 자동 렌더 안 됨.
  2. **폰트** — 마크다운(렌더) 모드에서 폰트가 정상(세리프 아님, 가독성 개선).
  3. **3버튼** — [읽기]/[편집]/[마크다운] 전환. 편집=원본 TextBox+툴바, 마크다운=전체 렌더(이미지 포함).
  4. **새/빈 노트** — 새 메모는 편집 모드로 열려 바로 타이핑.
  5. **plain 노트** — 기존처럼 편집 TextBox + "마크다운으로 전환" 버튼. 전환 후 편집 모드.
  6. **편집 저장** — 편집 후 다른 모드/노트로 갔다 와도 내용 유지(자동저장).
  7. 다크/라이트 대비.

- [ ] **Step 4: (통과 후) finishing-a-development-branch로 병합 + v0.6.0 릴리스**

---

## Self-Review (작성자 점검 결과)

- **스펙 커버리지**: §3.1 상태모델→MM2, §3.2 세그먼터→MM1, §3.3 RenderRead→MM3, §3.4 폰트→MM3, §3.5 XAML 3버튼/3뷰→MM4, §6 테스트→MM1/MM2 자동 + MM5 수동. 전 항목 매핑.
- **플레이스홀더**: 없음. WPF 태스크(MM3/MM4)는 자동 테스트 없음(명시).
- **타입 일관성**: `ReadSegment(IsImage, Value)`, `MarkdownReadSegmenter.Segment`, `MarkdownViewMode{Read,Edit,Rendered}`, `ViewMode/ShowRead/ShowEdit/ShowRendered/ShowToolbar`, `SetReadModeCommand/SetEditModeCommand/SetRenderedModeCommand`, `IMarkdownRenderer.RenderRead`, behavior `RenderMode` — 태스크 간 일치.
- **주의**: MM2가 `IsPreviewMode`/`ShowPreview`/`ShowSource`/`TogglePreviewCommand`를 제거 → MM4 전까지 XAML 런타임 바인딩 어긋남(빌드는 통과). MM2→MM3→MM4 연속 실행, GUI 검증은 MM5. 기존 MainViewModel 테스트가 `IsPreviewMode`를 참조하면 MM2에서 `ViewMode`로 갱신. `RenderRead`의 이미지 로직은 기존 `BuildImage`와 유사(중복 허용 — 읽기/렌더 분리).

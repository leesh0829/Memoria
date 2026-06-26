# Memoria M7 — Theme/Color & Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 라이트/다크/시스템 모드 + 프리셋 팔레트(default/dark/sepia/solarized) + 커스텀 강조색을 런타임 전환 가능한 테마 시스템과, 테마·주간보고·고객사·단축키·자동시작/트레이·백업/휴지통 보존을 한 곳에서 편집하는 설정 화면을 완성한다.

**Architecture:** 색 결정 규칙(mode+preset+accent→팔레트 사전 URI), 강조색 검증/정규화, 시스템 테마 레지스트리 값 파싱은 모두 WPF 비의존 **순수 로직**(정적 클래스)으로 분리해 `Memoria.Tests`(net9.0-windows)에서 xUnit으로 100% 자동 검증한다. WPF 리소스 사전 교체·`SystemEvents` 구독 같은 시각/전역 동작은 `IThemeApplier`/`ISystemThemeSource` 추상화 뒤에 두어 `IThemeService` 코디네이션 로직을 fake로 테스트하고, 실제 색 전환은 **수동 검증 체크포인트**로 확인한다. 설정 로직은 `SettingsViewModel`/`ClientsSettingsViewModel`(CommunityToolkit.Mvvm만 의존)로 분리해 자동 테스트하고, View(code-behind)는 얇게 유지한다.

**Tech Stack:** C# / .NET 9 / WPF(net9.0-windows) / `CommunityToolkit.Mvvm`(ObservableObject/[ObservableProperty]/IRelayCommand) / WPF `ResourceDictionary` + `DynamicResource` / `Microsoft.Win32.Registry`(AppsUseLightTheme) + `Microsoft.Win32.SystemEvents`(UserPreferenceChanged) / 테스트는 `xUnit` + `FluentAssertions`.

## Global Constraints
- 런타임: **.NET 9**.
- TFM: `Memoria.Core` = **net9.0**, `Memoria.App` = **net9.0-windows**(`<UseWPF>true</UseWPF>`), `Memoria.Tests` = **net9.0-windows**.
- 테마/시스템통합/설정 화면 코드는 **`Memoria.App`에만** 둔다. `Memoria.Core`는 WPF/Win32 비의존(M7은 Core 스키마를 건드리지 않고 `settings` 키와 `clients`/`client_rules`만 소비).
- DB/데이터 루트: `%LOCALAPPDATA%\Memoria`(M1이 생성). M7은 `ISettingsRepository`/`IClientRepository`를 통해서만 접근.
- 빌드/테스트는 **Windows `dotnet.exe`** 로만 수행(WPF는 Linux dotnet 빌드 불가). WSL 호출 시 **Windows 절대경로** 인자 사용.
- 단일파일 publish에서 **`PublishTrimmed` 금지**(WPF 트리밍 미지원), **`EnableCompressionInSingleFile` 미사용**(콜드 스타트 비용). 테마 `.xaml` 사전은 단일파일에 포함(`<Page>`/`<Resource>` 빌드 액션).
- **모든 색/브러시는 `DynamicResource`만** 사용(StaticResource 금지). 전환은 최상위 `Application.Resources.MergedDictionaries`의 **테마 사전 1개만 교체**해 깜빡임 최소화. 서드파티 컨트롤(트레이 컨텍스트 메뉴 등)도 동일 테마 키 사용.
- 테마 키는 3축: **`theme.mode`(light|dark|system)**, **`theme.preset`(default|dark|sepia|solarized)**, **`theme.accent`(#RRGGBB)**. 프리셋은 mode/accent로 표현되지 않으므로 **별도 키로 저장**하며 셋이 함께 최종 색을 결정한다.
- 시스템 모드: 레지스트리 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` 읽기 + 런타임 변경은 `SystemEvents.UserPreferenceChanged`(WM_SETTINGCHANGE 기반, M6 message-only 창과 동일 수명) 구독으로 추종.
- 고객사 두 순서는 **독립**(§6.3): 표시순 = `clients.sort_order`(설정의 "순서변경" UI가 바꾸는 값), 매칭순 = `client_rules.priority`(키워드 편집 화면에서만 조정). 순서변경 UI는 **priority를 절대 건드리지 않는다.**
- 분류 우선순위 도메인 규칙(참고): `자율형공장` > `SLD`(M1 검증 완료). M7은 규칙을 재구현하지 않고 priority 편집 UI만 제공한다.
- 양식 A 도메인 규칙(참고): `[업무 내용]`↔`[이슈]` 사이 빈 줄 1개(M1 검증 완료). M7은 머리글 문자열만 설정으로 노출한다.
- 설정 키는 `Memoria.Core.SettingsKeys` 상수만 사용(문자열 리터럴 금지).

---

### Task 1: 테마 해상도 순수 로직 (ThemeResolver)

**Files:**
- Create: `src/Memoria.App/Theming/ThemeResolver.cs`
- Test: `tests/Memoria.Tests/Theming/ThemeResolverTests.cs`

**Interfaces:**
- Consumes: `Memoria.Core.Models.ThemeMode`(Light/Dark/System — 계약 §1).
- Produces: `static class ThemeResolver` { `static readonly string[] Presets`; `ThemeMode ResolveEffectiveMode(ThemeMode mode, bool systemIsLight)`; `string NormalizePreset(string?)`; `Uri ResolvePaletteUri(ThemeMode mode, string? preset, bool systemIsLight)` (component-relative `Uri`, 예 `Themes/Sepia.Dark.xaml`) }.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/Theming/ThemeResolverTests.cs
using System;
using FluentAssertions;
using Memoria.App.Theming;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Theming;

public class ThemeResolverTests
{
    [Theory]
    [InlineData(ThemeMode.Light, true, ThemeMode.Light)]
    [InlineData(ThemeMode.Light, false, ThemeMode.Light)]
    [InlineData(ThemeMode.Dark, true, ThemeMode.Dark)]
    [InlineData(ThemeMode.Dark, false, ThemeMode.Dark)]
    [InlineData(ThemeMode.System, true, ThemeMode.Light)]
    [InlineData(ThemeMode.System, false, ThemeMode.Dark)]
    public void ResolveEffectiveMode_follows_system_only_when_mode_is_system(
        ThemeMode mode, bool systemIsLight, ThemeMode expected)
    {
        ThemeResolver.ResolveEffectiveMode(mode, systemIsLight).Should().Be(expected);
    }

    [Theory]
    [InlineData("default", "default")]
    [InlineData("DARK", "dark")]
    [InlineData(" Sepia ", "sepia")]
    [InlineData("solarized", "solarized")]
    [InlineData(null, "default")]
    [InlineData("", "default")]
    [InlineData("neon", "default")] // 알 수 없는 프리셋 → default 폴백
    public void NormalizePreset_lowercases_trims_and_falls_back(string? input, string expected)
    {
        ThemeResolver.NormalizePreset(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(ThemeMode.Light, "default", true, "Themes/Default.Light.xaml")]
    [InlineData(ThemeMode.Dark, "default", true, "Themes/Default.Dark.xaml")]
    [InlineData(ThemeMode.System, "sepia", false, "Themes/Sepia.Dark.xaml")]
    [InlineData(ThemeMode.System, "solarized", true, "Themes/Solarized.Light.xaml")]
    [InlineData(ThemeMode.Dark, "neon", true, "Themes/Default.Dark.xaml")] // 미지의 프리셋 폴백
    public void ResolvePaletteUri_combines_effective_mode_and_preset(
        ThemeMode mode, string preset, bool systemIsLight, string expected)
    {
        var uri = ThemeResolver.ResolvePaletteUri(mode, preset, systemIsLight);
        uri.IsAbsoluteUri.Should().BeFalse();
        uri.OriginalString.Should().Be(expected);
    }

    [Fact]
    public void Presets_list_is_the_four_supported_palettes()
    {
        ThemeResolver.Presets.Should().BeEquivalentTo(
            new[] { "default", "dark", "sepia", "solarized" });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ThemeResolverTests"
```
예상 실패: `error CS0246: The type or namespace name 'ThemeResolver' could not be found` (컴파일 실패).

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/Theming/ThemeResolver.cs
using System;
using Memoria.Core.Models;

namespace Memoria.App.Theming;

public static class ThemeResolver
{
    public static readonly string[] Presets = { "default", "dark", "sepia", "solarized" };

    public static ThemeMode ResolveEffectiveMode(ThemeMode mode, bool systemIsLight) => mode switch
    {
        ThemeMode.Light => ThemeMode.Light,
        ThemeMode.Dark => ThemeMode.Dark,
        ThemeMode.System => systemIsLight ? ThemeMode.Light : ThemeMode.Dark,
        _ => ThemeMode.Light,
    };

    public static string NormalizePreset(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
            return "default";

        var normalized = preset.Trim().ToLowerInvariant();
        return Array.IndexOf(Presets, normalized) >= 0 ? normalized : "default";
    }

    public static Uri ResolvePaletteUri(ThemeMode mode, string? preset, bool systemIsLight)
    {
        var effective = ResolveEffectiveMode(mode, systemIsLight);
        var normalized = NormalizePreset(preset);
        var presetName = char.ToUpperInvariant(normalized[0]) + normalized[1..];
        var variant = effective == ThemeMode.Light ? "Light" : "Dark";
        return new Uri($"Themes/{presetName}.{variant}.xaml", UriKind.Relative);
    }
}
```
> component-relative `Uri`(상대 URI)를 사용한다. `ResourceDictionary.Source`에 상대 URI를 넣으면 WPF가 해당 어셈블리 component 기준으로 해석하므로 `pack://` 절대 URI가 불필요하고, 단위 테스트에서 pack 스킴 등록 없이도 URI를 생성·검증할 수 있다.

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ThemeResolverTests"
```
예상: `Passed!  - Failed: 0, Passed: 19` (Theory 6+7+5 + Fact 1).

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/Theming/ThemeResolver.cs tests/Memoria.Tests/Theming/ThemeResolverTests.cs
git commit -m "feat(app): add pure theme resolver for mode/preset palette uri

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: 강조색 검증/정규화 + 시스템 테마 값 파서 (순수 로직)

**Files:**
- Create: `src/Memoria.App/Theming/AccentColor.cs`, `src/Memoria.App/Theming/SystemThemeReader.cs`
- Test: `tests/Memoria.Tests/Theming/AccentColorTests.cs`, `tests/Memoria.Tests/Theming/SystemThemeReaderTests.cs`

**Interfaces:**
- Consumes: `SettingsKeys.ThemeAccent`("theme.accent") 형식(#RRGGBB) — 호출자가 저장/로드.
- Produces:
  - `static class AccentColor` { `const string Default = "#0078D4"`; `bool IsValid(string?)`; `string Normalize(string?)`(유효하면 `#RRGGBB` 대문자, 아니면 `Default`) }.
  - `static class SystemThemeReader` { `const string PersonalizeKeyPath`; `const string AppsUseLightThemeValue = "AppsUseLightTheme"`; `bool ParseAppsUseLightTheme(object? registryValue, bool fallbackIsLight = true)`; `bool ReadIsLight()`(실제 HKCU 읽기, 값 없으면 light) }.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/Theming/AccentColorTests.cs
using FluentAssertions;
using Memoria.App.Theming;
using Xunit;

namespace Memoria.Tests.Theming;

public class AccentColorTests
{
    [Theory]
    [InlineData("#0078D4", true)]
    [InlineData("0078D4", true)]   // # 생략 허용
    [InlineData("#abcdef", true)]
    [InlineData("#ABC", false)]    // 3자리 단축 미지원
    [InlineData("#12345G", false)] // 비-16진
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_accepts_six_digit_hex(string? input, bool expected)
    {
        AccentColor.IsValid(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("0078d4", "#0078D4")]
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData("garbage", "#0078D4")] // 무효 → 기본값
    [InlineData(null, "#0078D4")]
    public void Normalize_returns_uppercase_hash_prefixed_or_default(string? input, string expected)
    {
        AccentColor.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Default_is_windows_blue()
    {
        AccentColor.Default.Should().Be("#0078D4");
    }
}
```

```csharp
// tests/Memoria.Tests/Theming/SystemThemeReaderTests.cs
using FluentAssertions;
using Memoria.App.Theming;
using Xunit;

namespace Memoria.Tests.Theming;

public class SystemThemeReaderTests
{
    [Theory]
    [InlineData(1, true)]    // AppsUseLightTheme=1 → light
    [InlineData(0, false)]   // 0 → dark
    public void ParseAppsUseLightTheme_maps_int_value(int value, bool expected)
    {
        SystemThemeReader.ParseAppsUseLightTheme(value).Should().Be(expected);
    }

    [Fact]
    public void ParseAppsUseLightTheme_handles_long_value()
    {
        SystemThemeReader.ParseAppsUseLightTheme(0L).Should().BeFalse();
        SystemThemeReader.ParseAppsUseLightTheme(1L).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParseAppsUseLightTheme_uses_fallback_when_value_missing(bool fallback)
    {
        SystemThemeReader.ParseAppsUseLightTheme(null, fallbackIsLight: fallback).Should().Be(fallback);
    }

    [Fact]
    public void Constants_are_canonical()
    {
        SystemThemeReader.PersonalizeKeyPath.Should()
            .Be(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        SystemThemeReader.AppsUseLightThemeValue.Should().Be("AppsUseLightTheme");
    }

    [Fact]
    public void ReadIsLight_does_not_throw_on_windows()
    {
        // 실제 HKCU를 읽되 결과 값은 환경에 따라 다르므로 예외 없음만 검증.
        var act = () => SystemThemeReader.ReadIsLight();
        act.Should().NotThrow();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~AccentColorTests|FullyQualifiedName~SystemThemeReaderTests"
```
예상 실패: `error CS0246: ... 'AccentColor'/'SystemThemeReader' ...` (컴파일 실패).

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/Theming/AccentColor.cs
using System.Text.RegularExpressions;

namespace Memoria.App.Theming;

public static partial class AccentColor
{
    public const string Default = "#0078D4";

    [GeneratedRegex("^#?[0-9A-Fa-f]{6}$")]
    private static partial Regex HexPattern();

    public static bool IsValid(string? hex)
        => !string.IsNullOrWhiteSpace(hex) && HexPattern().IsMatch(hex.Trim());

    public static string Normalize(string? hex)
    {
        if (!IsValid(hex))
            return Default;

        var trimmed = hex!.Trim().TrimStart('#');
        return "#" + trimmed.ToUpperInvariant();
    }
}
```

```csharp
// src/Memoria.App/Theming/SystemThemeReader.cs
using Microsoft.Win32;

namespace Memoria.App.Theming;

public static class SystemThemeReader
{
    public const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string AppsUseLightThemeValue = "AppsUseLightTheme";

    public static bool ParseAppsUseLightTheme(object? registryValue, bool fallbackIsLight = true) => registryValue switch
    {
        int i => i != 0,
        long l => l != 0,
        _ => fallbackIsLight,
    };

    public static bool ReadIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
        var value = key?.GetValue(AppsUseLightThemeValue);
        return ParseAppsUseLightTheme(value, fallbackIsLight: true);
    }
}
```
> `Microsoft.Win32.Registry`는 `net9.0-windows` TFM에 in-box(별도 NuGet 불필요).

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~AccentColorTests|FullyQualifiedName~SystemThemeReaderTests"
```
예상: `Passed!  - Failed: 0, Passed: 16` (AccentColor 8+4+1, SystemThemeReader 2+1+2+1+1 → 합 16).

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/Theming/AccentColor.cs src/Memoria.App/Theming/SystemThemeReader.cs tests/Memoria.Tests/Theming/AccentColorTests.cs tests/Memoria.Tests/Theming/SystemThemeReaderTests.cs
git commit -m "feat(app): add accent color validation and system theme registry parser

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: 테마 리소스 사전(팔레트 8종) + App.xaml MergedDictionaries 골격

**Files:**
- Create: `src/Memoria.App/Themes/Base.xaml`(공유 키/스타일), `src/Memoria.App/Themes/Default.Light.xaml`, `Default.Dark.xaml`, `Dark.Light.xaml`, `Dark.Dark.xaml`, `Sepia.Light.xaml`, `Sepia.Dark.xaml`, `Solarized.Light.xaml`, `Solarized.Dark.xaml`
- Modify: `src/Memoria.App/App.xaml`(MergedDictionaries에 Base + 기본 팔레트 슬롯)
- Test: 자동 테스트 없음(WPF 리소스 로딩/시각) → **빌드 검증 + 수동 검증 체크포인트**. 색 결정 로직은 Task 1에서 자동 검증됨.

**Interfaces:**
- Consumes: Task 1 `ThemeResolver.ResolvePaletteUri` 가 만드는 파일명 규약(`Themes/{Preset}.{Variant}.xaml`).
- Produces: 모든 팔레트가 **계약 §10의 17개 브러시 키 전부**를 동일하게 정의한다(단일 진리원천 — M2/M3/M4/M5/M9 View가 이 키만 `DynamicResource`로 참조). 키 이름은 계약 §10과 **정확히 일치**해야 한다(접미사 `Brush` 형태 금지, `Brush.*` 점-구분 키 사용):
  - `Brush.WindowBackground`, `Brush.Surface`, `Brush.SidebarBackground`, `Brush.ToolbarBackground`, `Brush.EditorBackground`, `Brush.Foreground`, `Brush.SecondaryForeground`, `Brush.Border`, `Brush.ListItemHover`, `Brush.ListItemSelected`, `Brush.Accent`, `Brush.AccentForeground`, `Brush.StrikethroughForeground`, `Brush.UnclassifiedHighlight`, `Brush.WarningBackground`, `Brush.WarningBorder`, `Brush.WarningForeground`.
  - 누락 키가 하나라도 있으면 해당 키를 `DynamicResource`로 참조하는 다른 마일스톤 View가 런타임에 빈/투명으로 렌더되므로, **17개 전부**를 모든 팔레트에 반드시 둔다.
  - `Brush.Accent`는 팔레트가 기본값을 정의하되, 런타임에 `ThemeService`가 사용자 강조색으로 **덮어쓴다**(Task 5).

- [ ] **Step 1: 공유 사전 Base.xaml 작성(스타일은 DynamicResource만 참조)**

```xml
<!-- src/Memoria.App/Themes/Base.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 색이 아닌, 색에 의존하는 공유 스타일만 둔다. 색 값 자체는 팔레트 사전에 있다. -->
    <Style x:Key="MemoriaWindowStyle" TargetType="Window">
        <Setter Property="Background" Value="{DynamicResource Brush.WindowBackground}" />
        <Setter Property="Foreground" Value="{DynamicResource Brush.Foreground}" />
    </Style>

    <Style TargetType="TextBlock">
        <Setter Property="Foreground" Value="{DynamicResource Brush.Foreground}" />
    </Style>

    <Style x:Key="AccentButtonStyle" TargetType="Button">
        <Setter Property="Background" Value="{DynamicResource Brush.Accent}" />
        <Setter Property="Foreground" Value="{DynamicResource Brush.AccentForeground}" />
        <Setter Property="BorderBrush" Value="{DynamicResource Brush.Border}" />
        <Setter Property="Padding" Value="10,4" />
    </Style>
</ResourceDictionary>
```

- [ ] **Step 2: 8개 팔레트 사전 작성(동일 키, 값만 다름)**

> 각 파일은 계약 §10의 **17개 `Brush.*` 키를 모두** 정의해야 한다(누락 시 `DynamicResource`가 빈 값으로 렌더). 아래는 `Default.Light.xaml` 전체 예시이며, 나머지 7개는 동일 키 구조(17개)에 색 값만 팔레트별로 교체한다.

```xml
<!-- src/Memoria.App/Themes/Default.Light.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Brush.WindowBackground"        Color="#FFFFFF" />
    <SolidColorBrush x:Key="Brush.Surface"                 Color="#F3F3F3" />
    <SolidColorBrush x:Key="Brush.SidebarBackground"       Color="#EAEAEA" />
    <SolidColorBrush x:Key="Brush.ToolbarBackground"       Color="#F7F7F7" />
    <SolidColorBrush x:Key="Brush.EditorBackground"        Color="#FFFFFF" />
    <SolidColorBrush x:Key="Brush.Foreground"              Color="#1A1A1A" />
    <SolidColorBrush x:Key="Brush.SecondaryForeground"     Color="#666666" />
    <SolidColorBrush x:Key="Brush.Border"                  Color="#D0D0D0" />
    <SolidColorBrush x:Key="Brush.ListItemHover"           Color="#E5F1FB" />
    <SolidColorBrush x:Key="Brush.ListItemSelected"        Color="#CCE4F7" />
    <SolidColorBrush x:Key="Brush.Accent"                  Color="#0078D4" />
    <SolidColorBrush x:Key="Brush.AccentForeground"        Color="#FFFFFF" />
    <SolidColorBrush x:Key="Brush.StrikethroughForeground" Color="#999999" />
    <SolidColorBrush x:Key="Brush.UnclassifiedHighlight"   Color="#FFF4CE" />
    <SolidColorBrush x:Key="Brush.WarningBackground"       Color="#FFF4CE" />
    <SolidColorBrush x:Key="Brush.WarningBorder"           Color="#E6B800" />
    <SolidColorBrush x:Key="Brush.WarningForeground"       Color="#6B5300" />
</ResourceDictionary>
```

`Default.Dark.xaml` (다크 기본):
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Brush.WindowBackground"        Color="#1E1E1E" />
    <SolidColorBrush x:Key="Brush.Surface"                 Color="#252526" />
    <SolidColorBrush x:Key="Brush.SidebarBackground"       Color="#2D2D30" />
    <SolidColorBrush x:Key="Brush.ToolbarBackground"       Color="#333333" />
    <SolidColorBrush x:Key="Brush.EditorBackground"        Color="#1E1E1E" />
    <SolidColorBrush x:Key="Brush.Foreground"              Color="#F0F0F0" />
    <SolidColorBrush x:Key="Brush.SecondaryForeground"     Color="#A0A0A0" />
    <SolidColorBrush x:Key="Brush.Border"                  Color="#3F3F46" />
    <SolidColorBrush x:Key="Brush.ListItemHover"           Color="#2A2D2E" />
    <SolidColorBrush x:Key="Brush.ListItemSelected"        Color="#094771" />
    <SolidColorBrush x:Key="Brush.Accent"                  Color="#0A84FF" />
    <SolidColorBrush x:Key="Brush.AccentForeground"        Color="#FFFFFF" />
    <SolidColorBrush x:Key="Brush.StrikethroughForeground" Color="#777777" />
    <SolidColorBrush x:Key="Brush.UnclassifiedHighlight"   Color="#4A4327" />
    <SolidColorBrush x:Key="Brush.WarningBackground"       Color="#4A3C1A" />
    <SolidColorBrush x:Key="Brush.WarningBorder"           Color="#8A6D1B" />
    <SolidColorBrush x:Key="Brush.WarningForeground"       Color="#F0D88A" />
</ResourceDictionary>
```

`Dark.Light.xaml` / `Dark.Dark.xaml` (프리셋 "dark": light 변형도 약간 어두운 회색조, dark 변형은 더 짙은 흑):
```xml
<!-- Dark.Light.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Brush.WindowBackground"        Color="#E8E8E8" />
    <SolidColorBrush x:Key="Brush.Surface"                 Color="#DCDCDC" />
    <SolidColorBrush x:Key="Brush.SidebarBackground"       Color="#C8C8C8" />
    <SolidColorBrush x:Key="Brush.ToolbarBackground"       Color="#D2D2D2" />
    <SolidColorBrush x:Key="Brush.EditorBackground"        Color="#E8E8E8" />
    <SolidColorBrush x:Key="Brush.Foreground"              Color="#101010" />
    <SolidColorBrush x:Key="Brush.SecondaryForeground"     Color="#555555" />
    <SolidColorBrush x:Key="Brush.Border"                  Color="#B0B0B0" />
    <SolidColorBrush x:Key="Brush.ListItemHover"           Color="#D0D8E0" />
    <SolidColorBrush x:Key="Brush.ListItemSelected"        Color="#B8C8D8" />
    <SolidColorBrush x:Key="Brush.Accent"                  Color="#005A9E" />
    <SolidColorBrush x:Key="Brush.AccentForeground"        Color="#FFFFFF" />
    <SolidColorBrush x:Key="Brush.StrikethroughForeground" Color="#888888" />
    <SolidColorBrush x:Key="Brush.UnclassifiedHighlight"   Color="#F0E6C0" />
    <SolidColorBrush x:Key="Brush.WarningBackground"       Color="#F0E6C0" />
    <SolidColorBrush x:Key="Brush.WarningBorder"           Color="#C8A030" />
    <SolidColorBrush x:Key="Brush.WarningForeground"       Color="#5C4A14" />
</ResourceDictionary>
```
```xml
<!-- Dark.Dark.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Brush.WindowBackground"        Color="#121212" />
    <SolidColorBrush x:Key="Brush.Surface"                 Color="#1A1A1A" />
    <SolidColorBrush x:Key="Brush.SidebarBackground"       Color="#202020" />
    <SolidColorBrush x:Key="Brush.ToolbarBackground"       Color="#262626" />
    <SolidColorBrush x:Key="Brush.EditorBackground"        Color="#121212" />
    <SolidColorBrush x:Key="Brush.Foreground"              Color="#EAEAEA" />
    <SolidColorBrush x:Key="Brush.SecondaryForeground"     Color="#909090" />
    <SolidColorBrush x:Key="Brush.Border"                  Color="#303030" />
    <SolidColorBrush x:Key="Brush.ListItemHover"           Color="#242424" />
    <SolidColorBrush x:Key="Brush.ListItemSelected"        Color="#0A3A5C" />
    <SolidColorBrush x:Key="Brush.Accent"                  Color="#3794FF" />
    <SolidColorBrush x:Key="Brush.AccentForeground"        Color="#FFFFFF" />
    <SolidColorBrush x:Key="Brush.StrikethroughForeground" Color="#6A6A6A" />
    <SolidColorBrush x:Key="Brush.UnclassifiedHighlight"   Color="#3A3320" />
    <SolidColorBrush x:Key="Brush.WarningBackground"       Color="#3A2E12" />
    <SolidColorBrush x:Key="Brush.WarningBorder"           Color="#7A5E15" />
    <SolidColorBrush x:Key="Brush.WarningForeground"       Color="#E8CE80" />
</ResourceDictionary>
```

`Sepia.Light.xaml` / `Sepia.Dark.xaml`:
```xml
<!-- Sepia.Light.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Brush.WindowBackground"        Color="#F4ECD8" />
    <SolidColorBrush x:Key="Brush.Surface"                 Color="#EDE4CC" />
    <SolidColorBrush x:Key="Brush.SidebarBackground"       Color="#E4D7B8" />
    <SolidColorBrush x:Key="Brush.ToolbarBackground"       Color="#EAE0C6" />
    <SolidColorBrush x:Key="Brush.EditorBackground"        Color="#F4ECD8" />
    <SolidColorBrush x:Key="Brush.Foreground"              Color="#433422" />
    <SolidColorBrush x:Key="Brush.SecondaryForeground"     Color="#7A6A4F" />
    <SolidColorBrush x:Key="Brush.Border"                  Color="#CBB78F" />
    <SolidColorBrush x:Key="Brush.ListItemHover"           Color="#E8DBBA" />
    <SolidColorBrush x:Key="Brush.ListItemSelected"        Color="#DBC79A" />
    <SolidColorBrush x:Key="Brush.Accent"                  Color="#A86B2D" />
    <SolidColorBrush x:Key="Brush.AccentForeground"        Color="#FFFFFF" />
    <SolidColorBrush x:Key="Brush.StrikethroughForeground" Color="#9A8B6A" />
    <SolidColorBrush x:Key="Brush.UnclassifiedHighlight"   Color="#EAD9A0" />
    <SolidColorBrush x:Key="Brush.WarningBackground"       Color="#EAD9A0" />
    <SolidColorBrush x:Key="Brush.WarningBorder"           Color="#B58A3C" />
    <SolidColorBrush x:Key="Brush.WarningForeground"       Color="#5C4318" />
</ResourceDictionary>
```
```xml
<!-- Sepia.Dark.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Brush.WindowBackground"        Color="#2B2419" />
    <SolidColorBrush x:Key="Brush.Surface"                 Color="#332B1E" />
    <SolidColorBrush x:Key="Brush.SidebarBackground"       Color="#3C3324" />
    <SolidColorBrush x:Key="Brush.ToolbarBackground"       Color="#403626" />
    <SolidColorBrush x:Key="Brush.EditorBackground"        Color="#2B2419" />
    <SolidColorBrush x:Key="Brush.Foreground"              Color="#E8DBBF" />
    <SolidColorBrush x:Key="Brush.SecondaryForeground"     Color="#B0A081" />
    <SolidColorBrush x:Key="Brush.Border"                  Color="#534734" />
    <SolidColorBrush x:Key="Brush.ListItemHover"           Color="#3A3124" />
    <SolidColorBrush x:Key="Brush.ListItemSelected"        Color="#5A4A2E" />
    <SolidColorBrush x:Key="Brush.Accent"                  Color="#D9A05B" />
    <SolidColorBrush x:Key="Brush.AccentForeground"        Color="#2B2419" />
    <SolidColorBrush x:Key="Brush.StrikethroughForeground" Color="#8A7A5C" />
    <SolidColorBrush x:Key="Brush.UnclassifiedHighlight"   Color="#4A3E26" />
    <SolidColorBrush x:Key="Brush.WarningBackground"       Color="#463A20" />
    <SolidColorBrush x:Key="Brush.WarningBorder"           Color="#8A6D2E" />
    <SolidColorBrush x:Key="Brush.WarningForeground"       Color="#E8C888" />
</ResourceDictionary>
```

`Solarized.Light.xaml` / `Solarized.Dark.xaml` (Ethan Schoonover Solarized 팔레트):
```xml
<!-- Solarized.Light.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Brush.WindowBackground"        Color="#FDF6E3" />
    <SolidColorBrush x:Key="Brush.Surface"                 Color="#EEE8D5" />
    <SolidColorBrush x:Key="Brush.SidebarBackground"       Color="#E4DBC0" />
    <SolidColorBrush x:Key="Brush.ToolbarBackground"       Color="#EEE8D5" />
    <SolidColorBrush x:Key="Brush.EditorBackground"        Color="#FDF6E3" />
    <SolidColorBrush x:Key="Brush.Foreground"              Color="#073642" />
    <SolidColorBrush x:Key="Brush.SecondaryForeground"     Color="#657B83" />
    <SolidColorBrush x:Key="Brush.Border"                  Color="#CFC8B0" />
    <SolidColorBrush x:Key="Brush.ListItemHover"           Color="#EAE3CC" />
    <SolidColorBrush x:Key="Brush.ListItemSelected"        Color="#D7CFB2" />
    <SolidColorBrush x:Key="Brush.Accent"                  Color="#268BD2" />
    <SolidColorBrush x:Key="Brush.AccentForeground"        Color="#FDF6E3" />
    <SolidColorBrush x:Key="Brush.StrikethroughForeground" Color="#93A1A1" />
    <SolidColorBrush x:Key="Brush.UnclassifiedHighlight"   Color="#EEE2B0" />
    <SolidColorBrush x:Key="Brush.WarningBackground"       Color="#EEE2B0" />
    <SolidColorBrush x:Key="Brush.WarningBorder"           Color="#B58900" />
    <SolidColorBrush x:Key="Brush.WarningForeground"       Color="#586E75" />
</ResourceDictionary>
```
```xml
<!-- Solarized.Dark.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Brush.WindowBackground"        Color="#002B36" />
    <SolidColorBrush x:Key="Brush.Surface"                 Color="#073642" />
    <SolidColorBrush x:Key="Brush.SidebarBackground"       Color="#04303A" />
    <SolidColorBrush x:Key="Brush.ToolbarBackground"       Color="#073642" />
    <SolidColorBrush x:Key="Brush.EditorBackground"        Color="#002B36" />
    <SolidColorBrush x:Key="Brush.Foreground"              Color="#EEE8D5" />
    <SolidColorBrush x:Key="Brush.SecondaryForeground"     Color="#93A1A1" />
    <SolidColorBrush x:Key="Brush.Border"                  Color="#0B4451" />
    <SolidColorBrush x:Key="Brush.ListItemHover"           Color="#093E49" />
    <SolidColorBrush x:Key="Brush.ListItemSelected"        Color="#0E5360" />
    <SolidColorBrush x:Key="Brush.Accent"                  Color="#268BD2" />
    <SolidColorBrush x:Key="Brush.AccentForeground"        Color="#002B36" />
    <SolidColorBrush x:Key="Brush.StrikethroughForeground" Color="#586E75" />
    <SolidColorBrush x:Key="Brush.UnclassifiedHighlight"   Color="#14424A" />
    <SolidColorBrush x:Key="Brush.WarningBackground"       Color="#133A42" />
    <SolidColorBrush x:Key="Brush.WarningBorder"           Color="#B58900" />
    <SolidColorBrush x:Key="Brush.WarningForeground"       Color="#E8C547" />
</ResourceDictionary>
```

- [ ] **Step 3: App.xaml MergedDictionaries 골격 구성(Base[0] + 팔레트 슬롯[1])**

```xml
<!-- src/Memoria.App/App.xaml -->
<Application x:Class="Memoria.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- [0] 공유 스타일 -->
                <ResourceDictionary Source="Themes/Base.xaml" />
                <!-- [1] 테마 팔레트 슬롯: ThemeService가 런타임에 교체(기본 default/light) -->
                <ResourceDictionary Source="Themes/Default.Light.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```
> 슬롯 인덱스 규약: **[0]=Base, [1]=팔레트**. Task 5 `ThemeApplier`는 항상 인덱스 1을 교체한다. M2가 이미 App.xaml에 다른 MergedDictionaries(예: 컨트롤 스타일)를 두었다면, 팔레트는 **항상 Base 다음(마지막 직전 또는 명시 인덱스)** 으로 고정하고 ThemeApplier가 그 인덱스를 사용하도록 맞춘다(없는 이름 발명 금지, 기존 구조에 슬롯만 끼움).

- [ ] **Step 4: 빌드로 XAML 컴파일/리소스 포함 검증**

```
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\Memoria.sln"
```
예상: `Build succeeded`(9개 ResourceDictionary가 `Page`로 컴파일되고 App.xaml MergedDictionaries 해석 성공). 키 누락은 빌드가 아닌 런타임 바인딩 실패이므로 Step 5에서 시각 확인.

- [ ] **Step 5: 수동 검증 체크포인트 — 팔레트 시각 확인**
  Task 8 설정 화면 배선 전이라도, App.xaml 슬롯[1]의 `Source`를 임시로 각 팔레트로 바꿔가며 `dotnet.exe run --project "C:\...\src\Memoria.App"` 실행 후 눈으로 확인:
  1. 8개 팔레트 각각에서 메인 창 배경/사이드바/툴바/전경 텍스트/선택 항목 색이 **모두 채워지는가**(빈 색/투명 없음 = 키 누락 없음).
  2. 라이트 변형은 어두운 글자/밝은 배경, 다크 변형은 밝은 글자/어두운 배경으로 **가독성**이 확보되는가.
  3. 트레이 컨텍스트 메뉴(M6) 항목 색이 팔레트를 따라가는가(서드파티 컨트롤 테마 적용 — `DynamicResource` 키 사용 확인).
  확인 후 App.xaml 슬롯[1]은 기본 `Themes/Default.Light.xaml`로 되돌려 둔다.

- [ ] **Step 6: Commit**

```
git add src/Memoria.App/Themes/ src/Memoria.App/App.xaml
git commit -m "feat(app): add 8 theme palette dictionaries and merged-dictionary slot

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: 테마 적용기(IThemeApplier) + 시스템 테마 소스(ISystemThemeSource)

**Files:**
- Create: `src/Memoria.App/Theming/IThemeApplier.cs`, `src/Memoria.App/Theming/WpfThemeApplier.cs`, `src/Memoria.App/Theming/ISystemThemeSource.cs`, `src/Memoria.App/Theming/SystemEventsThemeSource.cs`
- Test: `tests/Memoria.Tests/Theming/SystemEventsThemeSourceTests.cs`(생성/Dispose 비예외 + IsLight 위임). WPF 리소스 교체는 Application 컨텍스트 필요 → **수동 검증은 Task 8에 통합**.

**Interfaces:**
- Consumes: Task 1 `ThemeResolver`(URI), Task 2 `SystemThemeReader.ReadIsLight`, `AccentColor.Normalize`.
- Produces:
  - `interface IThemeApplier` { `void ApplyPalette(Uri paletteUri)`(MergedDictionaries 슬롯[1] 교체); `void ApplyAccent(string accentHex)`(`Brush.Accent` 리소스 덮어쓰기) }.
  - `sealed class WpfThemeApplier : IThemeApplier`(`Application.Current.Resources` 조작).
  - `interface ISystemThemeSource : IDisposable` { `bool IsLight()`; `event EventHandler? SystemThemeChanged` }.
  - `sealed class SystemEventsThemeSource : ISystemThemeSource`(`SystemEvents.UserPreferenceChanged` 구독, M6 message-only 창과 동일 프로세스 수명).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/Theming/SystemEventsThemeSourceTests.cs
using FluentAssertions;
using Memoria.App.Theming;
using Xunit;

namespace Memoria.Tests.Theming;

public class SystemEventsThemeSourceTests
{
    [Fact]
    public void IsLight_delegates_to_registry_without_throwing()
    {
        using var source = new SystemEventsThemeSource();
        var act = () => source.IsLight();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_unsubscribes_and_does_not_throw()
    {
        var source = new SystemEventsThemeSource();
        source.Invoking(s => s.Dispose()).Should().NotThrow();
        // 이중 Dispose 안전
        source.Invoking(s => s.Dispose()).Should().NotThrow();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~SystemEventsThemeSourceTests"
```
예상 실패: `error CS0246: ... 'SystemEventsThemeSource' ...`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/Theming/IThemeApplier.cs
using System;

namespace Memoria.App.Theming;

public interface IThemeApplier
{
    void ApplyPalette(Uri paletteUri);
    void ApplyAccent(string accentHex);
}
```

```csharp
// src/Memoria.App/Theming/WpfThemeApplier.cs
using System;
using System.Windows;
using System.Windows.Media;

namespace Memoria.App.Theming;

public sealed class WpfThemeApplier : IThemeApplier
{
    // App.xaml 규약: MergedDictionaries[0]=Base, [1]=팔레트 슬롯.
    private const int PaletteSlotIndex = 1;

    public void ApplyPalette(Uri paletteUri)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = paletteUri };

        if (dictionaries.Count > PaletteSlotIndex)
            dictionaries[PaletteSlotIndex] = palette; // 1개만 교체 → 깜빡임 최소화
        else
            dictionaries.Add(palette);
    }

    public void ApplyAccent(string accentHex)
    {
        var color = (Color)ColorConverter.ConvertFromString(AccentColor.Normalize(accentHex));
        Application.Current.Resources["Brush.Accent"] = new SolidColorBrush(color);
    }
}
```

```csharp
// src/Memoria.App/Theming/ISystemThemeSource.cs
using System;

namespace Memoria.App.Theming;

public interface ISystemThemeSource : IDisposable
{
    bool IsLight();
    event EventHandler? SystemThemeChanged;
}
```

```csharp
// src/Memoria.App/Theming/SystemEventsThemeSource.cs
using System;
using Microsoft.Win32;

namespace Memoria.App.Theming;

public sealed class SystemEventsThemeSource : ISystemThemeSource
{
    private bool _disposed;

    public event EventHandler? SystemThemeChanged;

    public SystemEventsThemeSource()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsLight() => SystemThemeReader.ReadIsLight();

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // General 범주에 테마(색) 변경이 포함된다.
        if (e.Category == UserPreferenceCategory.General)
            SystemThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _disposed = true;
    }
}
```
> `Microsoft.Win32.SystemEvents`는 `net9.0-windows`(WPF) 워크로드에 포함된다. `UserPreferenceChanged`는 내부적으로 WM_SETTINGCHANGE를 수신하는 숨은 창을 사용하므로 M6의 message-only 창과 동일하게 프로세스 수명 동안 유효하다.

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~SystemEventsThemeSourceTests"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/Theming/IThemeApplier.cs src/Memoria.App/Theming/WpfThemeApplier.cs src/Memoria.App/Theming/ISystemThemeSource.cs src/Memoria.App/Theming/SystemEventsThemeSource.cs tests/Memoria.Tests/Theming/SystemEventsThemeSourceTests.cs
git commit -m "feat(app): add WPF theme applier and SystemEvents-backed system theme source

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: 테마 코디네이션 서비스 (IThemeService) + 공유 테스트 페이크

**Files:**
- Create: `src/Memoria.App/Theming/IThemeService.cs`, `src/Memoria.App/Theming/ThemeService.cs`, `tests/Memoria.Tests/Fakes/InMemorySettingsRepository.cs`(공유 페이크), `tests/Memoria.Tests/Theming/FakeThemeCollaborators.cs`
- Test: `tests/Memoria.Tests/Theming/ThemeServiceTests.cs`

**Interfaces:**
- Consumes:
  - `Memoria.Core.Data.ISettingsRepository` { `GetOrDefault(string, string)`, `Set(string, string)` } (계약 §4).
  - `Memoria.Core.SettingsKeys` { `ThemeMode`, `ThemePreset`, `ThemeAccent` } (계약 §6).
  - `Memoria.Core.Models.ThemeMode`.
  - Task 1 `ThemeResolver`, Task 2 `AccentColor`, Task 4 `IThemeApplier`/`ISystemThemeSource`.
- Produces:
  - `interface IThemeService` { `ThemeMode Mode { get; }`; `string Preset { get; }`; `string Accent { get; }`; `void Initialize()`; `void Apply(ThemeMode mode, string preset, string accent)`; `event EventHandler? ThemeChanged` }.
  - `sealed class ThemeService : IThemeService, IDisposable`. mode 문자열 변환 `static ThemeMode ParseMode(string?)` / `static string ModeToString(ThemeMode)`.
  - `InMemorySettingsRepository`(테스트 전용 `ISettingsRepository` 구현, 이후 Task 6/7에서 재사용).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/Fakes/InMemorySettingsRepository.cs
using System.Collections.Generic;
using Memoria.Core.Data;

namespace Memoria.Tests.Fakes;

public sealed class InMemorySettingsRepository : ISettingsRepository
{
    private readonly Dictionary<string, string> _store = new();

    public string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;

    public string GetOrDefault(string key, string fallback)
        => _store.TryGetValue(key, out var v) ? v : fallback;

    public void Set(string key, string value) => _store[key] = value;

    public IReadOnlyDictionary<string, string> GetAll() => _store;
}
```

```csharp
// tests/Memoria.Tests/Theming/FakeThemeCollaborators.cs
using System;
using Memoria.App.Theming;

namespace Memoria.Tests.Theming;

public sealed class FakeThemeApplier : IThemeApplier
{
    public Uri? LastPalette { get; private set; }
    public string? LastAccent { get; private set; }
    public int PaletteApplyCount { get; private set; }

    public void ApplyPalette(Uri paletteUri)
    {
        LastPalette = paletteUri;
        PaletteApplyCount++;
    }

    public void ApplyAccent(string accentHex) => LastAccent = accentHex;
}

public sealed class FakeSystemThemeSource : ISystemThemeSource
{
    public bool Light { get; set; } = true;
    public event EventHandler? SystemThemeChanged;

    public bool IsLight() => Light;
    public void RaiseChanged() => SystemThemeChanged?.Invoke(this, EventArgs.Empty);
    public void Dispose() { }
}
```

```csharp
// tests/Memoria.Tests/Theming/ThemeServiceTests.cs
using FluentAssertions;
using Memoria.App.Theming;
using Memoria.Core;
using Memoria.Core.Models;
using Memoria.Tests.Fakes;
using Xunit;

namespace Memoria.Tests.Theming;

public class ThemeServiceTests
{
    private static (ThemeService svc, FakeThemeApplier applier, FakeSystemThemeSource sys, InMemorySettingsRepository settings)
        Create(bool systemLight = true)
    {
        var settings = new InMemorySettingsRepository();
        var applier = new FakeThemeApplier();
        var sys = new FakeSystemThemeSource { Light = systemLight };
        var svc = new ThemeService(settings, applier, sys);
        return (svc, applier, sys, settings);
    }

    [Fact]
    public void Initialize_uses_defaults_and_applies_palette_and_accent()
    {
        var (svc, applier, _, _) = Create(systemLight: true);

        svc.Initialize();

        svc.Mode.Should().Be(ThemeMode.System);
        svc.Preset.Should().Be("default");
        svc.Accent.Should().Be("#0078D4");
        applier.LastPalette!.OriginalString.Should().Be("Themes/Default.Light.xaml");
        applier.LastAccent.Should().Be("#0078D4");
    }

    [Fact]
    public void Initialize_reads_persisted_values()
    {
        var (svc, applier, _, settings) = Create(systemLight: true);
        settings.Set(SettingsKeys.ThemeMode, "dark");
        settings.Set(SettingsKeys.ThemePreset, "sepia");
        settings.Set(SettingsKeys.ThemeAccent, "ff8800");

        svc.Initialize();

        svc.Mode.Should().Be(ThemeMode.Dark);
        applier.LastPalette!.OriginalString.Should().Be("Themes/Sepia.Dark.xaml");
        applier.LastAccent.Should().Be("#FF8800");
    }

    [Fact]
    public void Apply_persists_normalized_settings()
    {
        var (svc, _, _, settings) = Create();

        svc.Apply(ThemeMode.Light, "Solarized", "00aaff");

        settings.Get(SettingsKeys.ThemeMode).Should().Be("light");
        settings.Get(SettingsKeys.ThemePreset).Should().Be("solarized");
        settings.Get(SettingsKeys.ThemeAccent).Should().Be("#00AAFF");
    }

    [Fact]
    public void Apply_fixed_mode_ignores_system_value()
    {
        var (svc, applier, _, _) = Create(systemLight: true);

        svc.Apply(ThemeMode.Dark, "default", "#0078D4");

        applier.LastPalette!.OriginalString.Should().Be("Themes/Default.Dark.xaml");
    }

    [Fact]
    public void System_change_reapplies_only_when_mode_is_system()
    {
        var (svc, applier, sys, _) = Create(systemLight: true);
        svc.Apply(ThemeMode.System, "default", "#0078D4");
        applier.LastPalette!.OriginalString.Should().Be("Themes/Default.Light.xaml");

        sys.Light = false;
        sys.RaiseChanged();

        applier.LastPalette!.OriginalString.Should().Be("Themes/Default.Dark.xaml");
    }

    [Fact]
    public void System_change_is_ignored_for_fixed_mode()
    {
        var (svc, applier, sys, _) = Create(systemLight: true);
        svc.Apply(ThemeMode.Light, "default", "#0078D4");
        var countAfterApply = applier.PaletteApplyCount;

        sys.Light = false;
        sys.RaiseChanged();

        applier.PaletteApplyCount.Should().Be(countAfterApply); // 재적용 없음
        applier.LastPalette!.OriginalString.Should().Be("Themes/Default.Light.xaml");
    }

    [Fact]
    public void Apply_raises_ThemeChanged()
    {
        var (svc, _, _, _) = Create();
        var raised = false;
        svc.ThemeChanged += (_, _) => raised = true;

        svc.Apply(ThemeMode.Dark, "default", "#0078D4");

        raised.Should().BeTrue();
    }

    [Theory]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("DARK", ThemeMode.Dark)]
    [InlineData("system", ThemeMode.System)]
    [InlineData("garbage", ThemeMode.System)]
    [InlineData(null, ThemeMode.System)]
    public void ParseMode_maps_strings(string? input, ThemeMode expected)
    {
        ThemeService.ParseMode(input).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ThemeServiceTests"
```
예상 실패: `error CS0246: ... 'ThemeService'/'IThemeService' ...`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/Theming/IThemeService.cs
using System;
using Memoria.Core.Models;

namespace Memoria.App.Theming;

public interface IThemeService
{
    ThemeMode Mode { get; }
    string Preset { get; }
    string Accent { get; }
    void Initialize();
    void Apply(ThemeMode mode, string preset, string accent);
    event EventHandler? ThemeChanged;
}
```

```csharp
// src/Memoria.App/Theming/ThemeService.cs
using System;
using Memoria.Core;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.App.Theming;

public sealed class ThemeService : IThemeService, IDisposable
{
    private readonly ISettingsRepository _settings;
    private readonly IThemeApplier _applier;
    private readonly ISystemThemeSource _systemTheme;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;
    public string Preset { get; private set; } = "default";
    public string Accent { get; private set; } = AccentColor.Default;

    public event EventHandler? ThemeChanged;

    public ThemeService(ISettingsRepository settings, IThemeApplier applier, ISystemThemeSource systemTheme)
    {
        _settings = settings;
        _applier = applier;
        _systemTheme = systemTheme;
        _systemTheme.SystemThemeChanged += OnSystemThemeChanged;
    }

    public void Initialize()
    {
        var mode = ParseMode(_settings.GetOrDefault(SettingsKeys.ThemeMode, "system"));
        var preset = ThemeResolver.NormalizePreset(_settings.GetOrDefault(SettingsKeys.ThemePreset, "default"));
        var accent = AccentColor.Normalize(_settings.GetOrDefault(SettingsKeys.ThemeAccent, AccentColor.Default));
        ApplyInternal(mode, preset, accent, persist: false);
    }

    public void Apply(ThemeMode mode, string preset, string accent)
        => ApplyInternal(mode, ThemeResolver.NormalizePreset(preset), AccentColor.Normalize(accent), persist: true);

    private void ApplyInternal(ThemeMode mode, string preset, string accent, bool persist)
    {
        Mode = mode;
        Preset = preset;
        Accent = accent;

        var uri = ThemeResolver.ResolvePaletteUri(mode, preset, _systemTheme.IsLight());
        _applier.ApplyPalette(uri);
        _applier.ApplyAccent(accent);

        if (persist)
        {
            _settings.Set(SettingsKeys.ThemeMode, ModeToString(mode));
            _settings.Set(SettingsKeys.ThemePreset, preset);
            _settings.Set(SettingsKeys.ThemeAccent, accent);
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        if (Mode == ThemeMode.System)
            ApplyInternal(Mode, Preset, Accent, persist: false);
    }

    public static ThemeMode ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => ThemeMode.Light,
        "dark" => ThemeMode.Dark,
        _ => ThemeMode.System,
    };

    public static string ModeToString(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "light",
        ThemeMode.Dark => "dark",
        _ => "system",
    };

    public void Dispose() => _systemTheme.SystemThemeChanged -= OnSystemThemeChanged;
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ThemeServiceTests"
```
예상: `Passed!  - Failed: 0, Passed: 12` (Fact 7 + Theory 5).

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/Theming/IThemeService.cs src/Memoria.App/Theming/ThemeService.cs tests/Memoria.Tests/Fakes/InMemorySettingsRepository.cs tests/Memoria.Tests/Theming/FakeThemeCollaborators.cs tests/Memoria.Tests/Theming/ThemeServiceTests.cs
git commit -m "feat(app): add theme service coordinating palette/accent/system mode

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: SettingsViewModel (테마/주간보고/단축키/앱/백업·휴지통)

**Files:**
- Create: `src/Memoria.App/ViewModels/SettingsViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes:
  - `Memoria.Core.Data.ISettingsRepository`(§4).
  - `Memoria.Core.SettingsKeys`(§6): `ThemeMode`, `ThemePreset`, `ThemeAccent`, `ReporterName`, `FormatATaskHeader`, `FormatAIssueHeader`, `FormatBTitleWord`, `FormatBIssueHeader`, `ReportIndent`, `IncludeDoneOnly`, `HotkeyNewNote`, `Autostart`, `CloseToTray`, `BackupRetentionCount`, `TrashRetentionDays`.
  - `Memoria.Core.Models.ThemeMode`.
  - Task 5 `IThemeService.Apply`(테마 즉시 적용+영속).
  - M6 `Memoria.App.Windows.HotkeyParser.TryParse`(단축키 검증), `Memoria.App.Windows.IAutostartService`(자동시작 토글).
- Produces:
  - `sealed partial class SettingsViewModel : ObservableObject` with `[ObservableProperty]`:
    - 테마: `ThemeMode Mode`, `string Preset`, `string Accent`(변경 즉시 `IThemeService.Apply`).
    - 주간보고: `string ReporterName`, `TaskHeaderA`, `IssueHeaderA`, `TitleWordB`, `IssueHeaderB`, `ReportIndent`, `bool IncludeDoneOnly`.
    - 앱: `string HotkeyNewNote`, `bool Autostart`, `bool CloseToTray`.
    - 보존: `int BackupRetentionCount`, `int TrashRetentionDays`.
  - 검증 읽기전용: `bool IsHotkeyValid`, `bool IsAccentValid`, `bool CanSave`.
  - `IRelayCommand SaveCommand`(주간보고/단축키/앱/보존 키 영속 + 자동시작 토글; 테마는 변경 즉시 영속됨).
  - `string[] AvailablePresets`(= `ThemeResolver.Presets`).

> 적용 정책(명시): **테마 3축(Mode/Preset/Accent)** 은 사용자가 즉각 미리보기 가능하도록 **변경 즉시 `IThemeService.Apply`(영속 포함)**. 나머지(주간보고/단축키/앱/보존)는 `SaveCommand`로 일괄 영속. 무효한 강조색/단축키는 적용/저장하지 않는다(`CanSave=false`).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/ViewModels/SettingsViewModelTests.cs
using FluentAssertions;
using Memoria.App.Theming;
using Memoria.App.ViewModels;
using Memoria.App.Windows;
using Memoria.Core;
using Memoria.Core.Models;
using Memoria.Tests.Fakes;
using Memoria.Tests.Theming;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class SettingsViewModelTests
{
    private sealed class FakeAutostartService : IAutostartService
    {
        public bool Enabled { get; private set; }
        public bool IsEnabled() => Enabled;
        public void Enable() => Enabled = true;
        public void Disable() => Enabled = false;
    }

    private static (SettingsViewModel vm, InMemorySettingsRepository settings, FakeAutostartService autostart, FakeThemeApplier applier)
        Create()
    {
        var settings = new InMemorySettingsRepository();
        var applier = new FakeThemeApplier();
        var theme = new ThemeService(settings, applier, new FakeSystemThemeSource { Light = true });
        theme.Initialize();
        var autostart = new FakeAutostartService();
        var vm = new SettingsViewModel(settings, theme, autostart);
        return (vm, settings, autostart, applier);
    }

    [Fact]
    public void Loads_defaults_when_settings_empty()
    {
        var (vm, _, _, _) = Create();

        vm.Mode.Should().Be(ThemeMode.System);
        vm.Preset.Should().Be("default");
        vm.Accent.Should().Be("#0078D4");
        vm.ReporterName.Should().Be("이승현");
        vm.TaskHeaderA.Should().Be("[업무 내용]");
        vm.IssueHeaderA.Should().Be("[이슈]");
        vm.TitleWordB.Should().Be("주간 보고");
        vm.IssueHeaderB.Should().Be("* 이슈사항:");
        vm.ReportIndent.Should().Be("\t");
        vm.IncludeDoneOnly.Should().BeFalse();
        vm.HotkeyNewNote.Should().Be("Ctrl+Alt+N");
        vm.Autostart.Should().BeTrue();
        vm.CloseToTray.Should().BeTrue();
        vm.BackupRetentionCount.Should().Be(7);
        vm.TrashRetentionDays.Should().Be(30);
    }

    [Fact]
    public void Loads_persisted_values()
    {
        var (vm, settings, _, _) = Create();
        settings.Set(SettingsKeys.ReporterName, "홍길동");
        settings.Set(SettingsKeys.IncludeDoneOnly, "true");
        settings.Set(SettingsKeys.TrashRetentionDays, "14");

        var reloaded = new SettingsViewModel(settings,
            new ThemeService(settings, new FakeThemeApplier(), new FakeSystemThemeSource()),
            new FakeAutostartService());

        reloaded.ReporterName.Should().Be("홍길동");
        reloaded.IncludeDoneOnly.Should().BeTrue();
        reloaded.TrashRetentionDays.Should().Be(14);
    }

    [Fact]
    public void Changing_mode_applies_theme_immediately()
    {
        var (vm, settings, _, applier) = Create();

        vm.Mode = ThemeMode.Dark;

        applier.LastPalette!.OriginalString.Should().Be("Themes/Default.Dark.xaml");
        settings.Get(SettingsKeys.ThemeMode).Should().Be("dark");
    }

    [Fact]
    public void Changing_valid_accent_applies_immediately()
    {
        var (vm, settings, _, applier) = Create();

        vm.Accent = "#FF0000";

        applier.LastAccent.Should().Be("#FF0000");
        settings.Get(SettingsKeys.ThemeAccent).Should().Be("#FF0000");
    }

    [Fact]
    public void Invalid_accent_is_not_applied_and_blocks_save()
    {
        var (vm, settings, _, applier) = Create();
        applier.LastAccent = null;

        vm.Accent = "nope";

        vm.IsAccentValid.Should().BeFalse();
        vm.CanSave.Should().BeFalse();
        applier.LastAccent.Should().BeNull(); // 무효색 미적용
    }

    [Fact]
    public void Invalid_hotkey_blocks_save()
    {
        var (vm, _, _, _) = Create();

        vm.HotkeyNewNote = "Ctrl+";

        vm.IsHotkeyValid.Should().BeFalse();
        vm.CanSave.Should().BeFalse();
    }

    [Fact]
    public void Save_persists_report_app_and_retention_keys()
    {
        var (vm, settings, autostart, _) = Create();
        vm.ReporterName = "김철수";
        vm.TaskHeaderA = "[할 일]";
        vm.IncludeDoneOnly = true;
        vm.HotkeyNewNote = "Ctrl+Shift+M";
        vm.Autostart = false;
        vm.CloseToTray = false;
        vm.BackupRetentionCount = 10;
        vm.TrashRetentionDays = 60;

        vm.SaveCommand.Execute(null);

        settings.Get(SettingsKeys.ReporterName).Should().Be("김철수");
        settings.Get(SettingsKeys.FormatATaskHeader).Should().Be("[할 일]");
        settings.Get(SettingsKeys.IncludeDoneOnly).Should().Be("true");
        settings.Get(SettingsKeys.HotkeyNewNote).Should().Be("Ctrl+Shift+M");
        settings.Get(SettingsKeys.Autostart).Should().Be("false");
        settings.Get(SettingsKeys.CloseToTray).Should().Be("false");
        settings.Get(SettingsKeys.BackupRetentionCount).Should().Be("10");
        settings.Get(SettingsKeys.TrashRetentionDays).Should().Be("60");
    }

    [Fact]
    public void Save_toggles_autostart_service()
    {
        var (vm, _, autostart, _) = Create();

        vm.Autostart = false;
        vm.SaveCommand.Execute(null);
        autostart.IsEnabled().Should().BeFalse();

        vm.Autostart = true;
        vm.SaveCommand.Execute(null);
        autostart.IsEnabled().Should().BeTrue();
    }

    [Fact]
    public void AvailablePresets_matches_resolver()
    {
        var (vm, _, _, _) = Create();
        vm.AvailablePresets.Should().BeEquivalentTo(new[] { "default", "dark", "sepia", "solarized" });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~SettingsViewModelTests"
```
예상 실패: `error CS0246: ... 'SettingsViewModel' ...`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/ViewModels/SettingsViewModel.cs
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.App.Theming;
using Memoria.App.Windows;
using Memoria.Core;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settings;
    private readonly IThemeService _theme;
    private readonly IAutostartService _autostart;
    private bool _loading;

    public string[] AvailablePresets { get; } = ThemeResolver.Presets;

    [ObservableProperty] private ThemeMode _mode;
    [ObservableProperty] private string _preset = "default";
    [ObservableProperty] private string _accent = AccentColor.Default;

    [ObservableProperty] private string _reporterName = "이승현";
    [ObservableProperty] private string _taskHeaderA = "[업무 내용]";
    [ObservableProperty] private string _issueHeaderA = "[이슈]";
    [ObservableProperty] private string _titleWordB = "주간 보고";
    [ObservableProperty] private string _issueHeaderB = "* 이슈사항:";
    [ObservableProperty] private string _reportIndent = "\t";
    [ObservableProperty] private bool _includeDoneOnly;

    [ObservableProperty] private string _hotkeyNewNote = "Ctrl+Alt+N";
    [ObservableProperty] private bool _autostartEnabled = true;   // backing: Autostart
    [ObservableProperty] private bool _closeToTray = true;

    [ObservableProperty] private int _backupRetentionCount = 7;
    [ObservableProperty] private int _trashRetentionDays = 30;

    [ObservableProperty] private bool _isHotkeyValid = true;
    [ObservableProperty] private bool _isAccentValid = true;

    public bool Autostart
    {
        get => AutostartEnabled;
        set => AutostartEnabled = value;
    }

    public bool CanSave => IsHotkeyValid && IsAccentValid;

    public SettingsViewModel(ISettingsRepository settings, IThemeService theme, IAutostartService autostart)
    {
        _settings = settings;
        _theme = theme;
        _autostart = autostart;
        Load();
    }

    private void Load()
    {
        _loading = true;

        Mode = _theme.Mode;
        Preset = _theme.Preset;
        Accent = _theme.Accent;

        ReporterName = _settings.GetOrDefault(SettingsKeys.ReporterName, "이승현");
        TaskHeaderA = _settings.GetOrDefault(SettingsKeys.FormatATaskHeader, "[업무 내용]");
        IssueHeaderA = _settings.GetOrDefault(SettingsKeys.FormatAIssueHeader, "[이슈]");
        TitleWordB = _settings.GetOrDefault(SettingsKeys.FormatBTitleWord, "주간 보고");
        IssueHeaderB = _settings.GetOrDefault(SettingsKeys.FormatBIssueHeader, "* 이슈사항:");
        ReportIndent = _settings.GetOrDefault(SettingsKeys.ReportIndent, "\t");
        IncludeDoneOnly = bool.Parse(_settings.GetOrDefault(SettingsKeys.IncludeDoneOnly, "false"));

        HotkeyNewNote = _settings.GetOrDefault(SettingsKeys.HotkeyNewNote, "Ctrl+Alt+N");
        AutostartEnabled = bool.Parse(_settings.GetOrDefault(SettingsKeys.Autostart, "true"));
        CloseToTray = bool.Parse(_settings.GetOrDefault(SettingsKeys.CloseToTray, "true"));

        BackupRetentionCount = int.Parse(_settings.GetOrDefault(SettingsKeys.BackupRetentionCount, "7"), CultureInfo.InvariantCulture);
        TrashRetentionDays = int.Parse(_settings.GetOrDefault(SettingsKeys.TrashRetentionDays, "30"), CultureInfo.InvariantCulture);

        _loading = false;
    }

    partial void OnModeChanged(ThemeMode value) => ApplyTheme();
    partial void OnPresetChanged(string value) => ApplyTheme();

    partial void OnAccentChanged(string value)
    {
        IsAccentValid = AccentColor.IsValid(value);
        OnPropertyChanged(nameof(CanSave));
        if (IsAccentValid)
            ApplyTheme();
    }

    partial void OnHotkeyNewNoteChanged(string value)
    {
        IsHotkeyValid = HotkeyParser.TryParse(value, out _);
        OnPropertyChanged(nameof(CanSave));
    }

    private void ApplyTheme()
    {
        if (_loading || !IsAccentValid)
            return;
        _theme.Apply(Mode, Preset, Accent);
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanSave)
            return;

        _settings.Set(SettingsKeys.ReporterName, ReporterName);
        _settings.Set(SettingsKeys.FormatATaskHeader, TaskHeaderA);
        _settings.Set(SettingsKeys.FormatAIssueHeader, IssueHeaderA);
        _settings.Set(SettingsKeys.FormatBTitleWord, TitleWordB);
        _settings.Set(SettingsKeys.FormatBIssueHeader, IssueHeaderB);
        _settings.Set(SettingsKeys.ReportIndent, ReportIndent);
        _settings.Set(SettingsKeys.IncludeDoneOnly, IncludeDoneOnly ? "true" : "false");

        _settings.Set(SettingsKeys.HotkeyNewNote, HotkeyNewNote);
        _settings.Set(SettingsKeys.Autostart, AutostartEnabled ? "true" : "false");
        _settings.Set(SettingsKeys.CloseToTray, CloseToTray ? "true" : "false");

        _settings.Set(SettingsKeys.BackupRetentionCount, BackupRetentionCount.ToString(CultureInfo.InvariantCulture));
        _settings.Set(SettingsKeys.TrashRetentionDays, TrashRetentionDays.ToString(CultureInfo.InvariantCulture));

        if (AutostartEnabled)
            _autostart.Enable();
        else
            _autostart.Disable();
    }
}
```
> `SaveCommand`는 `[RelayCommand]` 소스 생성기가 `Save` 메서드에서 만든다(`IRelayCommand`). `Autostart` 래퍼 속성은 테스트/바인딩 명명 가독성을 위한 것이며 내부 `AutostartEnabled` 백킹과 동기화된다.

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~SettingsViewModelTests"
```
예상: `Passed!  - Failed: 0, Passed: 9`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/SettingsViewModel.cs tests/Memoria.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "feat(app): add SettingsViewModel for theme/report/app/retention settings

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: ClientsSettingsViewModel (표시순 vs 매칭 우선순위 독립)

**Files:**
- Create: `src/Memoria.App/ViewModels/ClientsSettingsViewModel.cs`, `src/Memoria.App/ViewModels/ClientRowViewModel.cs`, `src/Memoria.App/ViewModels/ClientRuleRowViewModel.cs`
- Test: `tests/Memoria.Tests/ViewModels/ClientsSettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `Memoria.Core.Data.IClientRepository`(§4) { `Create(Client)`, `Update(Client)`, `Delete(int)`, `GetAll(bool enabledOnly = false)`, `GetRules()`, `ReplaceRules(int clientId, IEnumerable<ClientRule>)` }; `Memoria.Core.Models.Client`/`ClientRule`(§1).
- Produces:
  - `sealed partial class ClientRowViewModel : ObservableObject` { `int Id`(get), `[ObservableProperty] string Name`, `[ObservableProperty] bool Enabled`, `int SortOrder`(get/set) }.
  - `sealed partial class ClientRuleRowViewModel : ObservableObject` { `int Id`(get), `[ObservableProperty] string Keyword`, `[ObservableProperty] int Priority` }.
  - `sealed partial class ClientsSettingsViewModel : ObservableObject` { `ObservableCollection<ClientRowViewModel> Clients`; `ObservableCollection<ClientRuleRowViewModel> Rules`; `ClientRowViewModel? SelectedClient`; commands: `AddClientCommand(string name)`, `DeleteClientCommand(ClientRowViewModel)`, `MoveUpCommand(ClientRowViewModel)`, `MoveDownCommand(ClientRowViewModel)`(표시순 = `sort_order`만 변경), `AddRuleCommand`, `DeleteRuleCommand(ClientRuleRowViewModel)`, `SaveRulesCommand`(선택 고객사 규칙 priority 영속) }.

> §6.3 핵심: **MoveUp/MoveDown 등 표시순 변경은 `clients.sort_order`만 갱신**하고 `client_rules.priority`는 **절대 건드리지 않는다.** priority는 `SaveRulesCommand`(키워드 편집)에서만 변경된다.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/ViewModels/ClientsSettingsViewModelTests.cs
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class ClientsSettingsViewModelTests
{
    private sealed class FakeClientRepository : IClientRepository
    {
        public List<Client> Store { get; } = new();
        public List<ClientRule> RuleStore { get; } = new();
        private int _nextClientId = 1;
        private int _nextRuleId = 1;

        public int Create(Client client)
        {
            client.Id = _nextClientId++;
            Store.Add(client);
            return client.Id;
        }

        public void Update(Client client)
        {
            var existing = Store.First(c => c.Id == client.Id);
            existing.Name = client.Name;
            existing.SortOrder = client.SortOrder;
            existing.Enabled = client.Enabled;
        }

        public void Delete(int id) => Store.RemoveAll(c => c.Id == id);

        public IReadOnlyList<Client> GetAll(bool enabledOnly = false)
            => Store.Where(c => !enabledOnly || c.Enabled).OrderBy(c => c.SortOrder).ToList();

        public IReadOnlyList<ClientRule> GetRules() => RuleStore.ToList();

        public void ReplaceRules(int clientId, IEnumerable<ClientRule> rules)
        {
            RuleStore.RemoveAll(r => r.ClientId == clientId);
            foreach (var r in rules)
            {
                r.Id = _nextRuleId++;
                r.ClientId = clientId;
                RuleStore.Add(r);
            }
        }
    }

    private static (ClientsSettingsViewModel vm, FakeClientRepository repo) Create()
    {
        var repo = new FakeClientRepository();
        // 표시순(sort_order)과 매칭순(priority)을 의도적으로 다르게 시드.
        repo.Create(new Client { Name = "SLD", SortOrder = 0, Enabled = true });          // id 1
        repo.Create(new Client { Name = "MTP", SortOrder = 1, Enabled = true });          // id 2
        repo.Create(new Client { Name = "자율형 공장", SortOrder = 2, Enabled = true });   // id 3
        repo.RuleStore.Add(new ClientRule { Id = 1, ClientId = 3, Keyword = "자율형공장", Priority = 1 });
        repo.RuleStore.Add(new ClientRule { Id = 2, ClientId = 1, Keyword = "SLD", Priority = 6 });
        var vm = new ClientsSettingsViewModel(repo);
        return (vm, repo);
    }

    [Fact]
    public void Loads_clients_ordered_by_sort_order()
    {
        var (vm, _) = Create();
        vm.Clients.Select(c => c.Name).Should().ContainInOrder("SLD", "MTP", "자율형 공장");
    }

    [Fact]
    public void AddClient_appends_with_next_sort_order()
    {
        var (vm, repo) = Create();

        vm.AddClientCommand.Execute("카본센스");

        var added = repo.Store.Single(c => c.Name == "카본센스");
        added.SortOrder.Should().Be(3); // max(0,1,2)+1
        vm.Clients.Last().Name.Should().Be("카본센스");
    }

    [Fact]
    public void DeleteClient_removes_from_repo_and_list()
    {
        var (vm, repo) = Create();
        var mtp = vm.Clients.Single(c => c.Name == "MTP");

        vm.DeleteClientCommand.Execute(mtp);

        repo.Store.Should().NotContain(c => c.Name == "MTP");
        vm.Clients.Should().NotContain(c => c.Name == "MTP");
    }

    [Fact]
    public void MoveDown_swaps_display_sort_order_only()
    {
        var (vm, repo) = Create();
        var sld = vm.Clients.Single(c => c.Name == "SLD");

        vm.MoveDownCommand.Execute(sld);

        // 표시순 변경: SLD <-> MTP
        vm.Clients.Select(c => c.Name).Should().ContainInOrder("MTP", "SLD", "자율형 공장");
        repo.Store.Single(c => c.Name == "MTP").SortOrder.Should().Be(0);
        repo.Store.Single(c => c.Name == "SLD").SortOrder.Should().Be(1);
    }

    [Fact]
    public void Reordering_display_does_not_change_rule_priority()
    {
        var (vm, repo) = Create();
        var sld = vm.Clients.Single(c => c.Name == "SLD");

        vm.MoveDownCommand.Execute(sld);

        // §6.3: 표시순 변경은 client_rules.priority에 영향 없음
        repo.RuleStore.Single(r => r.Keyword == "자율형공장").Priority.Should().Be(1);
        repo.RuleStore.Single(r => r.Keyword == "SLD").Priority.Should().Be(6);
    }

    [Fact]
    public void Selecting_client_loads_its_rules()
    {
        var (vm, _) = Create();
        vm.SelectedClient = vm.Clients.Single(c => c.Name == "자율형 공장");

        vm.Rules.Select(r => r.Keyword).Should().ContainSingle().Which.Should().Be("자율형공장");
    }

    [Fact]
    public void SaveRules_persists_priority_independently_of_sort_order()
    {
        var (vm, repo) = Create();
        vm.SelectedClient = vm.Clients.Single(c => c.Name == "자율형 공장");
        vm.Rules.Single().Priority = 1;
        vm.AddRuleCommand.Execute(null);
        vm.Rules.Last().Keyword = "자율형 공장";
        vm.Rules.Last().Priority = 2;

        vm.SaveRulesCommand.Execute(null);

        var saved = repo.RuleStore.Where(r => r.ClientId == 3).OrderBy(r => r.Priority).ToList();
        saved.Select(r => (r.Keyword, r.Priority)).Should()
            .ContainInOrder(("자율형공장", 1), ("자율형 공장", 2));
        // sort_order는 그대로
        repo.Store.Single(c => c.Id == 3).SortOrder.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ClientsSettingsViewModelTests"
```
예상 실패: `error CS0246: ... 'ClientsSettingsViewModel' ...`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/ViewModels/ClientRowViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace Memoria.App.ViewModels;

public sealed partial class ClientRowViewModel : ObservableObject
{
    public int Id { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _enabled;
    public int SortOrder { get; set; }

    public ClientRowViewModel(int id, string name, bool enabled, int sortOrder)
    {
        Id = id;
        _name = name;
        _enabled = enabled;
        SortOrder = sortOrder;
    }
}
```

```csharp
// src/Memoria.App/ViewModels/ClientRuleRowViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace Memoria.App.ViewModels;

public sealed partial class ClientRuleRowViewModel : ObservableObject
{
    public int Id { get; }
    [ObservableProperty] private string _keyword;
    [ObservableProperty] private int _priority;

    public ClientRuleRowViewModel(int id, string keyword, int priority)
    {
        Id = id;
        _keyword = keyword;
        _priority = priority;
    }
}
```

```csharp
// src/Memoria.App/ViewModels/ClientsSettingsViewModel.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public sealed partial class ClientsSettingsViewModel : ObservableObject
{
    private readonly IClientRepository _clients;

    public ObservableCollection<ClientRowViewModel> Clients { get; } = new();
    public ObservableCollection<ClientRuleRowViewModel> Rules { get; } = new();

    [ObservableProperty] private ClientRowViewModel? _selectedClient;

    public ClientsSettingsViewModel(IClientRepository clients)
    {
        _clients = clients;
        LoadClients();
    }

    private void LoadClients()
    {
        Clients.Clear();
        foreach (var c in _clients.GetAll().OrderBy(c => c.SortOrder))
            Clients.Add(new ClientRowViewModel(c.Id, c.Name, c.Enabled, c.SortOrder));
    }

    partial void OnSelectedClientChanged(ClientRowViewModel? value)
    {
        Rules.Clear();
        if (value is null)
            return;
        foreach (var r in _clients.GetRules().Where(r => r.ClientId == value.Id).OrderBy(r => r.Priority))
            Rules.Add(new ClientRuleRowViewModel(r.Id, r.Keyword, r.Priority));
    }

    [RelayCommand]
    private void AddClient(string name)
    {
        var nextOrder = Clients.Count == 0 ? 0 : Clients.Max(c => c.SortOrder) + 1;
        var id = _clients.Create(new Client { Name = name, SortOrder = nextOrder, Enabled = true });
        Clients.Add(new ClientRowViewModel(id, name, true, nextOrder));
    }

    [RelayCommand]
    private void DeleteClient(ClientRowViewModel row)
    {
        _clients.Delete(row.Id);
        Clients.Remove(row);
    }

    [RelayCommand]
    private void MoveUp(ClientRowViewModel row)
    {
        var index = Clients.IndexOf(row);
        if (index <= 0)
            return;
        Swap(index, index - 1);
    }

    [RelayCommand]
    private void MoveDown(ClientRowViewModel row)
    {
        var index = Clients.IndexOf(row);
        if (index < 0 || index >= Clients.Count - 1)
            return;
        Swap(index, index + 1);
    }

    // 표시순(sort_order)만 교체한다. client_rules.priority는 건드리지 않는다(§6.3).
    private void Swap(int a, int b)
    {
        var rowA = Clients[a];
        var rowB = Clients[b];

        (rowA.SortOrder, rowB.SortOrder) = (rowB.SortOrder, rowA.SortOrder);
        _clients.Update(new Client { Id = rowA.Id, Name = rowA.Name, Enabled = rowA.Enabled, SortOrder = rowA.SortOrder });
        _clients.Update(new Client { Id = rowB.Id, Name = rowB.Name, Enabled = rowB.Enabled, SortOrder = rowB.SortOrder });

        Clients.Move(a, b);
    }

    [RelayCommand]
    private void AddRule()
    {
        var nextPriority = Rules.Count == 0 ? 1 : Rules.Max(r => r.Priority) + 1;
        Rules.Add(new ClientRuleRowViewModel(0, "", nextPriority));
    }

    [RelayCommand]
    private void DeleteRule(ClientRuleRowViewModel rule) => Rules.Remove(rule);

    [RelayCommand]
    private void SaveRules()
    {
        if (SelectedClient is null)
            return;

        var rules = Rules.Select(r => new ClientRule
        {
            ClientId = SelectedClient.Id,
            Keyword = r.Keyword,
            Priority = r.Priority,
        });
        _clients.ReplaceRules(SelectedClient.Id, rules);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~ClientsSettingsViewModelTests"
```
예상: `Passed!  - Failed: 0, Passed: 7`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/ViewModels/ClientRowViewModel.cs src/Memoria.App/ViewModels/ClientRuleRowViewModel.cs src/Memoria.App/ViewModels/ClientsSettingsViewModel.cs tests/Memoria.Tests/ViewModels/ClientsSettingsViewModelTests.cs
git commit -m "feat(app): add clients settings VM with independent display/priority order

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: 설정 View(XAML) + 강조색 선택 + App 테마 배선(수동 검증)

**Files:**
- Create: `src/Memoria.App/Views/SettingsWindow.xaml`, `src/Memoria.App/Views/SettingsWindow.xaml.cs`, `src/Memoria.App/Views/ISettingsWindowService.cs`, `src/Memoria.App/Views/SettingsWindowService.cs`
- Modify: `src/Memoria.App/App.xaml.cs`(계약 §9.4 **step 3**에 M7 DI 등록을 *추가*하고 **step 7**에 `IThemeService.Initialize()` 한 줄을 *추가* — **App.xaml.cs 전체 재작성 금지, 기존 호출 보존**), `src/Memoria.App/ViewModels/MainViewModel.cs`(M2가 §9.3에서 스텁으로 선언한 `OpenSettingsCommand`의 `OpenSettings` **본문을 M7이 채움**)
- Test: 자동 테스트 없음(WPF 창/색 전환은 메시지 펌프 필요) → **빌드 검증 + 수동 검증 체크포인트**. VM/테마 로직은 Task 1~7에서 자동 검증됨.

**Interfaces:**
- Consumes: Task 5 `IThemeService`(App 부트스트랩), Task 6 `SettingsViewModel`, Task 7 `ClientsSettingsViewModel`, M6 `IAutostartService`, M6 message-only 창과 동일 수명의 `SystemEventsThemeSource`, 계약 §9.2 `AppServices.Resolve<T>()`(서비스 해석), 계약 §9.3 `MainViewModel.OpenSettingsCommand`(M2 스텁), `Memoria.Core.Data.IClientRepository`.
- Produces: 조립된 설정 화면 + `ISettingsWindowService`/`SettingsWindowService`(M7 내부 — 창 생성/표시를 ViewModel에서 분리해 VM의 WPF 비의존 유지) + M2 `OpenSettingsCommand` 본문 + 테마 부트스트랩(새 **공개 계약** 없음).

- [ ] **Step 1: SettingsWindow.xaml 작성(모든 색 DynamicResource)**

```xml
<!-- src/Memoria.App/Views/SettingsWindow.xaml -->
<Window x:Class="Memoria.App.Views.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="설정" Width="640" Height="640"
        Background="{DynamicResource Brush.WindowBackground}"
        Foreground="{DynamicResource Brush.Foreground}">
    <TabControl Background="{DynamicResource Brush.Surface}" Margin="8">
        <!-- 테마 탭 -->
        <TabItem Header="테마">
            <StackPanel Margin="12">
                <TextBlock Text="모드" Margin="0,0,0,4" />
                <ComboBox SelectedValue="{Binding Mode}" SelectedValuePath="Tag" Width="200" HorizontalAlignment="Left">
                    <ComboBoxItem Content="라이트" Tag="{x:Static x:Null}" />
                </ComboBox>
                <TextBlock Text="프리셋" Margin="0,12,0,4" />
                <ComboBox ItemsSource="{Binding AvailablePresets}" SelectedItem="{Binding Preset}" Width="200" HorizontalAlignment="Left" />
                <TextBlock Text="강조색 (#RRGGBB)" Margin="0,12,0,4" />
                <StackPanel Orientation="Horizontal">
                    <Border Width="28" Height="28" Background="{DynamicResource Brush.Accent}" BorderBrush="{DynamicResource Brush.Border}" BorderThickness="1" />
                    <TextBox Text="{Binding Accent, UpdateSourceTrigger=PropertyChanged}" Width="120" Margin="8,0,0,0" />
                </StackPanel>
                <WrapPanel Margin="0,8,0,0">
                    <Button Content="#0078D4" Tag="#0078D4" Click="AccentSwatch_Click" Width="80" Margin="0,0,4,4" />
                    <Button Content="#D83B01" Tag="#D83B01" Click="AccentSwatch_Click" Width="80" Margin="0,0,4,4" />
                    <Button Content="#107C10" Tag="#107C10" Click="AccentSwatch_Click" Width="80" Margin="0,0,4,4" />
                    <Button Content="#5C2D91" Tag="#5C2D91" Click="AccentSwatch_Click" Width="80" Margin="0,0,4,4" />
                </WrapPanel>
            </StackPanel>
        </TabItem>

        <!-- 주간보고 탭 -->
        <TabItem Header="주간보고">
            <StackPanel Margin="12">
                <TextBlock Text="보고자 이름" /><TextBox Text="{Binding ReporterName}" />
                <TextBlock Text="양식 A — 업무 머리글" Margin="0,8,0,0" /><TextBox Text="{Binding TaskHeaderA}" />
                <TextBlock Text="양식 A — 이슈 머리글" Margin="0,8,0,0" /><TextBox Text="{Binding IssueHeaderA}" />
                <TextBlock Text="양식 B — 제목 문구" Margin="0,8,0,0" /><TextBox Text="{Binding TitleWordB}" />
                <TextBlock Text="양식 B — 이슈 머리글" Margin="0,8,0,0" /><TextBox Text="{Binding IssueHeaderB}" />
                <CheckBox Content="완료 항목만 포함(includeDoneOnly)" IsChecked="{Binding IncludeDoneOnly}" Margin="0,8,0,0" />
            </StackPanel>
        </TabItem>

        <!-- 고객사 탭 -->
        <TabItem Header="고객사" x:Name="ClientsTab">
            <Grid Margin="12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column="0">
                    <TextBlock Text="표시순(양식 B 섹션 순서)" />
                    <ListBox x:Name="ClientList" ItemsSource="{Binding Clients}" SelectedItem="{Binding SelectedClient}"
                             DisplayMemberPath="Name" Height="380" Background="{DynamicResource Brush.Surface}" />
                    <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                        <Button Content="▲" Command="{Binding MoveUpCommand}" CommandParameter="{Binding SelectedClient}" Width="32" />
                        <Button Content="▼" Command="{Binding MoveDownCommand}" CommandParameter="{Binding SelectedClient}" Width="32" Margin="4,0,0,0" />
                        <Button Content="삭제" Command="{Binding DeleteClientCommand}" CommandParameter="{Binding SelectedClient}" Margin="4,0,0,0" />
                    </StackPanel>
                </StackPanel>
                <StackPanel Grid.Column="1" Margin="8,0,0,0">
                    <TextBlock Text="키워드/우선순위(매칭순 — 표시순과 독립)" />
                    <DataGrid ItemsSource="{Binding Rules}" AutoGenerateColumns="False" Height="380"
                              Background="{DynamicResource Brush.Surface}">
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="키워드" Binding="{Binding Keyword}" Width="*" />
                            <DataGridTextColumn Header="우선순위" Binding="{Binding Priority}" Width="80" />
                        </DataGrid.Columns>
                    </DataGrid>
                    <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                        <Button Content="규칙 추가" Command="{Binding AddRuleCommand}" />
                        <Button Content="규칙 저장" Command="{Binding SaveRulesCommand}" Margin="4,0,0,0" />
                    </StackPanel>
                </StackPanel>
            </Grid>
        </TabItem>

        <!-- 앱/보존 탭 -->
        <TabItem Header="앱">
            <StackPanel Margin="12">
                <TextBlock Text="새 메모 단축키" /><TextBox Text="{Binding HotkeyNewNote, UpdateSourceTrigger=PropertyChanged}" Width="200" HorizontalAlignment="Left" />
                <CheckBox Content="Windows 시작 시 자동 실행" IsChecked="{Binding Autostart}" Margin="0,8,0,0" />
                <CheckBox Content="닫기(X) 시 트레이로 숨김" IsChecked="{Binding CloseToTray}" Margin="0,4,0,0" />
                <TextBlock Text="백업 보존 개수" Margin="0,8,0,0" /><TextBox Text="{Binding BackupRetentionCount}" Width="80" HorizontalAlignment="Left" />
                <TextBlock Text="휴지통 보존 일수" Margin="0,8,0,0" /><TextBox Text="{Binding TrashRetentionDays}" Width="80" HorizontalAlignment="Left" />
            </StackPanel>
        </TabItem>
    </TabControl>
</Window>
```
> 모드 ComboBox는 code-behind에서 `ThemeMode` 항목(라이트/다크/시스템)을 채운다(enum→한글 라벨 매핑은 얇은 code-behind). 모든 패널/리스트/그리드 배경은 `DynamicResource` 키만 사용(StaticResource 금지).

- [ ] **Step 2: SettingsWindow.xaml.cs (얇게: DataContext 분배 + 강조색 스와치 + 모드 항목)**

```csharp
// src/Memoria.App/Views/SettingsWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using Memoria.App.ViewModels;
using Memoria.Core.Models;

namespace Memoria.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _settingsVm;

    public SettingsWindow(SettingsViewModel settingsVm, ClientsSettingsViewModel clientsVm)
    {
        InitializeComponent();
        _settingsVm = settingsVm;
        DataContext = settingsVm;
        ClientsTab.DataContext = clientsVm;
        Closed += (_, _) => settingsVm.SaveCommand.Execute(null); // 비-테마 설정 일괄 저장
    }

    private void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
            _settingsVm.Accent = hex; // 즉시 적용(ThemeService)
    }
}
```
> 강조색 선택은 프리셋 스와치 + `#RRGGBB` 직접 입력으로 구성한다(서드파티 color picker 패키지 미도입 — 단순성 원칙). 더 풍부한 선택기가 필요하면 추후 별도 마일스톤에서 추가.

- [ ] **Step 3a: 설정 창 표시 서비스(`ISettingsWindowService`) — VM의 WPF 비의존을 지키는 경계**

> 계약 §9.3은 `OpenSettingsCommand`를 "M2 스텁으로 선언 → M7이 본문 채움"으로 규정한다. ViewModel은 WPF 타입 의존을 피해야 하므로(계약 머리말), 창 생성/표시는 WPF에 의존하는 `SettingsWindowService`로 분리하고 VM은 WPF 비의존 인터페이스(`ISettingsWindowService`)만 호출한다.

```csharp
// src/Memoria.App/Views/ISettingsWindowService.cs
namespace Memoria.App.Views;

public interface ISettingsWindowService
{
    void ShowSettings();
}
```

```csharp
// src/Memoria.App/Views/SettingsWindowService.cs
using System.Windows;
using Memoria.App.ViewModels;

namespace Memoria.App.Views;

public sealed class SettingsWindowService : ISettingsWindowService
{
    public void ShowSettings()
    {
        // 서비스 접근은 계약 §9.2 AppServices.Resolve<T>()로만 한다(수동 new/필드 의존 금지).
        var window = new SettingsWindow(
            AppServices.Resolve<SettingsViewModel>(),
            AppServices.Resolve<ClientsSettingsViewModel>())
        {
            Owner = Application.Current.MainWindow,
        };
        window.ShowDialog();
    }
}
```

- [ ] **Step 3b: M2 `OpenSettingsCommand` 스텁 본문 채움(MainViewModel)**

> M2가 `[RelayCommand]`로 선언한 `OpenSettings` 메서드의 **본문만** 채운다(명령명/시그니처 변경 금지 — 계약 §9.3). VM은 `AppServices.Resolve<ISettingsWindowService>()`로 표시를 위임하므로 WPF 타입에 의존하지 않는다.

```csharp
// src/Memoria.App/ViewModels/MainViewModel.cs (M2 산출물에 본문만 추가)
// using Memoria.App.Views; 추가

[RelayCommand]
private void OpenSettings() => AppServices.Resolve<ISettingsWindowService>().ShowSettings();
```
> 트레이(M6)와 메인 메뉴는 동일한 `OpenSettingsCommand`를 호출한다. M6는 이 명령을 안전하게 Consumes 하던 상태였고(빈 스텁), M7이 본문을 채우면 트레이/메뉴 양쪽에서 설정 창이 열린다 — M6 배선을 따로 교체할 필요 없음.

- [ ] **Step 3c: App.xaml.cs §9.4 step 3에 M7 DI 등록 추가(전체 재작성 금지, 기존 호출 보존)**

> 계약 §9.4는 누적 패치다("각 마일스톤은 기존 호출 보존 + 자기 배선만 추가"). M7은 step 3의 `services.AddMemoriaCore(...)` 등록 블록에 **아래 줄만 추가**한다. `ThemeService` 생성자(`ISettingsRepository`, `IThemeApplier`, `ISystemThemeSource`), `SettingsViewModel`(`ISettingsRepository`, `IThemeService`, `IAutostartService`), `ClientsSettingsViewModel`(`IClientRepository`)는 전부 Core(M1)/M6 등록으로 DI가 자동 해석한다.

```csharp
// src/Memoria.App/App.xaml.cs — §9.4 step 3 등록 블록에 추가
// using Memoria.App.Theming; using Memoria.App.Views; using Memoria.App.ViewModels; 추가
services.AddSingleton<IThemeApplier, WpfThemeApplier>();
services.AddSingleton<ISystemThemeSource, SystemEventsThemeSource>();   // M6 message-only 창과 동일 프로세스 수명
services.AddSingleton<IThemeService, ThemeService>();
services.AddSingleton<ISettingsWindowService, SettingsWindowService>();
services.AddTransient<SettingsViewModel>();                             // 설정 창을 열 때마다 새 인스턴스
services.AddTransient<ClientsSettingsViewModel>();
```

- [ ] **Step 3d: App.xaml.cs §9.4 step 7에 `IThemeService.Initialize()` 한 줄 추가**

> §9.4 부트스트랩 순서의 **step 7**(= `IThemeService.Initialize()`)에 정확히 아래 한 줄을 둔다. step 4(`EnsureReady`)·step 5/6(무결성/백업) 이후, step 10(MainWindow 생성) 이전이어야 첫 창이 저장된 테마로 렌더된다. App.xaml.cs를 새로 쓰지 않고 기존 OnStartup의 해당 위치에 삽입만 한다.

```csharp
// src/Memoria.App/App.xaml.cs — §9.4 step 7
AppServices.Resolve<IThemeService>().Initialize();   // 저장된 mode/preset/accent를 즉시 적용(시스템 모드 구독은 ThemeService 생성자가 수행)
```

- [ ] **Step 3e: OnExit에서 테마 서비스 정리(기존 정리 보존)**

> `IThemeService`는 `IDisposable`(SystemEvents 구독 해제). DI 컨테이너가 싱글톤을 dispose 하면 자동 정리되지만, 명시적으로도 안전하게 해제한다. 기존 M6 정리(`_hotkey`/`_tray`/`_singleInstance`)와 §9.4 OnExit 항목(`FlushAll`/`wal_checkpoint`)은 그대로 둔다.

```csharp
// src/Memoria.App/App.xaml.cs — OnExit에 한 줄 추가(기존 정리 보존)
(AppServices.Resolve<IThemeService>() as System.IDisposable)?.Dispose();
```

- [ ] **Step 4: 빌드로 View/배선 컴파일 검증**

```
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\Memoria.sln"
```
예상: `Build succeeded`. (M2 `MainViewModel.OpenSettingsCommand`/`AppServices`/M1 `IClientRepository` 심볼이 실제 이름과 일치해야 함 — 불일치 시 해당 한 줄만 실제 이름으로 교정.)

- [ ] **Step 5: 전체 테스트 회귀 확인**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests"
```
예상: M1~M7 전체 통과(`Failed: 0`). M7 신규 단위 테스트(ThemeResolver/AccentColor/SystemThemeReader/SystemEventsThemeSource/ThemeService/SettingsViewModel/ClientsSettingsViewModel)가 모두 PASS.

- [ ] **Step 6: 수동 검증 체크포인트 — 테마/설정 통합 동작**
  `dotnet.exe run --project "C:\...\src\Memoria.App"` 실행 후 트레이/메인의 "설정"으로 진입하여 확인:
  1. **모드 전환**: 라이트↔다크 변경 시 메인 창 전체 색이 **즉시** 바뀌고 깜빡임이 거의 없는가(MergedDictionaries 슬롯 1개 교체 확인).
  2. **프리셋 전환**: default/dark/sepia/solarized 변경 시 배경/전경/사이드바 색이 팔레트대로 바뀌는가.
  3. **강조색**: 스와치 클릭 또는 `#RRGGBB` 입력 시 강조 버튼/선택 항목 색이 즉시 바뀌는가. 무효 문자열 입력 시 적용되지 않고 저장도 막히는가.
  4. **시스템 모드 추종**: 모드를 "시스템"으로 두고 Windows 설정에서 앱 모드를 라이트↔다크로 토글하면 앱이 자동으로 따라가는가(`SystemEvents.UserPreferenceChanged`/레지스트리 추종). 고정 모드(라이트/다크)에서는 OS 변경에 반응하지 않는가.
  5. **재시작 영속**: 테마/주간보고/단축키/자동시작/보존 값을 바꾸고 앱을 재시작하면 값이 유지되는가(설정 → `ISettingsRepository` 영속 확인).
  6. **고객사 순서 vs 우선순위 독립**: 고객사 탭에서 ▲▼로 표시순을 바꿔도 키워드 그리드의 우선순위 값은 그대로인가. 우선순위를 바꿔 저장해도 표시순 리스트 순서는 그대로인가(§6.3).
  7. **자동시작 토글**: 자동시작 체크 해제 후 창을 닫아 저장 → `regedit`의 `HKCU\...\Run`에서 `Memoria` 값이 제거되는가(다시 체크 시 복원).
  8. **트레이 메뉴 테마**: 테마 전환 후 트레이 우클릭 메뉴 색도 따라 바뀌는가(서드파티 컨트롤 DynamicResource 적용 확인).

- [ ] **Step 7: Commit**

```
git add src/Memoria.App/Views/SettingsWindow.xaml src/Memoria.App/Views/SettingsWindow.xaml.cs src/Memoria.App/Views/ISettingsWindowService.cs src/Memoria.App/Views/SettingsWindowService.cs src/Memoria.App/ViewModels/MainViewModel.cs src/Memoria.App/App.xaml.cs
git commit -m "feat(app): add settings window, fill OpenSettings command, wire theme bootstrap

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 검증 요약

- **자동 테스트(xUnit, Windows dotnet.exe)**: `ThemeResolver`(19), `AccentColor`(13)+`SystemThemeReader`(7), `SystemEventsThemeSource`(2), `ThemeService`(12), `SettingsViewModel`(9), `ClientsSettingsViewModel`(7) — 핵심 순수 로직/코디네이션/VM 전부 자동 검증.
- **수동 검증 체크포인트**: 팔레트 8종 시각 확인(Task 3), 테마/시스템추종/영속/고객사 두 순서 독립/자동시작/트레이 메뉴 테마(Task 8).
- **빌드/테스트는 Windows `dotnet.exe`** 로만 수행하며, 모든 명령은 계약 §7 규약의 Windows 절대경로를 사용한다.
- **계약 준수**:
  - **§10 테마 브러시 키**: 8개 팔레트 모두 계약 §10의 **17개 `Brush.*` 키 전부**(`Brush.WindowBackground`/`Brush.Surface`/`Brush.SidebarBackground`/`Brush.ToolbarBackground`/`Brush.EditorBackground`/`Brush.Foreground`/`Brush.SecondaryForeground`/`Brush.Border`/`Brush.ListItemHover`/`Brush.ListItemSelected`/`Brush.Accent`/`Brush.AccentForeground`/`Brush.StrikethroughForeground`/`Brush.UnclassifiedHighlight`/`Brush.WarningBackground`/`Brush.WarningBorder`/`Brush.WarningForeground`)를 정확한 이름으로 정의한다. View는 이 키만 `DynamicResource`로 참조(StaticResource 금지).
  - **§9.3 명령명**: M2가 선언한 `OpenSettingsCommand` 스텁의 `OpenSettings` 본문만 채웠다(명령명/시그니처 불변).
  - **§9.2 서비스 로케이터 / §9.4 부트스트랩**: 서비스 접근은 `AppServices.Resolve<T>()`로 하고, App.xaml.cs는 전체 재작성 없이 §9.4 step 3(DI 등록)·step 7(`IThemeService.Initialize()`)·OnExit(테마 dispose)에 **추가만** 했다.
  - Consumes(`ISettingsRepository`/`IClientRepository`/`SettingsKeys`/`ThemeMode`, M6 message-only 창 수명) — Produces(`IThemeService`, `SettingsViewModel`) 시그니처를 계약 그대로 사용했고, 보조 협력자(`IThemeApplier`/`ISystemThemeSource`/`ClientsSettingsViewModel`/`ISettingsWindowService`)는 M7 내부 산출물로 추가했다.

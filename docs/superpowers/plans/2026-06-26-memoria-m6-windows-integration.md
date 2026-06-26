# Memoria M6 — Windows Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 전역 단축키(`Ctrl+Alt+N`)·트레이 상주·자동시작·단일 인스턴스(named pipe)·포그라운드 전환을 구현해 "어디서든 즉시 새 메모" 경험을 완성한다.

**Architecture:** Win32/트레이/레지스트리/IPC 같은 OS 통합 코드는 모두 `Memoria.App`(net9.0-windows)에만 둔다. 단축키 파싱·레지스트리 키/값 빌더·파이프 메시지 직렬화 같은 **순수 로직**은 별도 정적 클래스로 분리해 `Memoria.Tests`(net9.0-windows)에서 xUnit으로 자동 검증하고, message-only 창/트레이/전역 단축키/포그라운드처럼 시각·전역 동작은 **수동 검증 체크포인트**로 확인한다. 네 개의 서비스 인터페이스(`IGlobalHotkeyService`, `ITrayService`, `IAutostartService`, `ISingleInstanceService`)를 산출해 `App` 부트스트랩에서 조립한다.

**Tech Stack:** C# / .NET 9 / WPF(net9.0-windows) / `CommunityToolkit.Mvvm` / Win32 P/Invoke(`user32.dll`: `RegisterHotKey`/`UnregisterHotKey`/`SetForegroundWindow`/`AllowSetForegroundWindow`) / `System.Windows.Interop.HwndSource`(HWND_MESSAGE) / `System.Threading.Mutex` + `System.IO.Pipes`(NamedPipeServerStream/ClientStream) / `Microsoft.Win32.Registry`(HKCU Run) / `H.NotifyIcon.Wpf`(트레이) / 테스트는 `xUnit` + `FluentAssertions`.

## Global Constraints
- 런타임: **.NET 9**.
- TFM: `Memoria.Core` = **net9.0**, `Memoria.App` = **net9.0-windows**(`<UseWPF>true</UseWPF>`), `Memoria.Tests` = **net9.0-windows**.
- Windows 통합 코드(단축키/트레이/자동시작/단일 인스턴스/포그라운드)는 **`Memoria.App`에만** 둔다. `Memoria.Core`는 WPF/Win32 비의존.
- ViewModel은 `Memoria.App`에 두되 WPF 타입 의존 금지(`CommunityToolkit.Mvvm`만), **code-behind는 얇게** 유지하고 로직은 서비스/ViewModel로 분리해 xUnit 자동 테스트.
- DB/데이터 루트: `%LOCALAPPDATA%\Memoria`(M1이 생성). M6는 DB 스키마를 건드리지 않고 `settings` 키만 소비한다.
- 빌드/테스트는 **Windows `dotnet.exe`** 로만 수행(WPF는 Linux dotnet 빌드 불가). WSL 호출 시 **Windows 절대경로** 인자 사용.
- 단일파일 publish에서 **`PublishTrimmed` 금지**(WPF 트리밍 미지원), **`EnableCompressionInSingleFile` 미사용**(콜드 스타트 비용). 트레이 `.ico`는 단일파일에 포함.
- 전역 단축키: 앱 수명 내내 유지되는 **message-only 창(HWND_MESSAGE)** 에 `RegisterHotKey`(+`MOD_NOREPEAT`=0x4000) 및 `WM_HOTKEY`(0x0312) 후킹. 메인 창을 닫아도 단축키 유지.
- 창 닫기(X)=종료가 아니라 **Hide(HWND 유지)**, 설정 `app.closeToTray`(기본 true)로 제어. HWND를 파괴하지 않는다.
- 자동시작: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, 값 이름 `Memoria`, 설정 `app.autostart`(기본 true).
- 단일 인스턴스: 명명 `Mutex` + named pipe(새 메모/열기 명령 전달). 두 번째 인스턴스는 명령 전달 후 종료하며, 종료 전 `AllowSetForegroundWindow(ASFW_ANY)` → 첫 인스턴스가 `SetForegroundWindow`로 포그라운드.
- 설정 키는 `Memoria.Core.SettingsKeys` 상수만 사용: `HotkeyNewNote`("hotkey.newNote"), `CloseToTray`("app.closeToTray"), `Autostart`("app.autostart").

---

### Task 1: 단축키 문자열 파서 (HotkeyParser, 순수 로직)

**Files:**
- Create: `src/Memoria.App/Windows/HotkeyParser.cs`
- Test: `tests/Memoria.Tests/Windows/HotkeyParserTests.cs`

**Interfaces:**
- Consumes: `SettingsKeys.HotkeyNewNote`(설정 문자열 예 `"Ctrl+Alt+N"`).
- Produces: `Memoria.App.Windows.HotkeyModifiers`(`[Flags]` enum: None=0, Alt=0x0001, Control=0x0002, Shift=0x0004, Win=0x0008 — Win32 `MOD_*` 값과 동일), `readonly record struct ParsedHotkey(HotkeyModifiers Modifiers, uint VirtualKey)`, `static class HotkeyParser` { `const uint ModNoRepeat = 0x4000`; `bool TryParse(string?, out ParsedHotkey)` }.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/Windows/HotkeyParserTests.cs
using FluentAssertions;
using Memoria.App.Windows;
using Xunit;

namespace Memoria.Tests.Windows;

public class HotkeyParserTests
{
    [Fact]
    public void Parses_default_ctrl_alt_n()
    {
        HotkeyParser.TryParse("Ctrl+Alt+N", out var hk).Should().BeTrue();
        hk.Modifiers.Should().Be(HotkeyModifiers.Control | HotkeyModifiers.Alt);
        hk.VirtualKey.Should().Be(0x4Eu); // VK_N == 'N'
    }

    [Fact]
    public void Is_case_insensitive_and_trims_spaces()
    {
        HotkeyParser.TryParse(" ctrl + ALT + n ", out var hk).Should().BeTrue();
        hk.Modifiers.Should().Be(HotkeyModifiers.Control | HotkeyModifiers.Alt);
        hk.VirtualKey.Should().Be(0x4Eu);
    }

    [Fact]
    public void Parses_function_key_with_shift()
    {
        HotkeyParser.TryParse("Ctrl+Shift+F5", out var hk).Should().BeTrue();
        hk.Modifiers.Should().Be(HotkeyModifiers.Control | HotkeyModifiers.Shift);
        hk.VirtualKey.Should().Be(0x74u); // VK_F5 == 0x70 + 4
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("N")]            // 수정자 없음
    [InlineData("Ctrl+Alt")]    // 키 없음
    [InlineData("Ctrl+A+B")]    // 키 두 개
    [InlineData("Ctrl+Alt+NN")] // 알 수 없는 키
    [InlineData("Ctrl+Alt+F25")]// 범위 밖 F키
    public void Rejects_invalid_input(string? input)
    {
        HotkeyParser.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void ModNoRepeat_constant_is_0x4000()
    {
        HotkeyParser.ModNoRepeat.Should().Be(0x4000u);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~HotkeyParserTests"
```
예상 실패: `error CS0246: The type or namespace name 'HotkeyParser'/'HotkeyModifiers'/'ParsedHotkey' could not be found` (컴파일 실패).

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/Windows/HotkeyParser.cs
using System;

namespace Memoria.App.Windows;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,      // MOD_ALT
    Control = 0x0002,  // MOD_CONTROL
    Shift = 0x0004,    // MOD_SHIFT
    Win = 0x0008,      // MOD_WIN
}

public readonly record struct ParsedHotkey(HotkeyModifiers Modifiers, uint VirtualKey);

public static class HotkeyParser
{
    public const uint ModNoRepeat = 0x4000; // MOD_NOREPEAT

    public static bool TryParse(string? input, out ParsedHotkey result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var parts = input.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        var mods = HotkeyModifiers.None;
        uint vk = 0;
        bool keySet = false;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= HotkeyModifiers.Control;
                    break;
                case "alt":
                    mods |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    mods |= HotkeyModifiers.Shift;
                    break;
                case "win":
                case "windows":
                    mods |= HotkeyModifiers.Win;
                    break;
                default:
                    if (keySet)
                        return false; // 키는 하나만 허용
                    if (!TryMapKey(part, out vk))
                        return false;
                    keySet = true;
                    break;
            }
        }

        if (!keySet || mods == HotkeyModifiers.None)
            return false;

        result = new ParsedHotkey(mods, vk);
        return true;
    }

    private static bool TryMapKey(string key, out uint vk)
    {
        vk = 0;
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z') { vk = c; return true; } // VK_A..VK_Z == 'A'..'Z'
            if (c is >= '0' and <= '9') { vk = c; return true; } // VK_0..VK_9 == '0'..'9'
            return false;
        }

        if ((key[0] is 'F' or 'f') && uint.TryParse(key.AsSpan(1), out var fn) && fn is >= 1 and <= 24)
        {
            vk = 0x70 + (fn - 1); // VK_F1 == 0x70
            return true;
        }
        return false;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~HotkeyParserTests"
```
예상: `Passed!  - Failed: 0, Passed: 9` (Theory 7 + Fact 2 = 9건 통과).

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/Windows/HotkeyParser.cs tests/Memoria.Tests/Windows/HotkeyParserTests.cs
git commit -m "feat(app): add pure hotkey string parser for global hotkey

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: 자동시작 레지스트리 빌더 + IAutostartService

**Files:**
- Create: `src/Memoria.App/Windows/AutostartService.cs`
- Test: `tests/Memoria.Tests/Windows/AutostartTests.cs`

**Interfaces:**
- Consumes: `SettingsKeys.Autostart`("app.autostart") — 호출자(App)가 설정 값에 따라 Enable/Disable 결정.
- Produces:
  - `static class AutostartRegistry` { `const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run"`; `const string ValueName = "Memoria"`; `string BuildCommand(string exePath)` → `"\"{exePath}\""` }.
  - `interface IAutostartService` { `bool IsEnabled()`; `void Enable()`; `void Disable()` }.
  - `sealed class AutostartService : IAutostartService` (기본 생성자 = 실제 Run 키 + `Environment.ProcessPath`; 테스트용 생성자 = 커스텀 keyPath/valueName/exePath 주입).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/Windows/AutostartTests.cs
using FluentAssertions;
using Memoria.App.Windows;
using Microsoft.Win32;
using Xunit;

namespace Memoria.Tests.Windows;

public class AutostartTests
{
    [Fact]
    public void BuildCommand_wraps_path_in_quotes()
    {
        AutostartRegistry.BuildCommand(@"C:\Apps\Memoria\Memoria.exe")
            .Should().Be("\"C:\\Apps\\Memoria\\Memoria.exe\"");
    }

    [Fact]
    public void Default_run_key_path_and_value_name_are_canonical()
    {
        AutostartRegistry.RunKeyPath.Should().Be(@"Software\Microsoft\Windows\CurrentVersion\Run");
        AutostartRegistry.ValueName.Should().Be("Memoria");
    }

    [Fact]
    public void Enable_then_disable_roundtrips_against_hkcu()
    {
        // 실제 Run 키 오염 방지: 테스트 전용 HKCU 하위 키 사용
        var keyPath = @"Software\MemoriaTest\" + System.Guid.NewGuid().ToString("N");
        try
        {
            var svc = new AutostartService(keyPath, "Memoria", () => @"C:\Apps\Memoria\Memoria.exe");

            svc.IsEnabled().Should().BeFalse();

            svc.Enable();
            svc.IsEnabled().Should().BeTrue();
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
            {
                key!.GetValue("Memoria").Should().Be("\"C:\\Apps\\Memoria\\Memoria.exe\"");
            }

            svc.Disable();
            svc.IsEnabled().Should().BeFalse();
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\MemoriaTest", throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Disable_is_idempotent_when_value_absent()
    {
        var keyPath = @"Software\MemoriaTest\" + System.Guid.NewGuid().ToString("N");
        try
        {
            var svc = new AutostartService(keyPath, "Memoria", () => @"C:\x.exe");
            svc.Invoking(s => s.Disable()).Should().NotThrow();
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\MemoriaTest", throwOnMissingSubKey: false);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~AutostartTests"
```
예상 실패: `error CS0246: ... 'AutostartRegistry'/'AutostartService'/'IAutostartService' ...` (컴파일 실패).

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/Windows/AutostartService.cs
using System;
using Microsoft.Win32;

namespace Memoria.App.Windows;

public static class AutostartRegistry
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "Memoria";

    public static string BuildCommand(string exePath) => $"\"{exePath}\"";
}

public interface IAutostartService
{
    bool IsEnabled();
    void Enable();
    void Disable();
}

public sealed class AutostartService : IAutostartService
{
    private readonly string _keyPath;
    private readonly string _valueName;
    private readonly Func<string> _exePathProvider;

    public AutostartService()
        : this(AutostartRegistry.RunKeyPath, AutostartRegistry.ValueName,
               () => Environment.ProcessPath ?? AppContext.BaseDirectory)
    {
    }

    public AutostartService(string keyPath, string valueName, Func<string> exePathProvider)
    {
        _keyPath = keyPath;
        _valueName = valueName;
        _exePathProvider = exePathProvider;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        return key?.GetValue(_valueName) is string;
    }

    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath);
        key.SetValue(_valueName, AutostartRegistry.BuildCommand(_exePathProvider()), RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }
}
```
> `Microsoft.Win32.Registry` 타입은 `net9.0-windows` TFM에 in-box로 포함된다(별도 NuGet 불필요).

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~AutostartTests"
```
예상: `Passed!  - Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/Windows/AutostartService.cs tests/Memoria.Tests/Windows/AutostartTests.cs
git commit -m "feat(app): add HKCU Run autostart service with pure key/value builder

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: 파이프 메시지 직렬화 (PipeCommand / PipeMessage, 순수 로직)

**Files:**
- Create: `src/Memoria.App/Windows/PipeMessage.cs`
- Test: `tests/Memoria.Tests/Windows/PipeMessageTests.cs`

**Interfaces:**
- Produces: `enum PipeCommand { NewNote, Open }`, `static class PipeMessage` { `string Serialize(PipeCommand)`; `bool TryParse(string?, out PipeCommand)` }. 와이어 포맷: `NewNote`→`"new-note"`, `Open`→`"open"`(한 줄, 개행 종단).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/Windows/PipeMessageTests.cs
using FluentAssertions;
using Memoria.App.Windows;
using Xunit;

namespace Memoria.Tests.Windows;

public class PipeMessageTests
{
    [Theory]
    [InlineData(PipeCommand.NewNote, "new-note")]
    [InlineData(PipeCommand.Open, "open")]
    public void Serialize_produces_stable_wire_token(PipeCommand cmd, string expected)
    {
        PipeMessage.Serialize(cmd).Should().Be(expected);
    }

    [Theory]
    [InlineData(PipeCommand.NewNote)]
    [InlineData(PipeCommand.Open)]
    public void Roundtrips(PipeCommand cmd)
    {
        PipeMessage.TryParse(PipeMessage.Serialize(cmd), out var parsed).Should().BeTrue();
        parsed.Should().Be(cmd);
    }

    [Theory]
    [InlineData(" NEW-NOTE \r\n", PipeCommand.NewNote)]
    [InlineData("Open", PipeCommand.Open)]
    public void TryParse_is_case_insensitive_and_trims(string line, PipeCommand expected)
    {
        PipeMessage.TryParse(line, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("delete")]
    public void TryParse_rejects_unknown(string? line)
    {
        PipeMessage.TryParse(line, out _).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~PipeMessageTests"
```
예상 실패: `error CS0246: ... 'PipeCommand'/'PipeMessage' ...`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/Windows/PipeMessage.cs
using System;

namespace Memoria.App.Windows;

public enum PipeCommand
{
    NewNote,
    Open,
}

public static class PipeMessage
{
    public static string Serialize(PipeCommand command) => command switch
    {
        PipeCommand.NewNote => "new-note",
        PipeCommand.Open => "open",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    public static bool TryParse(string? line, out PipeCommand command)
    {
        command = default;
        switch (line?.Trim().ToLowerInvariant())
        {
            case "new-note":
                command = PipeCommand.NewNote;
                return true;
            case "open":
                command = PipeCommand.Open;
                return true;
            default:
                return false;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~PipeMessageTests"
```
예상: `Passed!  - Failed: 0, Passed: 9`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/Windows/PipeMessage.cs tests/Memoria.Tests/Windows/PipeMessageTests.cs
git commit -m "feat(app): add pipe command serialization for single-instance IPC

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: 단일 인스턴스 서비스 (Mutex + named pipe)

**Files:**
- Create: `src/Memoria.App/Windows/SingleInstanceService.cs`
- Test: `tests/Memoria.Tests/Windows/SingleInstanceServiceTests.cs`

**Interfaces:**
- Consumes: `PipeCommand`, `PipeMessage`(Task 3).
- Produces: `interface ISingleInstanceService : IDisposable` { `bool TryAcquire()` — 첫 인스턴스면 true(Mutex 획득 + pipe 서버 시작), 이미 실행 중이면 false; `void SignalExistingInstance(PipeCommand command)` — 두 번째 인스턴스가 명령 전달; `event EventHandler<PipeCommand>? CommandReceived` }. `sealed class SingleInstanceService`(기본 생성자 = 고정 Mutex/pipe 이름; 테스트용 생성자 = 커스텀 이름 주입).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Memoria.Tests/Windows/SingleInstanceServiceTests.cs
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Memoria.App.Windows;
using Xunit;

namespace Memoria.Tests.Windows;

public class SingleInstanceServiceTests
{
    [Fact]
    public void First_instance_acquires_second_does_not()
    {
        var id = Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceService($"mtx-{id}", $"pipe-{id}");
        using var second = new SingleInstanceService($"mtx-{id}", $"pipe-{id}");

        first.TryAcquire().Should().BeTrue();
        second.TryAcquire().Should().BeFalse();
    }

    [Fact]
    public async Task Second_instance_signals_first_via_pipe()
    {
        var id = Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceService($"mtx-{id}", $"pipe-{id}");
        var tcs = new TaskCompletionSource<PipeCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.CommandReceived += (_, cmd) => tcs.TrySetResult(cmd);

        first.TryAcquire().Should().BeTrue();

        using var second = new SingleInstanceService($"mtx-{id}", $"pipe-{id}");
        second.TryAcquire().Should().BeFalse();
        second.SignalExistingInstance(PipeCommand.NewNote);

        var winner = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        winner.Should().Be(tcs.Task);
        (await tcs.Task).Should().Be(PipeCommand.NewNote);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~SingleInstanceServiceTests"
```
예상 실패: `error CS0246: ... 'SingleInstanceService'/'ISingleInstanceService' ...`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/Windows/SingleInstanceService.cs
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace Memoria.App.Windows;

public interface ISingleInstanceService : IDisposable
{
    bool TryAcquire();
    void SignalExistingInstance(PipeCommand command);
    event EventHandler<PipeCommand>? CommandReceived;
}

public sealed class SingleInstanceService : ISingleInstanceService
{
    private const string DefaultMutexName = "Memoria.SingleInstance.Mutex";
    private const string DefaultPipeName = "Memoria.SingleInstance.Pipe";

    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private CancellationTokenSource? _cts;
    private Task? _serverLoop;

    public event EventHandler<PipeCommand>? CommandReceived;

    public SingleInstanceService() : this(DefaultMutexName, DefaultPipeName) { }

    public SingleInstanceService(string mutexName, string pipeName)
    {
        _mutexName = mutexName;
        _pipeName = pipeName;
    }

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _cts = new CancellationTokenSource();
        _serverLoop = Task.Run(() => ServerLoopAsync(_cts.Token));
        return true;
    }

    private async Task ServerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(
                _pipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using var reader = new StreamReader(server);
            string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (line is not null && PipeMessage.TryParse(line, out var command))
                CommandReceived?.Invoke(this, command);
        }
    }

    public void SignalExistingInstance(PipeCommand command)
    {
        using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
        client.Connect(2000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        writer.WriteLine(PipeMessage.Serialize(command));
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* shutdown best-effort */ }
        _cts?.Dispose();
        _mutex?.Dispose();
        _mutex = null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~SingleInstanceServiceTests"
```
예상: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/Windows/SingleInstanceService.cs tests/Memoria.Tests/Windows/SingleInstanceServiceTests.cs
git commit -m "feat(app): add single-instance service via named mutex and pipe IPC

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: 전역 단축키 서비스 (message-only 창) + 포그라운드 헬퍼

**Files:**
- Create: `src/Memoria.App/Windows/GlobalHotkeyService.cs`, `src/Memoria.App/Windows/ForegroundHelper.cs`
- Test: `tests/Memoria.Tests/Windows/GlobalHotkeyServiceTests.cs`

**Interfaces:**
- Consumes: `HotkeyParser.TryParse`, `HotkeyParser.ModNoRepeat`, `HotkeyModifiers`, `ParsedHotkey`(Task 1).
- Produces:
  - `interface IGlobalHotkeyService : IDisposable` { `bool Register(string hotkey)` — 파싱 성공+`RegisterHotKey` 성공 시 true; `void Unregister()`; `event EventHandler? HotkeyPressed` }.
  - `sealed class GlobalHotkeyService`(HWND_MESSAGE message-only `HwndSource` + `WM_HOTKEY` 후킹).
  - `static class ForegroundHelper` { `void AllowAny()`(=`AllowSetForegroundWindow(ASFW_ANY)`); `void BringToFront(IntPtr hWnd)`(=`SetForegroundWindow`) }.

- [ ] **Step 1: Write the failing test**

> 실제 전역 등록은 STA/메시지 펌프가 필요해 자동화 불가 → 파싱 단계의 거부 동작만 자동 테스트하고, 실제 단축키 동작은 본 Task의 수동 검증 체크포인트로 확인한다.

```csharp
// tests/Memoria.Tests/Windows/GlobalHotkeyServiceTests.cs
using FluentAssertions;
using Memoria.App.Windows;
using Xunit;

namespace Memoria.Tests.Windows;

public class GlobalHotkeyServiceTests
{
    [Fact]
    public void Register_returns_false_for_unparseable_hotkey()
    {
        using var svc = new GlobalHotkeyService();
        svc.Register("not-a-hotkey").Should().BeFalse();
    }

    [Fact]
    public void Register_returns_false_for_empty_string()
    {
        using var svc = new GlobalHotkeyService();
        svc.Register("").Should().BeFalse();
    }

    [Fact]
    public void Dispose_without_registration_does_not_throw()
    {
        var svc = new GlobalHotkeyService();
        svc.Invoking(s => s.Dispose()).Should().NotThrow();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GlobalHotkeyServiceTests"
```
예상 실패: `error CS0246: ... 'GlobalHotkeyService' ...`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Memoria.App/Windows/ForegroundHelper.cs
using System;
using System.Runtime.InteropServices;

namespace Memoria.App.Windows;

public static class ForegroundHelper
{
    private const int ASFW_ANY = -1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    public static void AllowAny() => AllowSetForegroundWindow(ASFW_ANY);

    public static void BringToFront(IntPtr hWnd) => SetForegroundWindow(hWnd);
}
```

```csharp
// src/Memoria.App/Windows/GlobalHotkeyService.cs
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Memoria.App.Windows;

public interface IGlobalHotkeyService : IDisposable
{
    bool Register(string hotkey);
    void Unregister();
    event EventHandler? HotkeyPressed;
}

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xB001;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public bool Register(string hotkey)
    {
        if (!HotkeyParser.TryParse(hotkey, out var parsed))
            return false;

        EnsureSource();
        Unregister();

        uint modifiers = (uint)parsed.Modifiers | HotkeyParser.ModNoRepeat;
        _registered = RegisterHotKey(_source!.Handle, HotkeyId, modifiers, parsed.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _source is not null)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    private void EnsureSource()
    {
        if (_source is not null)
            return;

        var parameters = new HwndSourceParameters("MemoriaHotkeyWindow")
        {
            ParentWindow = HWND_MESSAGE, // message-only window
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests" --filter "FullyQualifiedName~GlobalHotkeyServiceTests"
```
예상: `Passed!  - Failed: 0, Passed: 3`.

- [ ] **Step 6: 수동 검증 체크포인트 — 전역 단축키(글로벌 동작)**
  먼저 Task 7의 App 조립을 마친 뒤(또는 임시로 `App.OnStartup`에서 `_hotkey.Register("Ctrl+Alt+N")` + `HotkeyPressed`에 `MessageBox` 연결) 다음을 눈으로 확인:
  1. `dotnet.exe run --project "C:\...\src\Memoria.App"` 로 앱 실행 후 **다른 앱(메모장/브라우저)에 포커스**를 둔다.
  2. `Ctrl+Alt+N`을 누르면 `HotkeyPressed`가 발화(임시 MessageBox 표시 또는 새 메모 생성)되는지 확인.
  3. 메인 창을 닫기(X)로 숨긴 상태에서도 `Ctrl+Alt+N`이 여전히 동작하는지 확인(message-only 창 수명 유지 검증).
  4. 키를 길게 누르고 있을 때 반복 발화가 없는지 확인(`MOD_NOREPEAT` 동작).
  5. 다른 앱이 이미 `Ctrl+Alt+N`을 점유한 경우 `Register`가 `false`를 반환하는지 로그로 확인(설정에서 재지정 흐름은 M7).

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/Windows/GlobalHotkeyService.cs src/Memoria.App/Windows/ForegroundHelper.cs tests/Memoria.Tests/Windows/GlobalHotkeyServiceTests.cs
git commit -m "feat(app): add message-only global hotkey service and foreground helper

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: 트레이 서비스 (H.NotifyIcon.Wpf)

**Files:**
- Modify: `src/Memoria.App/Memoria.App.csproj`(PackageReference + .ico 포함)
- Create: `src/Memoria.App/Assets/app.ico`, `src/Memoria.App/Windows/TrayService.cs`
- Test: 자동 테스트 없음(서드파티 WPF 트레이 컨트롤은 메시지 펌프/STA 필요) → **수동 검증 체크포인트**.

**Interfaces:**
- Produces: `interface ITrayService : IDisposable` { `void Initialize()`; `event EventHandler? ToggleRequested`(좌클릭); `event EventHandler? NewNoteRequested`; `event EventHandler? OpenRequested`; `event EventHandler? SettingsRequested`; `event EventHandler? ExitRequested` }. `sealed class TrayService : ITrayService`.

- [ ] **Step 1: 패키지/리소스 추가 (빌드 검증)**
  `src/Memoria.App/Memoria.App.csproj`에 추가:

```xml
  <ItemGroup>
    <PackageReference Include="H.NotifyIcon.Wpf" Version="2.1.3" />
  </ItemGroup>
  <ItemGroup>
    <Resource Include="Assets\app.ico" />
  </ItemGroup>
```
  `src/Memoria.App/Assets/app.ico`를 배치(임시로 16/32px 단색 아이콘이라도 포함). `<Resource>`로 포함하므로 단일파일 publish에 내장된다.

- [ ] **Step 2: 빌드로 패키지 복원/참조 확인**

```
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\Memoria.sln"
```
예상: `Build succeeded`(H.NotifyIcon.Wpf 복원 성공). 실패 시 버전 가용성 확인 후 최신 2.x로 조정.

- [ ] **Step 3: TrayService 구현**

```csharp
// src/Memoria.App/Windows/TrayService.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;

namespace Memoria.App.Windows;

public interface ITrayService : IDisposable
{
    void Initialize();
    event EventHandler? ToggleRequested;
    event EventHandler? NewNoteRequested;
    event EventHandler? OpenRequested;
    event EventHandler? SettingsRequested;
    event EventHandler? ExitRequested;
}

public sealed class TrayService : ITrayService
{
    private TaskbarIcon? _icon;

    public event EventHandler? ToggleRequested;
    public event EventHandler? NewNoteRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        if (_icon is not null)
            return;

        _icon = new TaskbarIcon
        {
            ToolTipText = "Memoria",
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")),
        };

        _icon.TrayLeftMouseUp += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenu();
        menu.Items.Add(BuildItem("새 메모(Ctrl+Alt+N)", (_, _) => NewNoteRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(BuildItem("열기", (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(BuildItem("설정", (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildItem("종료", (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));
        _icon.ContextMenu = menu;

        _icon.ForceCreate();
    }

    private static MenuItem BuildItem(string header, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
```
> 테마 적용: 컨텍스트 메뉴의 색/브러시도 `DynamicResource` 테마 키를 사용해야 한다(StaticResource 금지). 실제 브러시 바인딩은 M7 테마 사전과 연계하되, M6에서는 기본 메뉴를 구성한다.

- [ ] **Step 4: 수동 검증 체크포인트 — 트레이 동작**
  Task 7 조립 후 앱 실행하여 확인:
  1. 작업표시줄 알림 영역에 Memoria 트레이 아이콘이 표시되는가(`.ico` 렌더 확인).
  2. **좌클릭**: 메인 창이 보이는 상태면 숨김, 숨김 상태면 표시(토글)되는가.
  3. **우클릭 메뉴**: `새 메모(Ctrl+Alt+N)`, `열기`, `설정`, 구분선, `종료` 항목이 순서대로 나오는가.
  4. `새 메모` 클릭 시 새 메모 생성 + 창 포그라운드, `열기` 클릭 시 창 표시, `종료` 클릭 시 앱이 실제로 프로세스 종료되는가.
  5. 단일파일 publish(`dotnet publish ... -p:PublishSingleFile=true`) 산출물에서도 아이콘이 정상 로드되는가(리소스 내장 검증).

- [ ] **Step 5: Commit**

```
git add src/Memoria.App/Memoria.App.csproj src/Memoria.App/Assets/app.ico src/Memoria.App/Windows/TrayService.cs
git commit -m "feat(app): add system tray service with H.NotifyIcon.Wpf

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: App 조립 — 단축키/트레이/단일 인스턴스/자동시작/closeToTray 배선

> **누적 패치 원칙(계약 §9.4):** App.xaml.cs를 **전체 재작성하지 않는다.** M2가 작성한 기존 부트스트랩(`AppPaths.EnsureDirectories` → `AddMemoriaCore` → `AppServices.Initialize` → `EnsureReady` → `MainWindow` 생성/Show 등)을 **그대로 보존**하고, M6는 계약 §9.4의 지정 위치(2)·(10)·(11) 및 `OnExit`에 자신의 배선만 **추가/삽입**한다. 서비스 해석은 계약 §9.2의 `AppServices.Resolve<T>()`만 사용한다(임의 정적 로케이터 가정 금지).

**Files:**
- Modify (additive): `src/Memoria.App/App.xaml.cs`(기존 §9.4 부트스트랩에 단일인스턴스/트레이/단축키/자동시작 배선 삽입), `src/Memoria.App/MainWindow.xaml.cs`(closeToTray code-behind)
- Test: 자동 테스트 없음(애플리케이션 수명/창 표시/포그라운드는 메시지 펌프 필요) → **수동 검증 체크포인트**. 단축키 파싱·레지스트리·파이프·단일 인스턴스의 순수 로직은 Task 1~4에서 이미 자동 검증됨.

**Interfaces:**
- Consumes:
  - `Memoria.Core.Data.ISettingsRepository` { `GetOrDefault(string, string)` } — `SettingsKeys.HotkeyNewNote`/`CloseToTray`/`Autostart` 읽기.
  - `Memoria.Core.Data.INoteRepository`(M1) — 새 메모 생성은 M2 `MainViewModel.NewPlainNoteCommand`(`IRelayCommand`, 계약 §9.3)에 위임.
  - `MainViewModel.NewPlainNoteCommand`(M2 산출, `CommunityToolkit.Mvvm.Input.IRelayCommand`, 계약 §9.3 — **`NewNoteCommand` 아님**).
  - `MainViewModel.OpenSettingsCommand`(M2가 **스텁으로 선언**, 본문은 M7; 계약 §9.3). 부트스트랩 순서상 M6가 안전하게 Consumes — 스텁이 항상 존재하므로 순서 의존성 없음.
  - `MainWindow.ViewModel`(계약 §9.3의 public 접근자 `public MainViewModel ViewModel => (MainViewModel)DataContext;`).
  - `AppServices.Resolve<T>()`(계약 §9.2의 App 서비스 로케이터).
  - `IGlobalHotkeyService`/`ITrayService`/`IAutostartService`/`ISingleInstanceService`/`ForegroundHelper`(Task 2·4·5·6).
- Produces: 조립된 부트스트랩(애플리케이션 진입 동작). 새 인터페이스 없음.

- [ ] **Step 1: 기존 App.xaml.cs에 M6 배선 추가(전체 재작성 금지)**

  M2가 작성한 `App : Application`을 보존한 채 아래를 **삽입**한다. 새 필드/메서드는 partial class 본문에 추가하고, 기존 `OnStartup`/`OnExit` 본문의 지정 위치에 호출만 끼워 넣는다.

  **(a) 필드 추가** — 클래스 본문 상단에 추가(기존 M2 필드는 유지):

```csharp
// src/Memoria.App/App.xaml.cs — 필드 추가
using System;
using System.Windows;
using System.Windows.Interop;
using Memoria.App.Windows;
using Memoria.Core;
using Memoria.Core.Data;

// (partial class App 내부)
private ISingleInstanceService _singleInstance = null!;
private IGlobalHotkeyService _hotkey = null!;
private ITrayService _tray = null!;
private IAutostartService _autostart = null!;
```

  **(b) §9.4 (2) 단일 인스턴스 게이트** — `base.OnStartup(e)` 직후, M2의 `AppPaths.EnsureDirectories()` 다음, `AddMemoriaCore` 이전에 삽입:

```csharp
_singleInstance = new SingleInstanceService();
if (!_singleInstance.TryAcquire())
{
    ForegroundHelper.AllowAny(); // 첫 인스턴스가 SetForegroundWindow 가능하도록
    _singleInstance.SignalExistingInstance(PipeCommand.NewNote);
    _singleInstance.Dispose();
    Shutdown();
    return;
}
```

  **(c) §9.4 (10)~(11) 트레이/단축키/자동시작 배선** — M2가 `MainWindow`를 생성한 뒤(`AppServices.Resolve<MainWindow>()`/`MainWindow` 할당 이후), `Show` 호출 직전에 삽입. **`MainWindow` 인스턴스는 새로 만들지 않고 M2가 생성한 것을 재사용**한다:

```csharp
var settings = AppServices.Resolve<ISettingsRepository>(); // 계약 §9.2
var mainWindow = (MainWindow)MainWindow!;                   // M2가 생성·할당한 인스턴스 재사용

_autostart = new AutostartService();
_tray = new TrayService();
_hotkey = new GlobalHotkeyService();

// 자동시작 설정 동기화
bool autostartWanted = bool.Parse(settings.GetOrDefault(SettingsKeys.Autostart, "true"));
if (autostartWanted) _autostart.Enable(); else _autostart.Disable();

// 트레이
_tray.Initialize();
_tray.ToggleRequested += (_, _) => ToggleMainWindow();
_tray.NewNoteRequested += (_, _) => NewNoteForeground();
_tray.OpenRequested += (_, _) => ShowMainWindow();
_tray.SettingsRequested += (_, _) => mainWindow.ViewModel.OpenSettingsCommand.Execute(null); // 계약 §9.3 (M2 스텁, 본문 M7)
_tray.ExitRequested += (_, _) => ExitApplication();

// 전역 단축키
string hotkeyStr = settings.GetOrDefault(SettingsKeys.HotkeyNewNote, "Ctrl+Alt+N");
_hotkey.HotkeyPressed += (_, _) => NewNoteForeground();
_hotkey.Register(hotkeyStr); // 등록 실패 시 false 반환(재지정 UI는 M7)

// 단일 인스턴스 IPC 수신 → UI 스레드로 마샬링
_singleInstance.CommandReceived += (_, cmd) =>
    Dispatcher.Invoke(() =>
    {
        if (cmd == PipeCommand.NewNote) NewNoteForeground();
        else ShowMainWindow();
    });
```

  **(d) 헬퍼 메서드 추가** — partial class 본문에 추가:

```csharp
private void NewNoteForeground()
{
    Dispatcher.Invoke(() =>
    {
        ((MainWindow)MainWindow!).ViewModel.NewPlainNoteCommand.Execute(null); // 계약 §9.3
        ShowMainWindow();
    });
}

private void ShowMainWindow()
{
    var mainWindow = (MainWindow)MainWindow!;
    if (!mainWindow.IsVisible) mainWindow.Show();
    if (mainWindow.WindowState == WindowState.Minimized)
        mainWindow.WindowState = WindowState.Normal;
    mainWindow.Activate();
    var handle = new WindowInteropHelper(mainWindow).Handle;
    ForegroundHelper.BringToFront(handle);
}

private void ToggleMainWindow()
{
    var mainWindow = (MainWindow)MainWindow!;
    if (mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized)
        mainWindow.Hide();
    else
        ShowMainWindow();
}

private void ExitApplication()
{
    ((MainWindow)MainWindow!).AllowClose = true;
    _hotkey.Dispose();
    _tray.Dispose();
    _singleInstance.Dispose();
    Shutdown();
}
```

  **(e) §9.4 OnExit 정리** — M2가 작성한 `OnExit`의 기존 정리(Autosave Flush, wal_checkpoint, Dispose 등)를 **보존**하고, `base.OnExit(e)` 이전에 다음 Dispose만 추가:

```csharp
_hotkey?.Dispose();
_tray?.Dispose();
_singleInstance?.Dispose();
```
> 모든 서비스 해석은 계약 §9.2 `AppServices.Resolve<T>()`로만 한다. `MainWindow.ViewModel`(§9.3 public 접근자), `NewPlainNoteCommand`/`OpenSettingsCommand`(§9.3)는 계약이 보장하는 심볼이므로 그대로 사용한다 — 이름 치환/발명 금지.

- [ ] **Step 2: closeToTray code-behind 작성(얇게)**

```csharp
// src/Memoria.App/MainWindow.xaml.cs (해당 부분)
using System.ComponentModel;
using System.Windows;
using Memoria.Core;
using Memoria.Core.Data;

namespace Memoria.App;

public partial class MainWindow : Window
{
    private readonly ISettingsRepository _settings;

    public bool AllowClose { get; set; }

    // ViewModel/ISettingsRepository는 생성자 주입(M2 DI). 기존 생성자에 _settings 보관만 추가.

    protected override void OnClosing(CancelEventArgs e)
    {
        bool closeToTray = bool.Parse(_settings.GetOrDefault(SettingsKeys.CloseToTray, "true"));
        if (closeToTray && !AllowClose)
        {
            e.Cancel = true;
            Hide(); // HWND 유지(파괴 금지)
            return;
        }
        base.OnClosing(e);
    }
}
```

- [ ] **Step 3: 빌드로 배선 컴파일 검증**

```
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\Memoria.sln"
```
예상: `Build succeeded`. 계약 §9.2/§9.3이 보장하는 `AppServices.Resolve<T>()`·`MainWindow.ViewModel`·`MainViewModel.NewPlainNoteCommand`·`OpenSettingsCommand`(M2 스텁) 심볼에 연결되므로 추가 치환은 불필요하다.

- [ ] **Step 4: 전체 테스트 회귀 확인**

```
dotnet.exe test "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests"
```
예상: M1~M6 전체 통과(`Failed: 0`). M6 신규 단위 테스트(파서/자동시작/파이프/단일인스턴스/단축키거부)가 모두 PASS.

- [ ] **Step 5: 수동 검증 체크포인트 — 통합 동작**
  `dotnet.exe run --project "C:\...\src\Memoria.App"` 로 실행하여 확인:
  1. **단일 인스턴스**: 앱이 켜진 상태에서 exe를 한 번 더 실행 → 새 프로세스는 즉시 종료되고, 기존 창이 **앞으로 튀어나오며 새 메모가 생성**되는가(작업표시줄 깜빡임 없이 포그라운드 — `AllowSetForegroundWindow`+`SetForegroundWindow`).
  2. **closeToTray**: `app.closeToTray=true`(기본)에서 창 X 클릭 → 종료되지 않고 트레이로 숨는가. 트레이 좌클릭으로 복귀하는가. HWND 유지로 단축키가 계속 동작하는가.
  3. **자동시작**: 앱 실행 후 `regedit`에서 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`에 값 이름 `Memoria` = `"<exe 경로>"`가 생성되는가(`app.autostart=true`). 설정을 false로 두고 재실행 시 값이 제거되는가.
  4. **전역 단축키 통합**: 다른 앱 포커스에서 `Ctrl+Alt+N` → 메인 창 포그라운드 + 새 메모 포커스(Task 5 체크포인트 재확인, 이번엔 임시 MessageBox가 아닌 실제 새 메모 흐름).
  5. **종료 경로**: 트레이 `종료` → `AllowClose=true`로 실제 프로세스 종료, Mutex/pipe/단축키/트레이가 모두 해제되어 재실행 시 단일 인스턴스가 정상 재획득되는가.

- [ ] **Step 6: Commit**

```
git add src/Memoria.App/App.xaml.cs src/Memoria.App/MainWindow.xaml.cs
git commit -m "feat(app): wire global hotkey, tray, single-instance, autostart, close-to-tray

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 검증 요약

- **자동 테스트(xUnit, Windows dotnet.exe)**: `HotkeyParser`(9), `Autostart` 빌더+HKCU 라운드트립(4), `PipeMessage`(9), `SingleInstanceService` Mutex+pipe(2), `GlobalHotkeyService` 파싱 거부/Dispose(3).
- **수동 검증 체크포인트**: 전역 단축키(Task 5), 트레이 표시/메뉴/단일파일 아이콘(Task 6), 단일 인스턴스 포그라운드·closeToTray·자동시작 레지스트리·종료 경로(Task 7).
- **빌드/테스트는 Windows `dotnet.exe`** 로만 수행하며, 모든 명령은 §7 규약의 Windows 절대경로를 사용한다.

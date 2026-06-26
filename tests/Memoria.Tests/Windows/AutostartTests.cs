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

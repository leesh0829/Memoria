using Microsoft.Win32;

namespace Memoria.App.Services;

/// <summary>절전/복귀·로그온 잠금 전환을 UI 정지 시각과 대조할 수 있게 기록한다.</summary>
public sealed class SystemLifecycleDiagnostics : IDisposable
{
    private int _disposed;

    public SystemLifecycleDiagnostics()
    {
        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            AppLog.Action("System.DiagnosticsStarted");
        }
        catch
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            throw;
        }
    }

    private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        => AppLog.Action("System.PowerMode", $"mode={e.Mode}");

    private static void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        => AppLog.Action("System.SessionSwitch", $"reason={e.Reason}");

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }
}

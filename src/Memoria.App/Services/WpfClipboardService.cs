using System.Windows;
using System.Runtime.InteropServices;

namespace Memoria.App.Services;

public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        try
        {
            Clipboard.SetText(text ?? "");
        }
        catch (COMException ex)
        {
            // Explorer/다른 앱이 잠시 클립보드를 점유한 경우 전역 예외 처리와 경고창까지
            // 번지는 것을 막는다. 클립보드 잠금은 다음 복사에서 자연스럽게 해소된다.
            AppLog.Warn($"Clipboard unavailable: 0x{ex.HResult:X8}");
        }
    }
}

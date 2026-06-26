using System.Windows;

namespace Memoria.App.Services;

public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text) => Clipboard.SetText(text ?? "");
}

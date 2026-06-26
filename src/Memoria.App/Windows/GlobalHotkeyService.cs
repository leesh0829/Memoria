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

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

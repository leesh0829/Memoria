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

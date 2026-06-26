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

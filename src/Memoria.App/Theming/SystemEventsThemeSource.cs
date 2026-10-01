// src/Memoria.App/Theming/SystemEventsThemeSource.cs
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Memoria.App.Theming;

public sealed class SystemEventsThemeSource : ISystemThemeSource
{
    private readonly Dispatcher? _dispatcher;
    private int _refreshPending;
    private int _lastThemeState = -1;
    private int _disposed;

    public event EventHandler? SystemThemeChanged;

    public SystemEventsThemeSource()
    {
        _dispatcher = Application.Current?.Dispatcher;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsLight()
    {
        var cached = Volatile.Read(ref _lastThemeState);
        if (cached >= 0) return cached == 1;

        // Registry.GetValue는 절전/로그온 직후 수 초간 멎을 수 있다. UI 스레드에서는
        // 임시 기본값을 반환하고 실제 조회를 작업 스레드에서 끝낸 뒤 테마를 갱신한다.
        if (_dispatcher is not null && _dispatcher.CheckAccess())
        {
            QueueRefresh();
            return true;
        }

        return ReadAndCacheTheme() == 1;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // General 범주에 테마(색) 변경이 포함된다.
        if (e.Category != UserPreferenceCategory.General || Volatile.Read(ref _disposed) != 0) return;
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (Volatile.Read(ref _disposed) != 0
            || Interlocked.CompareExchange(ref _refreshPending, 1, 0) != 0) return;
        ThreadPool.QueueUserWorkItem(_ => RefreshTheme());
    }

    private void RefreshTheme()
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            var previous = Volatile.Read(ref _lastThemeState);
            var current = ReadAndCacheTheme();
            if (previous == current || Volatile.Read(ref _disposed) != 0) return;

            if (_dispatcher is null || _dispatcher.CheckAccess())
            {
                RaiseChanged();
                return;
            }

            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RaiseChanged));
        }
        catch
        {
            // 시스템 테마 감지는 부가 기능이므로 실패해도 앱 동작을 막지 않는다.
        }
        finally
        {
            Interlocked.Exchange(ref _refreshPending, 0);
        }
    }

    private int ReadAndCacheTheme()
    {
        var state = SystemThemeReader.ReadIsLight() ? 1 : 0;
        Volatile.Write(ref _lastThemeState, state);
        return state;
    }

    private void RaiseChanged()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        SystemThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}

// src/Memoria.App/Views/ChecklistView.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Memoria.App.ViewModels;
using Memoria.Core;
using Memoria.Core.Data;

namespace Memoria.App.Views;

public partial class ChecklistView : UserControl
{
    private readonly DispatcherTimer _debounce;
    private Window? _ownerWindow;

    public ChecklistView()
    {
        InitializeComponent();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ResolveDebounceMs()) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            (DataContext as ChecklistViewModel)?.FlushSaves();
        };
        // 텍스트 변경마다 디바운스 재시작: ItemsControl 내부 TextBox 변경을 가로채 타이머 리셋
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(OnAnyTextChanged));
        Loaded += OnLoaded;
    }

    // 설정(autosave.debounceMs)을 읽어 적용. DI 미초기화(디자이너 등)면 기본 500ms.
    private static int ResolveDebounceMs()
    {
        try
        {
            var settings = AppServices.Resolve<ISettingsRepository>();
            return int.Parse(settings.GetOrDefault(SettingsKeys.AutosaveDebounceMs, "500"));
        }
        catch
        {
            return 500;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 소속 Window가 비활성화될 때 보류 저장을 즉시 확정한다(포커스 이탈 = 저장 시점).
        if (_ownerWindow is null)
        {
            _ownerWindow = Window.GetWindow(this);
            if (_ownerWindow is not null)
                _ownerWindow.Deactivated += OnOwnerDeactivated;
        }
    }

    private void OnOwnerDeactivated(object? sender, EventArgs e)
    {
        _debounce.Stop();
        (DataContext as ChecklistViewModel)?.FlushSaves();
    }

    private void OnAnyTextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnClientSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: ChecklistItemViewModel item } cb) return;

        // 로드 시 바인딩 실현이나 자동태깅(FlushSaves의 ClientId 재대입)으로도 SelectionChanged가 발생한다.
        // 그것까지 '수동 교정(IsManual=true)'으로 처리하면 자동 재분류가 영구 동결된다.
        // 사용자가 직접 드롭다운에서 고른 경우(열려 있거나 키보드 포커스가 있을 때)만 커밋한다.
        if (!cb.IsDropDownOpen && !cb.IsKeyboardFocusWithin) return;

        if (DataContext is ChecklistViewModel vm && vm.CommitClientCommand.CanExecute(item))
            vm.CommitClientCommand.Execute(item);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _debounce.Stop();
        if (_ownerWindow is not null)
        {
            _ownerWindow.Deactivated -= OnOwnerDeactivated;
            _ownerWindow = null;
        }
        (DataContext as ChecklistViewModel)?.FlushSaves();   // 즉시 flush
    }
}

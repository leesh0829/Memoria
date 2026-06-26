// src/Memoria.App/Views/ChecklistView.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Memoria.App.ViewModels;

namespace Memoria.App.Views;

public partial class ChecklistView : UserControl
{
    private readonly DispatcherTimer _debounce;

    public ChecklistView()
    {
        InitializeComponent();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            (DataContext as ChecklistViewModel)?.FlushSaves();
        };
        // 텍스트 변경마다 디바운스 재시작: ItemsControl 내부 TextBox 변경을 가로채 타이머 리셋
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(OnAnyTextChanged));
    }

    private void OnAnyTextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnClientSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: ChecklistItemViewModel item }
            && DataContext is ChecklistViewModel vm
            && vm.CommitClientCommand.CanExecute(item))
        {
            vm.CommitClientCommand.Execute(item);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _debounce.Stop();
        (DataContext as ChecklistViewModel)?.FlushSaves();   // 즉시 flush
    }
}

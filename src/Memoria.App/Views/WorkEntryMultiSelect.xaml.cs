using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Memoria.App.ViewModels;

namespace Memoria.App.Views;

public partial class WorkEntryMultiSelect : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(ObservableCollection<WorkEntryChoice>), typeof(WorkEntryMultiSelect),
        new PropertyMetadata(null, OnItemsChanged));
    public ObservableCollection<WorkEntryChoice>? ItemsSource
    {
        get => (ObservableCollection<WorkEntryChoice>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }
    public string Summary
    {
        get
        {
            var text = string.Join(", ", ItemsSource?.Where(c => c.IsSelected).Select(c => c.Name) ?? []);
            return string.IsNullOrEmpty(text) ? "선택 ▾" : text + " ▾";
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SelectionClosed;
    public WorkEntryMultiSelect()
    {
        InitializeComponent();
        ChoiceList.ItemsSource = ItemsSource;
    }
    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (WorkEntryMultiSelect)d;
        // Popup은 별도 트리이므로 ElementName 탐색에 의존하지 않고 선택지를 전달한다.
        if (control.ChoiceList is not null) control.ChoiceList.ItemsSource = control.ItemsSource;
        if (e.OldValue is ObservableCollection<WorkEntryChoice> old)
            foreach (var choice in old) PropertyChangedEventManager.RemoveHandler(choice, control.OnChoiceChanged, "");
        if (e.NewValue is ObservableCollection<WorkEntryChoice> next)
            foreach (var choice in next) PropertyChangedEventManager.AddHandler(choice, control.OnChoiceChanged, "");
        control.OnChoiceChanged(null, new PropertyChangedEventArgs(""));
    }
    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, new(nameof(Summary)));
    private void OnClosed(object? sender, EventArgs e) => SelectionClosed?.Invoke(this, EventArgs.Empty);
}

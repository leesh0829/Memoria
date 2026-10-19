using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Memoria.App.ViewModels;

namespace Memoria.App.Views;

public partial class WorkEntriesView : UserControl
{
    public WorkEntriesView() => InitializeComponent();
    private void Commit()
    {
        if (Keyboard.FocusedElement is TextBox textBox)
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Keyboard.ClearFocus();
        EntriesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        EntriesGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Commit();
        (DataContext as WorkEntriesViewModel)?.SaveCommand.Execute(null);
    }
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        Commit();
        (DataContext as WorkEntriesViewModel)?.CopyCommand.Execute(null);
    }
    private void OnSelectionClosed(object? sender, EventArgs e)
    {
        Commit();
        ScheduleRefresh();
    }
    private void OnRowEditEnding(object sender, DataGridRowEditEndingEventArgs e) => ScheduleRefresh();
    private void ScheduleRefresh() => Dispatcher.BeginInvoke(DispatcherPriority.Background,
        new Action(() => (DataContext as WorkEntriesViewModel)?.Refresh()));
}

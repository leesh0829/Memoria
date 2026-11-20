using System.Windows.Controls;
using System.Windows.Input;
using Memoria.Core.Reporting;

namespace Memoria.App.Views;

public partial class WeeklyReportView : UserControl
{
    public WeeklyReportView() => InitializeComponent();

    private void OnReportPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || Keyboard.Modifiers != ModifierKeys.None || sender is not TextBox editor) return;
        var start = editor.SelectionStart;
        editor.SelectedText = ReportIndentation.Unit;
        editor.CaretIndex = start + ReportIndentation.Unit.Length;
        editor.SelectionLength = 0;
        e.Handled = true;
    }
}

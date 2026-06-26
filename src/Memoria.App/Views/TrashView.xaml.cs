// src/Memoria.App/Views/TrashView.xaml.cs
using System.Windows.Controls;

namespace Memoria.App.Views;

/// <summary>
/// 휴지통 뷰 — DataContext(TrashViewModel)는 MainWindow code-behind에서 DI로 주입.
/// </summary>
public partial class TrashView : UserControl
{
    public TrashView() => InitializeComponent();
}

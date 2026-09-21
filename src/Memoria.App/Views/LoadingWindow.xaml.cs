using System.Windows;

namespace Memoria.App.Views;

public partial class LoadingWindow : Window
{
    public LoadingWindow() => InitializeComponent();

    public void SetStatus(string message) => StatusText.Text = message;
}

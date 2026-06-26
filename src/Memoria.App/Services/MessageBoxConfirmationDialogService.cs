using System.Windows;

namespace Memoria.App.Services;

public sealed class MessageBoxConfirmationDialogService : IConfirmationDialogService
{
    public bool Confirm(string message)
        => MessageBox.Show(message, "확인", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;
}

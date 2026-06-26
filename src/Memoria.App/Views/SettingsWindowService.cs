// src/Memoria.App/Views/SettingsWindowService.cs
using System.Windows;
using Memoria.App.ViewModels;

namespace Memoria.App.Views;

public sealed class SettingsWindowService : ISettingsWindowService
{
    public void ShowSettings()
    {
        // 서비스 접근은 계약 §9.2 AppServices.Resolve<T>()로만 한다.
        var window = new SettingsWindow(
            AppServices.Resolve<SettingsViewModel>(),
            AppServices.Resolve<ClientsSettingsViewModel>())
        {
            Owner = Application.Current.MainWindow,
        };
        window.ShowDialog();
    }
}

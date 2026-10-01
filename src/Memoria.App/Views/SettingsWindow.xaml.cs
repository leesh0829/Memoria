// src/Memoria.App/Views/SettingsWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using CoreThemeMode = Memoria.Core.Models.ThemeMode;  // WPF의 Window.ThemeMode와 이름 충돌 방지

namespace Memoria.App.Views;

public partial class SettingsWindow : Window
{
    private bool _settingsSaved;

    public SettingsWindow(SettingsViewModel settingsVm, ClientsSettingsViewModel clientsVm)
    {
        InitializeComponent();

        DataContext = settingsVm;
        ClientsTab.DataContext = clientsVm;

        // 모드 ComboBox를 ThemeMode 열거형 값으로 채운다(enum→한글 라벨 매핑).
        ModeComboBox.Items.Add(new ComboBoxItem { Content = "라이트", Tag = CoreThemeMode.Light });
        ModeComboBox.Items.Add(new ComboBoxItem { Content = "다크",   Tag = CoreThemeMode.Dark });
        ModeComboBox.Items.Add(new ComboBoxItem { Content = "시스템", Tag = CoreThemeMode.System });

        Loaded += async (_, _) =>
        {
            try
            {
                await clientsVm.LoadClientsAsync();
            }
            catch (System.Exception ex)
            {
                AppLog.Error("Clients.Load", ex);
            }
        };

        // 닫힘을 잠깐 유예하고 SQLite/레지스트리 저장을 백그라운드에서 끝낸다.
        // Closed 이벤트에서 동기 저장하면 UI 스레드와 앱 종료 경로를 함께 붙잡을 수 있다.
        Closing += async (_, e) =>
        {
            if (_settingsSaved) return;

            e.Cancel = true;
            try
            {
                await settingsVm.SaveAsync();
                if (Owner is Memoria.App.MainWindow mainWindow)
                    mainWindow.ViewModel.MarkdownEnabled = settingsVm.MarkdownEnabled;
                _settingsSaved = true;
                Close();
            }
            catch (System.Exception ex)
            {
                AppLog.Error("Settings.Save", ex);
                MessageBox.Show(this,
                    $"설정을 저장하지 못했습니다.\n\n{ex.Message}",
                    "Memoria 설정", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }

    private void OnBrowseJsonClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON 키|*.json|모든 파일|*.*" };
        if (dlg.ShowDialog() == true && DataContext is SettingsViewModel vm)
            vm.ServiceAccountJsonPath = dlg.FileName;
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureDirectories();
            AppLog.Action("UI.OpenDiagnosticFolder");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.DataDirectory,
                UseShellExecute = true,
            });
        }
        catch (System.Exception ex)
        {
            AppLog.Error("Diagnostics.OpenFolder", ex);
        }
    }

    // 정보 탭 링크 → 기본 브라우저로 열기.
    private void OnOpenLinkClick(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { /* 브라우저 실행 실패 시 무시 */ }
        e.Handled = true;
    }
}

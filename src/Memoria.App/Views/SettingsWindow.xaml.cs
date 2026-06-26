// src/Memoria.App/Views/SettingsWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using Memoria.App.ViewModels;
using CoreThemeMode = Memoria.Core.Models.ThemeMode;  // WPF의 Window.ThemeMode와 이름 충돌 방지

namespace Memoria.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _settingsVm;

    public SettingsWindow(SettingsViewModel settingsVm, ClientsSettingsViewModel clientsVm)
    {
        InitializeComponent();

        _settingsVm = settingsVm;
        DataContext = settingsVm;
        ClientsTab.DataContext = clientsVm;

        // 모드 ComboBox를 ThemeMode 열거형 값으로 채운다(enum→한글 라벨 매핑).
        ModeComboBox.Items.Add(new ComboBoxItem { Content = "라이트", Tag = CoreThemeMode.Light });
        ModeComboBox.Items.Add(new ComboBoxItem { Content = "다크",   Tag = CoreThemeMode.Dark });
        ModeComboBox.Items.Add(new ComboBoxItem { Content = "시스템", Tag = CoreThemeMode.System });

        // 창을 닫을 때 비-테마 설정을 일괄 저장한다.
        Closed += (_, _) => settingsVm.SaveCommand.Execute(null);
    }

    private void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
            _settingsVm.Accent = hex; // 즉시 적용(ThemeService)
    }
}

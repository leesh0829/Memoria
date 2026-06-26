// src/Memoria.App/ViewModels/SettingsViewModel.cs
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.App.Theming;
using Memoria.App.Windows;
using Memoria.Core;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settings;
    private readonly IThemeService _theme;
    private readonly IAutostartService _autostart;
    private bool _loading;

    public string[] AvailablePresets { get; } = ThemeResolver.Presets;

    [ObservableProperty] private ThemeMode _mode;
    [ObservableProperty] private string _preset = "default";
    [ObservableProperty] private string _accent = AccentColor.Default;

    [ObservableProperty] private string _reporterName = "이승현";
    [ObservableProperty] private string _taskHeaderA = "[업무 내용]";
    [ObservableProperty] private string _issueHeaderA = "[이슈]";
    [ObservableProperty] private string _titleWordB = "주간 보고";
    [ObservableProperty] private string _issueHeaderB = "* 이슈사항:";
    [ObservableProperty] private string _reportIndent = "\t";
    [ObservableProperty] private bool _includeDoneOnly;

    [ObservableProperty] private string _hotkeyNewNote = "Ctrl+Alt+N";
    [ObservableProperty] private bool _autostartEnabled = true;   // backing: Autostart
    [ObservableProperty] private bool _closeToTray = true;

    [ObservableProperty] private int _backupRetentionCount = 7;
    [ObservableProperty] private int _trashRetentionDays = 30;

    [ObservableProperty] private bool _isHotkeyValid = true;
    [ObservableProperty] private bool _isAccentValid = true;

    public bool Autostart
    {
        get => AutostartEnabled;
        set => AutostartEnabled = value;
    }

    public bool CanSave => IsHotkeyValid && IsAccentValid;

    public SettingsViewModel(ISettingsRepository settings, IThemeService theme, IAutostartService autostart)
    {
        _settings = settings;
        _theme = theme;
        _autostart = autostart;
        Load();
    }

    private void Load()
    {
        _loading = true;

        Mode = _theme.Mode;
        Preset = _theme.Preset;
        Accent = _theme.Accent;

        ReporterName = _settings.GetOrDefault(SettingsKeys.ReporterName, "이승현");
        TaskHeaderA = _settings.GetOrDefault(SettingsKeys.FormatATaskHeader, "[업무 내용]");
        IssueHeaderA = _settings.GetOrDefault(SettingsKeys.FormatAIssueHeader, "[이슈]");
        TitleWordB = _settings.GetOrDefault(SettingsKeys.FormatBTitleWord, "주간 보고");
        IssueHeaderB = _settings.GetOrDefault(SettingsKeys.FormatBIssueHeader, "* 이슈사항:");
        ReportIndent = _settings.GetOrDefault(SettingsKeys.ReportIndent, "\t");
        IncludeDoneOnly = bool.Parse(_settings.GetOrDefault(SettingsKeys.IncludeDoneOnly, "false"));

        HotkeyNewNote = _settings.GetOrDefault(SettingsKeys.HotkeyNewNote, "Ctrl+Alt+N");
        AutostartEnabled = bool.Parse(_settings.GetOrDefault(SettingsKeys.Autostart, "true"));
        CloseToTray = bool.Parse(_settings.GetOrDefault(SettingsKeys.CloseToTray, "true"));

        BackupRetentionCount = int.Parse(_settings.GetOrDefault(SettingsKeys.BackupRetentionCount, "7"), CultureInfo.InvariantCulture);
        TrashRetentionDays = int.Parse(_settings.GetOrDefault(SettingsKeys.TrashRetentionDays, "30"), CultureInfo.InvariantCulture);

        _loading = false;
    }

    partial void OnModeChanged(ThemeMode value) => ApplyTheme();
    partial void OnPresetChanged(string value) => ApplyTheme();

    // XAML은 래퍼 프로퍼티 Autostart에 바인딩하므로, backing 변경 시 INPC를 포워딩한다.
    partial void OnAutostartEnabledChanged(bool value) => OnPropertyChanged(nameof(Autostart));

    partial void OnAccentChanged(string value)
    {
        IsAccentValid = AccentColor.IsValid(value);
        OnPropertyChanged(nameof(CanSave));
        if (IsAccentValid)
            ApplyTheme();
    }

    partial void OnHotkeyNewNoteChanged(string value)
    {
        IsHotkeyValid = HotkeyParser.TryParse(value, out _);
        OnPropertyChanged(nameof(CanSave));
    }

    private void ApplyTheme()
    {
        if (_loading || !IsAccentValid)
            return;
        _theme.Apply(Mode, Preset, Accent);
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanSave)
            return;

        _settings.Set(SettingsKeys.ReporterName, ReporterName);
        _settings.Set(SettingsKeys.FormatATaskHeader, TaskHeaderA);
        _settings.Set(SettingsKeys.FormatAIssueHeader, IssueHeaderA);
        _settings.Set(SettingsKeys.FormatBTitleWord, TitleWordB);
        _settings.Set(SettingsKeys.FormatBIssueHeader, IssueHeaderB);
        _settings.Set(SettingsKeys.ReportIndent, ReportIndent);
        _settings.Set(SettingsKeys.IncludeDoneOnly, IncludeDoneOnly ? "true" : "false");

        _settings.Set(SettingsKeys.HotkeyNewNote, HotkeyNewNote);
        _settings.Set(SettingsKeys.Autostart, AutostartEnabled ? "true" : "false");
        _settings.Set(SettingsKeys.CloseToTray, CloseToTray ? "true" : "false");

        _settings.Set(SettingsKeys.BackupRetentionCount, BackupRetentionCount.ToString(CultureInfo.InvariantCulture));
        _settings.Set(SettingsKeys.TrashRetentionDays, TrashRetentionDays.ToString(CultureInfo.InvariantCulture));

        if (AutostartEnabled)
            _autostart.Enable();
        else
            _autostart.Disable();
    }
}

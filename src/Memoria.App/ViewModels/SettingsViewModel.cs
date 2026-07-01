// src/Memoria.App/ViewModels/SettingsViewModel.cs
using System.Collections.Generic;
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

    // 색 계열 스와치(키 / 한글 라벨 / 대표색). 클릭하면 Preset이 바뀌고 즉시 적용된다.
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = new[]
    {
        new ThemeOption("default", "기본(회색)", "#0078D4"),
        new ThemeOption("blue",    "파랑",       "#1A73E8"),
        new ThemeOption("teal",    "청록",       "#0C8080"),
        new ThemeOption("green",   "초록",       "#15803D"),
        new ThemeOption("yellow",  "노랑",       "#B8860B"),
        new ThemeOption("orange",  "주황",       "#E8590C"),
        new ThemeOption("red",     "빨강",       "#D32F2F"),
        new ThemeOption("pink",    "분홍",       "#C2185B"),
        new ThemeOption("purple",  "보라",       "#6A3DB8"),
    };

    [ObservableProperty] private ThemeMode _mode;
    [ObservableProperty] private string _preset = "default";

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

    public bool Autostart
    {
        get => AutostartEnabled;
        set => AutostartEnabled = value;
    }

    public bool CanSave => IsHotkeyValid;

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

    partial void OnHotkeyNewNoteChanged(string value)
    {
        IsHotkeyValid = HotkeyParser.TryParse(value, out _);
        OnPropertyChanged(nameof(CanSave));
    }

    private void ApplyTheme()
    {
        if (_loading)
            return;
        _theme.Apply(Mode, Preset);
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

/// 설정 창의 색 계열 스와치 한 개(키/한글 라벨/대표색).
public sealed record ThemeOption(string Key, string Label, string Swatch);

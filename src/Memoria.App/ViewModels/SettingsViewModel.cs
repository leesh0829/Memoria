// src/Memoria.App/ViewModels/SettingsViewModel.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.App.Services;
using Memoria.App.Theming;
using Memoria.App.Windows;
using Memoria.Core;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settings;
    private readonly IThemeService _theme;
    private readonly IAutostartService _autostart;
    private readonly IClientRepository _clients;
    private bool _loading;

    /// 양식 C에 포함할 고객사 체크 목록.
    public ObservableCollection<FormatCClientSelection> FormatCClientOptions { get; } = new();

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

    // 양식 C 기간 요일 선택용(콤보박스 항목).
    public IReadOnlyList<DayOfWeekOption> DayOfWeekOptions { get; } = new[]
    {
        new DayOfWeekOption(DayOfWeek.Monday,    "월요일"),
        new DayOfWeekOption(DayOfWeek.Tuesday,   "화요일"),
        new DayOfWeekOption(DayOfWeek.Wednesday, "수요일"),
        new DayOfWeekOption(DayOfWeek.Thursday,  "목요일"),
        new DayOfWeekOption(DayOfWeek.Friday,    "금요일"),
        new DayOfWeekOption(DayOfWeek.Saturday,  "토요일"),
        new DayOfWeekOption(DayOfWeek.Sunday,    "일요일"),
    };

    [ObservableProperty] private ThemeMode _mode;
    [ObservableProperty] private string _preset = "default";

    [ObservableProperty] private string _reporterName = "이승현";
    [ObservableProperty] private string _taskHeaderA = "[업무 내용]";
    [ObservableProperty] private string _issueHeaderA = "[이슈]";
    [ObservableProperty] private string _titleWordB = "주간 보고";
    [ObservableProperty] private string _issueHeaderB = "* 이슈사항:";
    [ObservableProperty] private DayOfWeek _startDayC = DayOfWeek.Friday;
    [ObservableProperty] private DayOfWeek _endDayC = DayOfWeek.Thursday;
    [ObservableProperty] private string _titleHeaderC = "[ 주간 실적 ]";
    [ObservableProperty] private string _planHeaderC = "[ 차주 계획 ]";
    [ObservableProperty] private string _detailMarkerC = "o";
    [ObservableProperty] private string _reportIndent = "\t";
    [ObservableProperty] private bool _includeDoneOnly;
    [ObservableProperty] private bool _showFormatD;

    [ObservableProperty] private string _serviceAccountJsonPath = "";
    [ObservableProperty] private string _sheetId = "";
    [ObservableProperty] private string _sheetTabName = "일자 작업내역";

    [ObservableProperty] private string _hotkeyNewNote = "Ctrl+Alt+N";
    [ObservableProperty] private bool _autostartEnabled = true;   // backing: Autostart
    [ObservableProperty] private bool _closeToTray = true;
    [ObservableProperty] private bool _markdownEnabled = true;

    [ObservableProperty] private int _backupRetentionCount = 7;
    [ObservableProperty] private int _trashRetentionDays = 30;

    [ObservableProperty] private bool _isHotkeyValid = true;

    public bool Autostart
    {
        get => AutostartEnabled;
        set => AutostartEnabled = value;
    }

    public bool CanSave => IsHotkeyValid;

    public SettingsViewModel(
        ISettingsRepository settings, IThemeService theme, IAutostartService autostart, IClientRepository clients)
    {
        _settings = settings;
        _theme = theme;
        _autostart = autostart;
        _clients = clients;
        Load();
    }

    private void Load()
    {
        _loading = true;
        var all = _settings.GetAll();
        string Read(string key, string fallback)
            => all.TryGetValue(key, out var value) ? value : fallback;

        Mode = _theme.Mode;
        Preset = _theme.Preset;

        ShowFormatD = bool.TryParse(Read(SettingsKeys.ShowFormatD, "false"), out var showD) && showD;
        ReporterName = Read(SettingsKeys.ReporterName, "이승현");
        TaskHeaderA = Read(SettingsKeys.FormatATaskHeader, "[업무 내용]");
        IssueHeaderA = Read(SettingsKeys.FormatAIssueHeader, "[이슈]");
        TitleWordB = Read(SettingsKeys.FormatBTitleWord, "주간 보고");
        IssueHeaderB = Read(SettingsKeys.FormatBIssueHeader, "* 이슈사항:");
        StartDayC = ReportDayOfWeek.Parse(
            Read(SettingsKeys.FormatCStartDay, nameof(DayOfWeek.Friday)), DayOfWeek.Friday);
        EndDayC = ReportDayOfWeek.Parse(
            Read(SettingsKeys.FormatCEndDay, nameof(DayOfWeek.Thursday)), DayOfWeek.Thursday);
        TitleHeaderC = Read(SettingsKeys.FormatCTitleHeader, "[ 주간 실적 ]");
        PlanHeaderC = Read(SettingsKeys.FormatCPlanHeader, "[ 차주 계획 ]");
        DetailMarkerC = Read(SettingsKeys.FormatCDetailMarker, "o");
        LoadFormatCClients(all);
        ReportIndent = Read(SettingsKeys.ReportIndent, "\t");
        IncludeDoneOnly = bool.Parse(Read(SettingsKeys.IncludeDoneOnly, "false"));

        ServiceAccountJsonPath = Read(SettingsKeys.GoogleServiceAccountJsonPath, "");
        SheetId = Read(SettingsKeys.GoogleSheetId, "");
        SheetTabName = Read(SettingsKeys.GoogleSheetTabName, "일자 작업내역");

        HotkeyNewNote = Read(SettingsKeys.HotkeyNewNote, "Ctrl+Alt+N");
        AutostartEnabled = bool.Parse(Read(SettingsKeys.Autostart, "true"));
        CloseToTray = bool.Parse(Read(SettingsKeys.CloseToTray, "true"));
        MarkdownEnabled = !bool.TryParse(Read(SettingsKeys.MarkdownEnabled, "true"), out var markdownEnabled)
            || markdownEnabled;

        BackupRetentionCount = int.Parse(Read(SettingsKeys.BackupRetentionCount, "7"), CultureInfo.InvariantCulture);
        TrashRetentionDays = int.Parse(Read(SettingsKeys.TrashRetentionDays, "30"), CultureInfo.InvariantCulture);

        _loading = false;
    }

    private void LoadFormatCClients(IReadOnlyDictionary<string, string> settings)
    {
        var clients = _clients.GetAll();   // 이미 sort_order 정렬
        var selected = Memoria.Core.Reporting.FormatCClients
            .Resolve(settings.TryGetValue(SettingsKeys.FormatCClientIds, out var ids) ? ids : null, clients);

        FormatCClientOptions.Clear();
        foreach (var c in clients)
            FormatCClientOptions.Add(new FormatCClientSelection(c.Id, c.Name)
            {
                // 필터 미설정(null)이면 전 업무가 나가므로 전부 체크된 상태로 보여준다.
                IsSelected = selected is null || selected.Contains(c.Id),
            });
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

        var values = CaptureSettings();
        SaveCaptured(values, AutostartEnabled);
    }

    public async Task SaveAsync()
    {
        if (!CanSave)
            return;

        AppLog.Action("UI.SettingsSave");
        using var perf = AppLog.Measure("Settings.Save");

        // WPF 바인딩 값을 UI 스레드에서 먼저 스냅샷하고, SQLite/레지스트리 I/O만
        // 작업 스레드에서 수행해 설정 창을 닫을 때 전체 앱이 멎지 않게 한다.
        var values = CaptureSettings();
        var autostartEnabled = AutostartEnabled;
        await Task.Run(() => SaveCaptured(values, autostartEnabled));
    }

    private IReadOnlyDictionary<string, string> CaptureSettings()
    {
        return new Dictionary<string, string>
        {
            [SettingsKeys.ReporterName] = ReporterName,
            [SettingsKeys.FormatATaskHeader] = TaskHeaderA,
            [SettingsKeys.FormatAIssueHeader] = IssueHeaderA,
            [SettingsKeys.FormatBTitleWord] = TitleWordB,
            [SettingsKeys.FormatBIssueHeader] = IssueHeaderB,
            [SettingsKeys.ShowFormatD] = ShowFormatD.ToString().ToLowerInvariant(),
            [SettingsKeys.FormatCStartDay] = StartDayC.ToString(),
            [SettingsKeys.FormatCEndDay] = EndDayC.ToString(),
            [SettingsKeys.FormatCTitleHeader] = TitleHeaderC,
            [SettingsKeys.FormatCPlanHeader] = PlanHeaderC,
            [SettingsKeys.FormatCDetailMarker] = DetailMarkerC,
            [SettingsKeys.FormatCClientIds] = Memoria.Core.Reporting.FormatCClients.Format(
                FormatCClientOptions.Where(c => c.IsSelected).Select(c => c.Id)),
            [SettingsKeys.ReportIndent] = ReportIndent,
            [SettingsKeys.IncludeDoneOnly] = IncludeDoneOnly ? "true" : "false",
            [SettingsKeys.GoogleServiceAccountJsonPath] = ServiceAccountJsonPath ?? "",
            [SettingsKeys.GoogleSheetId] = SheetId ?? "",
            [SettingsKeys.GoogleSheetTabName] = string.IsNullOrWhiteSpace(SheetTabName) ? "일자 작업내역" : SheetTabName,
            [SettingsKeys.HotkeyNewNote] = HotkeyNewNote,
            [SettingsKeys.Autostart] = AutostartEnabled ? "true" : "false",
            [SettingsKeys.CloseToTray] = CloseToTray ? "true" : "false",
            [SettingsKeys.MarkdownEnabled] = MarkdownEnabled ? "true" : "false",
            [SettingsKeys.BackupRetentionCount] = BackupRetentionCount.ToString(CultureInfo.InvariantCulture),
            [SettingsKeys.TrashRetentionDays] = TrashRetentionDays.ToString(CultureInfo.InvariantCulture),
        };
    }

    private void SaveCaptured(IReadOnlyDictionary<string, string> values, bool autostartEnabled)
    {
        _settings.SetMany(values);

        if (autostartEnabled)
            _autostart.Enable();
        else
            _autostart.Disable();
    }
}

/// 설정 창의 색 계열 스와치 한 개(키/한글 라벨/대표색).
public sealed record ThemeOption(string Key, string Label, string Swatch);

/// 양식 C 기간 요일 콤보박스 항목(값/한글 라벨).
public sealed record DayOfWeekOption(DayOfWeek Value, string Label);

/// 양식 C 고객사 체크 목록의 한 줄.
public sealed partial class FormatCClientSelection : ObservableObject
{
    public int Id { get; }
    public string Name { get; }

    [ObservableProperty] private bool _isSelected;

    public FormatCClientSelection(int id, string name)
    {
        Id = id;
        Name = name;
    }
}

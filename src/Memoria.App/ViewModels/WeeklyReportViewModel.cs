using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.App.Services;
using Memoria.Core;
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;

namespace Memoria.App.ViewModels;

public partial class WeeklyReportViewModel : ObservableObject
{
    private readonly IWeeklyReportService _reportService;
    private readonly IWeekCalculator _weekCalculator;
    private readonly INoteRepository _noteRepository;
    private readonly IClientRepository _clientRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly ISettingsRepository _settings;
    private readonly IClipboardService _clipboard;
    private readonly IConfirmationDialogService _dialogs;
    private readonly TimeProvider _timeProvider;

    private const string WeeklyReportGroupName = "주간보고";

    private int? _currentNoteId;

    [ObservableProperty]
    private DateOnly _selectedDate;

    [ObservableProperty]
    private ReportFormatKind _selectedFormat = ReportFormatKind.A;

    [ObservableProperty]
    private DateOnly _weekStart;

    [ObservableProperty]
    private DateOnly _weekEnd;

    [ObservableProperty]
    private string _weekRangeLabel = "";

    [ObservableProperty]
    private string _reportText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnclassifiedWarning))]
    private int _unclassifiedTaskCount;

    public bool HasUnclassifiedWarning => UnclassifiedTaskCount > 0;

    public WeeklyReportViewModel(
        IWeeklyReportService reportService,
        IWeekCalculator weekCalculator,
        INoteRepository noteRepository,
        IClientRepository clientRepository,
        IGroupRepository groupRepository,
        ISettingsRepository settings,
        IClipboardService clipboard,
        IConfirmationDialogService dialogs,
        TimeProvider timeProvider)
    {
        _reportService = reportService;
        _weekCalculator = weekCalculator;
        _noteRepository = noteRepository;
        _clientRepository = clientRepository;
        _groupRepository = groupRepository;
        _settings = settings;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _timeProvider = timeProvider;

        // 기본 = 오늘이 포함된 주. 부작용(리포지토리 호출)을 피하려고 backing field에 직접 설정.
        _selectedDate = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        RecomputeWeek();
    }

    partial void OnSelectedDateChanged(DateOnly value) => RecomputeWeek();

    private void RecomputeWeek()
    {
        var (monday, friday) = _weekCalculator.GetWorkWeek(SelectedDate);
        WeekStart = monday;
        WeekEnd = friday;
        WeekRangeLabel = $"{monday.ToString("MM/dd", System.Globalization.CultureInfo.InvariantCulture)} ~ {friday.ToString("MM/dd", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private ReportRenderOptions BuildOptions(DateOnly monday, DateOnly friday)
    {
        var includeDoneOnly =
            bool.TryParse(_settings.GetOrDefault(SettingsKeys.IncludeDoneOnly, "false"), out var b) && b;

        return new ReportRenderOptions
        {
            ReporterName = _settings.GetOrDefault(SettingsKeys.ReporterName, "이승현"),
            WeekStart = monday,
            WeekEnd = friday,
            TaskHeaderA = _settings.GetOrDefault(SettingsKeys.FormatATaskHeader, "[업무 내용]"),
            IssueHeaderA = _settings.GetOrDefault(SettingsKeys.FormatAIssueHeader, "[이슈]"),
            TitleWordB = _settings.GetOrDefault(SettingsKeys.FormatBTitleWord, "주간 보고"),
            IssueHeaderB = _settings.GetOrDefault(SettingsKeys.FormatBIssueHeader, "* 이슈사항:"),
            Indent = _settings.GetOrDefault(SettingsKeys.ReportIndent, "\t"),
            IncludeDoneOnly = includeDoneOnly,
            Clients = _clientRepository.GetAll(enabledOnly: true),
            UnclassifiedLabel = "미분류",
        };
    }

    private string RenderFresh(DateOnly monday, DateOnly friday)
    {
        var options = BuildOptions(monday, friday);
        var build = _reportService.Build(SelectedDate, options);
        UnclassifiedTaskCount = build.UnclassifiedTaskCount;
        return _reportService.Render(SelectedFormat, build.Data, options);
    }

    [RelayCommand]
    private void Generate()
    {
        var (monday, friday) = _weekCalculator.GetWorkWeek(SelectedDate);
        ReportText = RenderFresh(monday, friday);
    }
}

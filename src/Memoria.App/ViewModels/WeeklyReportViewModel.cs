using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.App.Services;
using Memoria.Core;
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Memoria.Core.Sheets;
using System.Threading.Tasks;

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
    private readonly ISpreadsheetReader _sheetReader;

    private const string WeeklyReportGroupName = "주간보고";

    private int? _currentNoteId;

    /// 현재 표시 중인 주간보고 노트 id(생성/로드 후). 사이드바·목록 동기화에 사용.
    public int? CurrentNoteId => _currentNoteId;

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
        TimeProvider timeProvider,
        ISpreadsheetReader sheetReader)
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
        _sheetReader = sheetReader;

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
        // #4 항상 현재 체크리스트(일일 업무일지)에서 새로 렌더 → 체크리스트 수정이 열 때마다 즉시 반영된다.
        //    (이전엔 저장된 옛 본문을 재사용해 '다시 생성'을 눌러야만 최신화됐다.)
        var monday = WeekStart;   // RecomputeWeek가 SelectedDate에 맞춰 이미 채움
        var friday = WeekEnd;
        var existing = _noteRepository.FindWeeklyReport(monday, SelectedFormat);
        var text = RenderFresh(monday, friday);
        ReportText = text;
        Persist(monday, existing, text);
    }

    [RelayCommand]
    private void Regenerate()
    {
        var monday = WeekStart;   // RecomputeWeek가 SelectedDate에 맞춰 이미 채움
        var friday = WeekEnd;
        var existing = _noteRepository.FindWeeklyReport(monday, SelectedFormat);
        if (existing is not null && !string.IsNullOrEmpty(existing.Body))
        {
            if (!_dialogs.Confirm("기존에 편집한 주간보고 내용을 덮어씁니다. 계속할까요?"))
                return;
        }

        var text = RenderFresh(monday, friday);
        ReportText = text;
        Persist(monday, existing, text);
    }

    private void Persist(DateOnly monday, Note? existing, string text)
    {
        if (existing is null)
        {
            var note = new Note
            {
                Type = NoteType.WeeklyReport,
                GroupId = ResolveWeeklyReportGroupId(),
                ReportFormat = SelectedFormat,
                ReportWeekStart = monday,
                Body = text,
            };
            _currentNoteId = _noteRepository.Create(note);
        }
        else
        {
            existing.Body = text;
            _noteRepository.Update(existing);
            _currentNoteId = existing.Id;
        }
    }

    private int? ResolveWeeklyReportGroupId()
    {
        var group = _groupRepository.GetAll()
            .FirstOrDefault(g => g.IsSystem && g.Name == WeeklyReportGroupName);
        return group?.Id;
    }

    // #5 양식 전환 시: 해당 양식으로 저장된 보고서가 있으면 재사용, 없으면 즉시 새로 렌더한다.
    //    (이전엔 LoadExisting이 없는 경우 ReportText를 비워 화면이 빈 채로 남는 버그가 있었다.)
    partial void OnSelectedFormatChanged(ReportFormatKind value) => Generate();

    [RelayCommand]
    private async Task GenerateFromSheet()
    {
        var monday = WeekStart;
        var friday = WeekEnd;
        var sheetId = _settings.GetOrDefault(SettingsKeys.GoogleSheetId, "");
        var tabName = _settings.GetOrDefault(SettingsKeys.GoogleSheetTabName, "일자 작업내역");
        if (string.IsNullOrWhiteSpace(sheetId))
        {
            _dialogs.Confirm("구글 시트 ID가 설정되지 않았습니다. 설정 > 구글 연동에서 입력하세요.");
            return;
        }
        try
        {
            var grid = await _sheetReader.ReadRowsAsync(sheetId, tabName);
            var parsed = SheetWorkParser.Parse(grid, monday, friday);
            if (parsed.Tasks.Count == 0 && parsed.Issues.Count == 0)
            {
                _dialogs.Confirm("이번 주에 해당하는 시트 행이 없습니다. 주(날짜)와 탭 이름을 확인하세요.");
                return;
            }
            var options = BuildOptions(monday, friday);
            var build = _reportService.BuildFromTexts(parsed.Tasks, parsed.Issues, monday, friday, options);
            UnclassifiedTaskCount = build.UnclassifiedTaskCount;
            var text = _reportService.Render(SelectedFormat, build.Data, options);
            ReportText = text;
            var existing = _noteRepository.FindWeeklyReport(monday, SelectedFormat);
            Persist(monday, existing, text);
        }
        catch (System.Exception ex)
        {
            _dialogs.Confirm($"구글 시트에서 가져오지 못했습니다: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Copy() => _clipboard.SetText(ReportText ?? "");
}

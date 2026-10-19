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
using System.Windows;

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
    private int _generation;
    private sealed record FormatASnapshot(DateOnly Start, DateOnly End, string Text, int Unclassified);
    private FormatASnapshot? _formatASnapshot;

    partial void OnReportTextChanged(string value) => RememberFormatA(value);

    private void RememberFormatA(string text)
    {
        if (SelectedFormat == ReportFormatKind.A)
            _formatASnapshot = new(WeekStart, WeekEnd, text, UnclassifiedTaskCount);
    }

    private void SetRenderedText(string text)
    {
        ReportText = text;
        // 주차만 바뀌고 생성 본문이 같아도 원본 기간·미분류 수는 갱신한다.
        RememberFormatA(text);
    }

    /// 저장 앵커. 양식 C는 표시 구간이 금~목이라 시작일을 그대로 저장하면 노트를 다시 열 때
    /// 구간이 한 주 밀린다. 그래서 A/B/C 모두 '선택 날짜가 속한 주의 월요일'을 키로 쓴다.
    private DateOnly _anchorMonday;

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
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnclassifiedWarning))]
    private int _unclassifiedTaskCount;

    public bool ShowFormatD => bool.TryParse(_settings.Get(SettingsKeys.ShowFormatD), out var show) && show;

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

    public void RefreshSettings()
    {
        OnPropertyChanged(nameof(ShowFormatD));
        RecomputeWeek();
    }

    private void RecomputeWeek()
    {
        var (monday, friday) = _weekCalculator.GetWorkWeek(SelectedDate);
        _anchorMonday = monday;

        // A/C는 금~목 고정, D는 기존 C의 사용자 지정 기간을 보존한다.
        var (start, end) = (monday, friday);
        if (SelectedFormat is ReportFormatKind.A or ReportFormatKind.C)
        {
            (start, end) = _weekCalculator.GetCustomRange(SelectedDate, DayOfWeek.Friday, DayOfWeek.Thursday);
        }
        else if (SelectedFormat == ReportFormatKind.D)
        {
            var settings = _settings.GetAll();
            var startDay = ReportDayOfWeek.Parse(
                Read(settings, SettingsKeys.FormatCStartDay, nameof(DayOfWeek.Friday)), DayOfWeek.Friday);
            var endDay = ReportDayOfWeek.Parse(
                Read(settings, SettingsKeys.FormatCEndDay, nameof(DayOfWeek.Thursday)), DayOfWeek.Thursday);
            (start, end) = _weekCalculator.GetCustomRange(SelectedDate, startDay, endDay);
        }

        WeekStart = start;
        WeekEnd = end;
        WeekRangeLabel = $"{start.ToString("MM/dd", System.Globalization.CultureInfo.InvariantCulture)} ~ {end.ToString("MM/dd", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private ReportRenderOptions BuildOptions(DateOnly monday, DateOnly friday)
    {
        var settings = _settings.GetAll();
        var includeDoneOnly =
            bool.TryParse(Read(settings, SettingsKeys.IncludeDoneOnly, "false"), out var b) && b;

        var clients = _clientRepository.GetAll(enabledOnly: true);

        return new ReportRenderOptions
        {
            ReporterName = Read(settings, SettingsKeys.ReporterName, "이승현"),
            WeekStart = monday,
            WeekEnd = friday,
            TaskHeaderA = Read(settings, SettingsKeys.FormatATaskHeader, "[업무 내용]"),
            IssueHeaderA = Read(settings, SettingsKeys.FormatAIssueHeader, "[이슈]"),
            TitleWordB = Read(settings, SettingsKeys.FormatBTitleWord, "주간 보고"),
            IssueHeaderB = Read(settings, SettingsKeys.FormatBIssueHeader, "* 이슈사항:"),
            TitleHeaderC = Read(settings, SettingsKeys.FormatCTitleHeader, "[ 주간 실적 ]"),
            PlanHeaderC = Read(settings, SettingsKeys.FormatCPlanHeader, "[ 차주 계획 ]"),
            DetailMarkerC = Read(settings, SettingsKeys.FormatCDetailMarker, "o"),
            ClientIdsC = FormatCClients.Resolve(
                settings.TryGetValue(SettingsKeys.FormatCClientIds, out var clientIds) ? clientIds : null,
                clients),
            Indent = Read(settings, SettingsKeys.ReportIndent, "\t"),
            IncludeDoneOnly = includeDoneOnly,
            Clients = clients,
            UnclassifiedLabel = "미분류",
        };
    }

    private static string Read(IReadOnlyDictionary<string, string> settings, string key, string fallback)
        => settings.TryGetValue(key, out var value) ? value : fallback;

    private sealed record RenderResult(string Text, int UnclassifiedTaskCount);

    private RenderResult RenderFresh(DateOnly start, DateOnly end, ReportFormatKind format)
    {
        var options = BuildOptions(start, end);
        // A→C 전환 시 시트에서 생성하거나 직접 편집한 현재 A 본문을 그대로 사용한다.
        // 체크리스트를 다시 조회하면 A와 C의 원본이 달라질 수 있다.
        if (format == ReportFormatKind.C && _formatASnapshot is { } source
            && source.Start == start && source.End == end)
            return new RenderResult(FormatAReport.WithoutClientHeaders(source.Text, options), source.Unclassified);
        var build = _reportService.BuildRange(start, end, options);
        return new RenderResult(
            _reportService.Render(format, build.Data, options),
            build.UnclassifiedTaskCount);
    }

    [RelayCommand]
    private void Generate()
    {
        // #4 항상 현재 체크리스트(일일 업무일지)에서 새로 렌더 → 체크리스트 수정이 열 때마다 즉시 반영된다.
        //    (이전엔 저장된 옛 본문을 재사용해 '다시 생성'을 눌러야만 최신화됐다.)
        var format = SelectedFormat;
        var existing = _noteRepository.FindWeeklyReport(_anchorMonday, format);
        var rendered = RenderFresh(WeekStart, WeekEnd, format);   // RecomputeWeek가 SelectedDate·양식에 맞춰 이미 채움
        UnclassifiedTaskCount = rendered.UnclassifiedTaskCount;
        SetRenderedText(rendered.Text);
        _currentNoteId = Persist(_anchorMonday, format, existing, rendered.Text);
    }

    [RelayCommand]
    private async Task GenerateInBackgroundAsync()
    {
        AppLog.Action("UI.WeeklyReportGenerate", $"format={SelectedFormat} date={SelectedDate:yyyy-MM-dd}");
        using var perf = AppLog.Measure("WeeklyReport.Generate");
        var generation = ++_generation;
        var monday = _anchorMonday;
        var start = WeekStart;
        var end = WeekEnd;
        var format = SelectedFormat;
        IsBusy = true;
        try
        {
            var result = await Task.Run(() =>
            {
                var existing = _noteRepository.FindWeeklyReport(monday, format);
                var rendered = RenderFresh(start, end, format);
                var noteId = Persist(monday, format, existing, rendered.Text);
                return (rendered, noteId);
            });
            if (generation != _generation) return;
            UnclassifiedTaskCount = result.rendered.UnclassifiedTaskCount;
            SetRenderedText(result.rendered.Text);
            _currentNoteId = result.noteId;
        }
        finally
        {
            if (generation == _generation) IsBusy = false;
        }
    }

    [RelayCommand]
    private void Regenerate()
    {
        var format = SelectedFormat;
        var existing = _noteRepository.FindWeeklyReport(_anchorMonday, format);
        if (existing is not null && !string.IsNullOrEmpty(existing.Body))
        {
            if (!_dialogs.Confirm("기존에 편집한 주간보고 내용을 덮어씁니다. 계속할까요?"))
                return;
        }

        var rendered = RenderFresh(WeekStart, WeekEnd, format);
        UnclassifiedTaskCount = rendered.UnclassifiedTaskCount;
        SetRenderedText(rendered.Text);
        _currentNoteId = Persist(_anchorMonday, format, existing, rendered.Text);
    }

    [RelayCommand]
    private async Task RegenerateInBackgroundAsync()
    {
        AppLog.Action("UI.WeeklyReportRegenerate", $"format={SelectedFormat} date={SelectedDate:yyyy-MM-dd}");
        using var perf = AppLog.Measure("WeeklyReport.Regenerate");
        var monday = _anchorMonday;
        var start = WeekStart;
        var end = WeekEnd;
        var format = SelectedFormat;
        var existing = await Task.Run(() => _noteRepository.FindWeeklyReport(monday, format));
        if (existing is not null && !string.IsNullOrEmpty(existing.Body)
            && !_dialogs.Confirm("기존에 편집한 주간보고 내용을 덮어씁니다. 계속할까요?"))
            return;

        var generation = ++_generation;
        IsBusy = true;
        try
        {
            var result = await Task.Run(() =>
            {
                var rendered = RenderFresh(start, end, format);
                var noteId = Persist(monday, format, existing, rendered.Text);
                return (rendered, noteId);
            });
            if (generation != _generation) return;
            UnclassifiedTaskCount = result.rendered.UnclassifiedTaskCount;
            SetRenderedText(result.rendered.Text);
            _currentNoteId = result.noteId;
        }
        finally
        {
            if (generation == _generation) IsBusy = false;
        }
    }

    private int Persist(DateOnly monday, ReportFormatKind format, Note? existing, string text)
    {
        if (existing is null)
        {
            var note = new Note
            {
                Type = NoteType.WeeklyReport,
                GroupId = ResolveWeeklyReportGroupId(),
                ReportFormat = format,
                ReportWeekStart = monday,
                Body = text,
            };
            return _noteRepository.Create(note);
        }

        existing.Body = text;
        _noteRepository.Update(existing);
        return existing.Id;
    }

    private int? ResolveWeeklyReportGroupId()
    {
        var group = _groupRepository.GetAll()
            .FirstOrDefault(g => g.IsSystem && g.Name == WeeklyReportGroupName);
        return group?.Id;
    }

    // #5 양식 전환 시: 해당 양식으로 저장된 보고서가 있으면 재사용, 없으면 즉시 새로 렌더한다.
    //    (이전엔 LoadExisting이 없는 경우 ReportText를 비워 화면이 빈 채로 남는 버그가 있었다.)
    //    양식마다 집계 구간이 다를 수 있으므로 렌더 전에 구간을 다시 계산한다.
    partial void OnSelectedFormatChanged(ReportFormatKind value)
    {
        RecomputeWeek();
        if (Application.Current?.Dispatcher is { } dispatcher && dispatcher.CheckAccess())
            GenerateInBackgroundCommand.Execute(null);
        else
            Generate();
    }

    [RelayCommand]
    private async Task GenerateFromSheet()
    {
        var monday = WeekStart;   // 양식 C면 금~목 구간
        var friday = WeekEnd;
        var settings = _settings.GetAll();
        var sheetId = Read(settings, SettingsKeys.GoogleSheetId, "");
        var tabName = Read(settings, SettingsKeys.GoogleSheetTabName, "일자 작업내역");
        if (string.IsNullOrWhiteSpace(sheetId))
        {
            _dialogs.Confirm("구글 시트 ID가 설정되지 않았습니다. 설정 > 구글 연동에서 입력하세요.");
            return;
        }
        IsBusy = true;
        try
        {
            var grid = await _sheetReader.ReadRowsAsync(sheetId, tabName);
            var anchorMonday = _anchorMonday;
            var format = SelectedFormat;
            var result = await Task.Run(() =>
            {
                var parsed = SheetWorkParser.Parse(grid, monday, friday);
                if (parsed.Tasks.Count == 0 && parsed.Issues.Count == 0)
                    return (Found: false, Text: "", Unclassified: 0, NoteId: (int?)null);

                var options = BuildOptions(monday, friday);
                var build = _reportService.BuildFromTexts(parsed.Tasks, parsed.Issues, monday, friday, options);
                // 시트의 완료 처리된 업무는 실제 업무일을 사용한다. 중복 업무는 마지막 업무일을 보존한다.
                var datedData = build.Data with
                {
                    Tasks = build.Data.Tasks.Select(task => parsed.TaskDates.TryGetValue(task.Text, out var date)
                        ? task with { CompletedDate = date } : task).ToList(),
                };
                var text = _reportService.Render(format, datedData, options);
                var existing = _noteRepository.FindWeeklyReport(anchorMonday, format);
                var noteId = Persist(anchorMonday, format, existing, text);
                return (Found: true, Text: text, Unclassified: build.UnclassifiedTaskCount, NoteId: (int?)noteId);
            });
            if (!result.Found)
            {
                _dialogs.Confirm("이번 주에 해당하는 시트 행이 없습니다. 주(날짜)와 탭 이름을 확인하세요.");
                return;
            }

            UnclassifiedTaskCount = result.Unclassified;
            SetRenderedText(result.Text);
            _currentNoteId = result.NoteId;
        }
        catch (System.Exception ex)
        {
            _dialogs.Confirm($"구글 시트에서 가져오지 못했습니다: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Copy() => _clipboard.SetText(ReportText ?? "");
}

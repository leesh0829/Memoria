// tests/Memoria.Tests/ViewModels/WeeklyReportFakes.cs
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Memoria.App.Services;
// FakeGroupRepository, FakeNoteRepository, FakeSettingsRepository are the canonical
// Memoria.Tests.Fakes versions (FakeGroupRepository.cs / FakeNoteRepository.cs / FakeSettingsRepository.cs).

namespace Memoria.Tests.ViewModels;

internal sealed class WeeklyReportFixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public WeeklyReportFixedTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class FakeWeekCalculator : IWeekCalculator
{
    public (DateOnly Monday, DateOnly Friday) GetWorkWeek(DateOnly anyDate)
    {
        int diff = ((int)anyDate.DayOfWeek + 6) % 7; // Monday = 0
        var monday = anyDate.AddDays(-diff);
        return (monday, monday.AddDays(4));
    }
}

internal sealed class FakeWeeklyReportService : IWeeklyReportService
{
    public DateOnly LastAnyDate { get; private set; }
    public ReportRenderOptions? LastOptions { get; private set; }
    public ReportFormatKind LastRenderFormat { get; private set; }
    public int BuildCallCount { get; private set; }
    public Func<DateOnly, ReportRenderOptions, WeeklyReportBuildResult> BuildImpl { get; set; }
        = (d, o) => new WeeklyReportBuildResult(
            new WeeklyReportData(new List<ReportTask>(), new List<ReportIssue>()), 0, o.WeekStart, o.WeekEnd);
    public string RenderResult { get; set; } = "RENDERED-TEXT";

    public WeeklyReportBuildResult Build(DateOnly anyDateInWeek, ReportRenderOptions options)
    {
        BuildCallCount++;
        LastAnyDate = anyDateInWeek;
        LastOptions = options;
        return BuildImpl(anyDateInWeek, options);
    }

    public string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options)
    {
        LastRenderFormat = format;
        return RenderResult;
    }

    public IReadOnlyList<string>? LastTaskTexts { get; private set; }
    public IReadOnlyList<string>? LastIssueTexts { get; private set; }
    public int BuildFromTextsCallCount { get; private set; }

    public WeeklyReportBuildResult BuildFromTexts(
        IReadOnlyList<string> taskTexts, IReadOnlyList<string> issueTexts,
        DateOnly monday, DateOnly friday, ReportRenderOptions options)
    {
        BuildFromTextsCallCount++;
        LastTaskTexts = taskTexts;
        LastIssueTexts = issueTexts;
        LastOptions = options;
        return new WeeklyReportBuildResult(
            new WeeklyReportData(
                taskTexts.Select(t => new ReportTask(t, null, true)).ToList(),
                issueTexts.Select(t => new ReportIssue(t)).ToList()),
            0, monday, friday);
    }
}

internal sealed class FakeClientRepository : IClientRepository
{
    public List<Client> Clients { get; } = new();
    public bool LastEnabledOnly { get; private set; }

    public IReadOnlyList<Client> GetAll(bool enabledOnly = false)
    {
        LastEnabledOnly = enabledOnly;
        var src = enabledOnly ? Clients.Where(c => c.Enabled) : Clients;
        return src.OrderBy(c => c.SortOrder).ToList();
    }

    public int Create(Client client) => throw new NotSupportedException();
    public void Update(Client client) => throw new NotSupportedException();
    public void Delete(int id) => throw new NotSupportedException();
    public IReadOnlyList<ClientRule> GetRules() => throw new NotSupportedException();
    public void ReplaceRules(int clientId, IEnumerable<ClientRule> rules) => throw new NotSupportedException();
}

internal sealed class FakeClipboardService : IClipboardService
{
    public string? LastText { get; private set; }
    public int SetCount { get; private set; }
    public void SetText(string text) { LastText = text; SetCount++; }
}

internal sealed class FakeConfirmationDialogService : IConfirmationDialogService
{
    public bool Result { get; set; } = true;
    public int CallCount { get; private set; }
    public string? LastMessage { get; private set; }
    public bool Confirm(string message) { CallCount++; LastMessage = message; return Result; }
}

internal sealed class FakeSpreadsheetReader : Memoria.Core.Sheets.ISpreadsheetReader
{
    public IReadOnlyList<IReadOnlyList<string>> Grid { get; set; } =
        new List<IReadOnlyList<string>>();
    public string? LastSheetId { get; private set; }
    public string? LastTabName { get; private set; }
    public int CallCount { get; private set; }
    public System.Exception? Throw { get; set; }

    public System.Threading.Tasks.Task<IReadOnlyList<IReadOnlyList<string>>> ReadRowsAsync(
        string sheetId, string tabName, System.Threading.CancellationToken ct = default)
    {
        CallCount++;
        LastSheetId = sheetId;
        LastTabName = tabName;
        if (Throw is not null) throw Throw;
        return System.Threading.Tasks.Task.FromResult(Grid);
    }
}

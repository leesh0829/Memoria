using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Memoria.Core.Sheets;

namespace Memoria.Tests.App.Fakes;

// ChecklistViewModel 생성/Load 용 최소 페이크.
internal sealed class FakeChecklistRepo : IChecklistRepository
{
    public List<ChecklistItem> Items { get; } = new();
    public int AddItem(ChecklistItem item) { item.Id = Items.Count + 1; Items.Add(item); return item.Id; }
    public void UpdateItem(ChecklistItem item) { }
    public void DeleteItem(int id) => Items.RemoveAll(i => i.Id == id);
    public IReadOnlyList<ChecklistItem> GetByNote(int noteId) =>
        Items.FindAll(i => i.NoteId == noteId);
}

internal sealed class FakeClientRepo : IClientRepository
{
    public int Create(Client client) => throw new NotSupportedException();
    public void Update(Client client) => throw new NotSupportedException();
    public void Delete(int id) => throw new NotSupportedException();
    public IReadOnlyList<Client> GetAll(bool enabledOnly = false) => new List<Client>();
    public IReadOnlyList<ClientRule> GetRules() => new List<ClientRule>();
    public void ReplaceRules(int clientId, IEnumerable<ClientRule> rules) => throw new NotSupportedException();
}

internal sealed class FakeTagging : ITaggingService
{
    public ChecklistItem ApplyAutoTag(ChecklistItem item) => item;
}

internal sealed class FakeWeekCalc : IWeekCalculator
{
    public (DateOnly Monday, DateOnly Friday) GetWorkWeek(DateOnly anyDate)
    {
        int delta = ((int)anyDate.DayOfWeek + 6) % 7; // Monday=0
        var monday = anyDate.AddDays(-delta);
        return (monday, monday.AddDays(4));
    }
}

internal sealed class FakeWeeklyReportService : IWeeklyReportService
{
    public WeeklyReportBuildResult Build(DateOnly anyDateInWeek, ReportRenderOptions options) =>
        new(new WeeklyReportData(new List<ReportTask>(), new List<ReportIssue>()), 0,
            options.WeekStart, options.WeekEnd);
    public string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options) => "";
    public WeeklyReportBuildResult BuildFromTexts(
        IReadOnlyList<string> taskTexts, IReadOnlyList<string> issueTexts,
        DateOnly monday, DateOnly friday, ReportRenderOptions options) =>
        new(new WeeklyReportData(
                taskTexts.Select(t => new ReportTask(t, null, true)).ToList(),
                issueTexts.Select(t => new ReportIssue(t)).ToList()),
            0, monday, friday);
}

internal sealed class FakeClipboard : Memoria.App.Services.IClipboardService
{
    public void SetText(string text) { }
}

internal sealed class FakeConfirm : Memoria.App.Services.IConfirmationDialogService
{
    public bool Confirm(string message) => true;
}

internal sealed class FakeSettings : ISettingsRepository
{
    public string? Get(string key) => null;
    public string GetOrDefault(string key, string fallback) => fallback;
    public void Set(string key, string value) { }
    public IReadOnlyDictionary<string, string> GetAll() => new Dictionary<string, string>();
}

internal sealed class FakeSearchService : ISearchService
{
    public List<SearchHit> Result { get; } = new();
    public string? LastQuery { get; private set; }
    public IReadOnlyList<SearchHit> Search(string query) { LastQuery = query; return Result; }
}

internal sealed class FakeSheetReader : ISpreadsheetReader
{
    public Task<IReadOnlyList<IReadOnlyList<string>>> ReadRowsAsync(
        string sheetId, string tabName, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IReadOnlyList<string>>>(new List<IReadOnlyList<string>>());
}

// M9 MainViewModel 의 신규 required 파라미터(ISearchService + 하위 에디터 VM 팩토리)를
// 기본 페이크로 채워주는 공용 헬퍼. M9 이전 테스트 헬퍼의 후방 호환성을 위해 사용한다.
internal static class M9EditorFakes
{
    public static Func<Memoria.App.ViewModels.ChecklistViewModel> ChecklistFactory(
        INoteRepository notes, IGroupRepository groups) =>
        () => new Memoria.App.ViewModels.ChecklistViewModel(
            new FakeChecklistRepo(), new FakeClientRepo(), new FakeTagging(), notes, groups, new FakeClipboard());

    public static Func<Memoria.App.ViewModels.WeeklyReportViewModel> WeeklyFactory(
        INoteRepository notes, IGroupRepository groups, TimeProvider time) =>
        () => new Memoria.App.ViewModels.WeeklyReportViewModel(
            new FakeWeeklyReportService(), new FakeWeekCalc(), notes, new FakeClientRepo(),
            groups, new FakeSettings(), new FakeClipboard(), new FakeConfirm(), time,
            new FakeSheetReader());
}

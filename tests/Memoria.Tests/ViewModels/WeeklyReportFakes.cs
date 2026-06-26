// tests/Memoria.Tests/ViewModels/WeeklyReportFakes.cs
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Memoria.App.Services;

namespace Memoria.Tests.ViewModels;

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTimeProvider(DateTimeOffset now) => _now = now;
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
}

internal sealed class FakeNoteRepository : INoteRepository
{
    public List<Note> Notes { get; } = new();
    public List<Note> Created { get; } = new();
    public List<Note> Updated { get; } = new();
    private int _nextId = 100;

    public int Create(Note note)
    {
        note.Id = _nextId++;
        Notes.Add(note);
        Created.Add(note);
        return note.Id;
    }

    public void Update(Note note)
    {
        Updated.Add(note);
        var idx = Notes.FindIndex(n => n.Id == note.Id);
        if (idx >= 0) Notes[idx] = note;
    }

    public Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format)
        => Notes.FirstOrDefault(n => n.Type == NoteType.WeeklyReport
            && n.ReportWeekStart == weekStart && n.ReportFormat == format);

    public Note? Get(int id) => Notes.FirstOrDefault(n => n.Id == id);

    public void SoftDelete(int id) => throw new NotSupportedException();
    public void Restore(int id) => throw new NotSupportedException();
    public void Purge(int id) => throw new NotSupportedException();
    public void PurgeExpiredTrash(int retentionDays) => throw new NotSupportedException();
    public IReadOnlyList<Note> GetByGroup(int? groupId) => throw new NotSupportedException();
    public IReadOnlyList<Note> GetTrash() => throw new NotSupportedException();
    public IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday) => throw new NotSupportedException();
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

internal sealed class FakeGroupRepository : IGroupRepository
{
    public List<Group> Groups { get; } = new();

    public IReadOnlyList<Group> GetAll() => Groups.OrderBy(g => g.SortOrder).ToList();

    public int Create(Group group) => throw new NotSupportedException();
    public void Update(Group group) => throw new NotSupportedException();
    public void Delete(int id) => throw new NotSupportedException();
    public Group? Get(int id) => Groups.FirstOrDefault(g => g.Id == id);
}

internal sealed class FakeSettingsRepository : ISettingsRepository
{
    public Dictionary<string, string> Values { get; } = new();
    public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
    public string GetOrDefault(string key, string fallback) => Values.TryGetValue(key, out var v) ? v : fallback;
    public void Set(string key, string value) => Values[key] = value;
    public IReadOnlyDictionary<string, string> GetAll() => Values;
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

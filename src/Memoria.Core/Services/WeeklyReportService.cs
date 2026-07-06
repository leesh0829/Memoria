using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.Core.Services;

public sealed class WeeklyReportService : IWeeklyReportService
{
    private readonly IWeekCalculator _week;
    private readonly INoteRepository _notes;
    private readonly IChecklistRepository _checklist;
    private readonly IClientClassifier _classifier;
    private readonly IClientRepository _clients;
    private readonly IWeeklyReportRenderer _renderer;

    public WeeklyReportService(
        IWeekCalculator week,
        INoteRepository notes,
        IChecklistRepository checklist,
        IClientClassifier classifier,
        IClientRepository clients,
        IWeeklyReportRenderer renderer)
    {
        _week = week;
        _notes = notes;
        _checklist = checklist;
        _classifier = classifier;
        _clients = clients;
        _renderer = renderer;
    }

    public WeeklyReportBuildResult Build(DateOnly anyDateInWeek, ReportRenderOptions options)
    {
        var (monday, friday) = _week.GetWorkWeek(anyDateInWeek);
        var rules = _clients.GetRules();
        var enabledIds = _clients.GetAll(enabledOnly: true).Select(c => c.Id).ToHashSet();

        var tasks = new List<ReportTask>();
        var issues = new List<ReportIssue>();

        foreach (var note in _notes.GetChecklistsInWeek(monday, friday))
        {
            foreach (var item in _checklist.GetByNote(note.Id))
            {
                if (item.Kind == ItemKind.Task)
                {
                    int? clientId = item.IsManual
                        ? item.ClientId
                        : _classifier.Classify(item.Text, rules, enabledIds);
                    tasks.Add(new ReportTask(item.Text, clientId, item.Done));
                }
                else
                {
                    issues.Add(new ReportIssue(item.Text));
                }
            }
        }

        var relevant = options.IncludeDoneOnly ? tasks.Where(t => t.Done) : tasks;
        int unclassified = relevant.Count(t => t.ClientId is null);

        var data = new WeeklyReportData(tasks, issues);
        return new WeeklyReportBuildResult(data, unclassified, monday, friday);
    }

    public string Render(ReportFormatKind format, WeeklyReportData data, ReportRenderOptions options)
        => _renderer.Render(format, data, options);

    public WeeklyReportBuildResult BuildFromTexts(
        IReadOnlyList<string> taskTexts, IReadOnlyList<string> issueTexts,
        DateOnly monday, DateOnly friday, ReportRenderOptions options)
    {
        var rules = _clients.GetRules();
        var enabledIds = _clients.GetAll(enabledOnly: true).Select(c => c.Id).ToHashSet();

        var tasks = taskTexts
            .Select(t => new ReportTask(t, _classifier.Classify(t, rules, enabledIds), Done: true))
            .ToList();
        var issues = issueTexts.Select(t => new ReportIssue(t)).ToList();

        var relevant = options.IncludeDoneOnly ? tasks.Where(t => t.Done) : tasks;
        int unclassified = relevant.Count(t => t.ClientId is null);

        var data = new WeeklyReportData(tasks, issues);
        return new WeeklyReportBuildResult(data, unclassified, monday, friday);
    }
}

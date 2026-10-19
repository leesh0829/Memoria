using System.Text.RegularExpressions;

namespace Memoria.Core.Reporting;

public static class ReportTaskDeduplicator
{
    public static IReadOnlyList<ReportTask> Merge(IEnumerable<ReportTask> tasks) => tasks
        .GroupBy(t => (t.ClientId, Text: Regex.Replace(t.Text.Trim(), @"\s+", " ")))
        .Select(g => g.First() with
        {
            Text = g.First().Text.Trim(),
            Done = g.Any(t => t.Done),
            CompletedDate = g.Max(t => t.CompletedDate),
        }).ToList();
}

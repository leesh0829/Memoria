using Memoria.Core.Models;

namespace Memoria.Core.Reporting;

public static class WorkEntryClipboard
{
    // Excel TSV의 셀 내부 줄바꿈은 큰따옴표로 감싸 행/열 경계를 보존한다.
    private static string Cell(string value) => value.IndexOfAny(['\t', '\r', '\n', '"']) >= 0
        ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    private static string Date(DateTime? date) => date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    public static string Render(IEnumerable<WorkEntry> entries) => string.Join("\r\n", entries
        .OrderBy(e => e.StatusOrder).Select(e => string.Join("\t", new[]
        {
            e.Done ? "완료" : "", string.Join(", ", e.Statuses), Date(e.RequestedDate),
            e.ProcessingStart is null && e.ProcessingEnd is null ? "" : $"{Date(e.ProcessingStart)} ~ {Date(e.ProcessingEnd)}",
            string.Join(", ", e.RequestTypes), string.Join(", ", e.Clients),
            string.Join(", ", e.WorkTypes), string.Join(", ", e.RequestMethods),
            e.Request, e.Cause, e.Action, e.Requester, e.Assignee,
        }.Select(Cell))));
}

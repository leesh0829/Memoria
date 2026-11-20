using System.Globalization;
using System.Text.Json;
using Dapper;
using Memoria.Core.Models;

namespace Memoria.Core.Data;

/// <summary>
/// 업무내역을 열 때 일일업무일지의 후임 작업을 단방향으로 반영한다.
/// 원인/조치/담당자 등 업무내역에서 보충한 정보는 유지한다.
/// 원본과 생성 시각을 함께 사용해 SQLite 항목 ID 재사용으로 인한 잘못된 연결을 방지한다.
/// </summary>
internal static class HandoffWorkEntrySynchronizer
{
    private const string Prefix = "handoff:";

    public static IReadOnlyList<WorkEntry> ReadAll(SqliteConnectionFactory factory)
    {
        using (DatabaseDiagnostics.EnterWrite(factory.WriteSync, "WorkEntry.SyncHandoffs"))
        {
            var connection = factory.Write;
            using var transaction = connection.BeginTransaction();
            var entries = connection.Query<string>("SELECT payload FROM work_entries ORDER BY rowid;", transaction: transaction)
                .Select(json => JsonSerializer.Deserialize<WorkEntry>(json)
                    ?? throw new InvalidDataException("업무 내역 데이터가 비어 있습니다.")).ToList();
            var byId = entries.ToDictionary(entry => entry.Id);
            var sources = connection.Query<HandoffSource>("""
                SELECT i.id AS Id, i.text AS Text, i.done AS Done, i.done_at AS DoneAt,
                       i.created_at AS CreatedAt, n.log_date AS LogDate,
                       n.deleted_at IS NOT NULL AS IsDeleted, c.name AS ClientName
                FROM checklist_items i
                JOIN notes n ON n.id = i.note_id
                LEFT JOIN clients c ON c.id = i.client_id
                WHERE i.kind = 'handoff'
                ORDER BY n.log_date, i.sort_order, i.id;
                """, transaction: transaction).ToList();
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            var visibleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                var id = Prefix + source.Id.ToString(CultureInfo.InvariantCulture) + ":"
                    + source.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture);
                sourceIds.Add(id);
                // 빈 입력 행과 휴지통 항목은 숨기되 보충 정보는 복원할 수 있도록 보관한다.
                if (source.IsDeleted || string.IsNullOrWhiteSpace(source.Text)) continue;
                visibleIds.Add(id);
                var isNew = !byId.TryGetValue(id, out var entry);
                entry ??= new WorkEntry { Id = id };
                var original = isNew ? null : JsonSerializer.Serialize(entry);
                var previous = entry.HandoffSnapshot is { } snapshot
                    ? JsonSerializer.Deserialize<SourceSnapshot>(snapshot) : null;
                var date = source.LogDate?.ToDateTime(TimeOnly.MinValue);
                if (previous is null || previous.Text != source.Text)
                    entry.Request = source.Text;
                if (previous is null || previous.ClientName != source.ClientName)
                    entry.Clients = string.IsNullOrWhiteSpace(source.ClientName) ? [] : [source.ClientName];
                if (previous is null || previous.LogDate != source.LogDate)
                    entry.RequestedDate = date;
                if (previous is null || entry.ProcessingStart is null
                    || entry.ProcessingStart == previous.LogDate?.ToDateTime(TimeOnly.MinValue))
                    entry.ProcessingStart = date;
                if (previous is null || previous.Done != source.Done)
                {
                    entry.Statuses = source.Done ? ["완료"] : ["대기중"];
                    entry.Done = source.Done;
                }
                if (previous is null || previous.Done != source.Done || previous.DoneAt != source.DoneAt
                    || (source.DoneAt is null && previous.LogDate != source.LogDate))
                    entry.ProcessingEnd = source.Done ? source.DoneAt?.LocalDateTime.Date ?? date : null;
                entry.HandoffSnapshot = JsonSerializer.Serialize(new SourceSnapshot(
                    source.Text, source.ClientName, source.LogDate, source.Done, source.DoneAt));
                var payload = JsonSerializer.Serialize(entry);
                if (payload != original)
                    connection.Execute("""
                        INSERT INTO work_entries(id, payload) VALUES(@Id, @Payload)
                        ON CONFLICT(id) DO UPDATE SET payload = excluded.payload;
                        """, new { Id = id, Payload = payload }, transaction);
                if (isNew) entries.Add(entry);
            }
            foreach (var stale in entries.Where(e => e.Id.StartsWith(Prefix, StringComparison.Ordinal) && !sourceIds.Contains(e.Id)))
                connection.Execute("DELETE FROM work_entries WHERE id = @Id;", new { stale.Id }, transaction);
            transaction.Commit();
            return entries.Where(e => !e.Id.StartsWith(Prefix, StringComparison.Ordinal) || visibleIds.Contains(e.Id)).ToList();
        }
    }

    private sealed record SourceSnapshot(string Text, string? ClientName, DateOnly? LogDate, bool Done, DateTimeOffset? DoneAt);

    private sealed class HandoffSource
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
        public bool Done { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateOnly? LogDate { get; set; }
        public bool IsDeleted { get; set; }
        public string? ClientName { get; set; }
    }
}

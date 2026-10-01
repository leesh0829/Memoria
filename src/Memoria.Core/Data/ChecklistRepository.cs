using Dapper;
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public sealed class ChecklistRepository : IChecklistRepository
{
    private const string SelectColumns =
        "id AS Id, note_id AS NoteId, kind AS Kind, text AS Text, done AS Done, done_at AS DoneAt, " +
        "client_id AS ClientId, is_manual AS IsManual, sort_order AS SortOrder, " +
        "created_at AS CreatedAt, updated_at AS UpdatedAt";

    private readonly SqliteConnectionFactory _factory;

    public ChecklistRepository(SqliteConnectionFactory factory) => _factory = factory;

    public int AddItem(ChecklistItem item)
    {
        var now = DateTimeOffset.UtcNow;
        item.CreatedAt = now;
        item.UpdatedAt = now;
        using (DatabaseDiagnostics.EnterWrite(_factory.WriteSync, "Checklist.AddItem"))
        {
            var conn = _factory.Write;
            conn.Execute(
                "INSERT INTO checklist_items(note_id, kind, text, done, done_at, client_id, is_manual, " +
                "sort_order, created_at, updated_at) " +
                "VALUES(@NoteId, @Kind, @Text, @Done, @DoneAt, @ClientId, @IsManual, " +
                "@SortOrder, @CreatedAt, @UpdatedAt);", item);
            item.Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid();");
        }
        return item.Id;
    }

    public void UpdateItem(ChecklistItem item)
    {
        using (DatabaseDiagnostics.EnterWrite(_factory.WriteSync, "Checklist.UpdateItem"))
        {
            _factory.Write.Execute(
                "UPDATE checklist_items SET kind = @Kind, text = @Text, done = @Done, done_at = @DoneAt, " +
                "client_id = @ClientId, is_manual = @IsManual, sort_order = @SortOrder, " +
                "updated_at = @UpdatedAt WHERE id = @Id;", item);
        }
    }

    public void UpdateItems(IReadOnlyList<ChecklistItem> items)
    {
        if (items.Count == 0) return;
        using (DatabaseDiagnostics.EnterWrite(_factory.WriteSync, "Checklist.UpdateItems"))
        {
            var conn = _factory.Write;
            using var tx = conn.BeginTransaction();
            conn.Execute(
                "UPDATE checklist_items SET kind = @Kind, text = @Text, done = @Done, done_at = @DoneAt, " +
                "client_id = @ClientId, is_manual = @IsManual, sort_order = @SortOrder, " +
                "updated_at = @UpdatedAt WHERE id = @Id;", items, tx);
            tx.Commit();
        }
    }

    public void UpdateItemMetadata(ChecklistItem item)
    {
        using (DatabaseDiagnostics.EnterWrite(_factory.WriteSync, "Checklist.UpdateItemMetadata"))
            _factory.Write.Execute(
                "UPDATE checklist_items SET done = @Done, done_at = @DoneAt, client_id = @ClientId, " +
                "is_manual = @IsManual, sort_order = @SortOrder, updated_at = @UpdatedAt WHERE id = @Id;", item);
    }

    public void SetSortOrders(IReadOnlyList<ChecklistItem> items)
    {
        if (items.Count == 0) return;
        using (DatabaseDiagnostics.EnterWrite(_factory.WriteSync, "Checklist.SetSortOrders"))
        {
            var conn = _factory.Write;
            using var tx = conn.BeginTransaction();
            conn.Execute(
                "UPDATE checklist_items SET sort_order = @SortOrder WHERE id = @Id;", items, tx);
            tx.Commit();
        }
    }

    public void DeleteItem(int id)
    {
        using (DatabaseDiagnostics.EnterWrite(_factory.WriteSync, "Checklist.DeleteItem"))
        {
            _factory.Write.Execute("DELETE FROM checklist_items WHERE id = @id;", new { id });
        }
    }

    public IReadOnlyList<ChecklistItem> GetByNote(int noteId)
    {
        using var perf = DatabaseDiagnostics.Measure("Checklist.GetByNote");
        using var conn = _factory.Open();
        return conn.Query<ChecklistItem>(
            $"SELECT {SelectColumns} FROM checklist_items WHERE note_id = @noteId " +
            "ORDER BY sort_order, id;", new { noteId }).ToList();
    }

    public IReadOnlyList<ChecklistItem> GetByNotes(IReadOnlyList<int> noteIds)
    {
        using var perf = DatabaseDiagnostics.Measure("Checklist.GetByNotes");
        if (noteIds.Count == 0) return [];
        using var conn = _factory.Open();
        return conn.Query<ChecklistItem>(
            $"SELECT {SelectColumns} FROM checklist_items WHERE note_id IN @noteIds " +
            "ORDER BY note_id, sort_order, id;", new { noteIds }).ToList();
    }
}

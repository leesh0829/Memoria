using System.Text.Json;
using Dapper;
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public interface IWorkEntryRepository
{
    IReadOnlyList<WorkEntry> GetAll();
    void Save(WorkEntry entry);
    void Delete(string id);
}

public sealed class WorkEntryRepository(SqliteConnectionFactory factory) : IWorkEntryRepository
{
    public IReadOnlyList<WorkEntry> GetAll()
        => HandoffWorkEntrySynchronizer.ReadAll(factory);

    public void Save(WorkEntry entry)
    {
        using (DatabaseDiagnostics.EnterWrite(factory.WriteSync, "WorkEntry.Save"))
            factory.Write.Execute("INSERT INTO work_entries(id, payload) VALUES(@Id, @Payload) " +
                "ON CONFLICT(id) DO UPDATE SET payload = excluded.payload;",
                new { entry.Id, Payload = JsonSerializer.Serialize(entry) });
    }

    public void Delete(string id)
    {
        using (DatabaseDiagnostics.EnterWrite(factory.WriteSync, "WorkEntry.Delete"))
            factory.Write.Execute("DELETE FROM work_entries WHERE id = @id;", new { id });
    }
}

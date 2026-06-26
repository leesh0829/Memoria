using System.Globalization;
using Dapper;
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public sealed class NoteRepository : INoteRepository
{
    private const string SelectColumns =
        "id AS Id, group_id AS GroupId, type AS Type, title AS Title, body AS Body, " +
        "log_date AS LogDate, report_format AS ReportFormat, report_week_start AS ReportWeekStart, " +
        "pinned AS Pinned, sort_order AS SortOrder, deleted_at AS DeletedAt, " +
        "created_at AS CreatedAt, updated_at AS UpdatedAt";

    private readonly SqliteConnectionFactory _factory;

    public NoteRepository(SqliteConnectionFactory factory) => _factory = factory;

    // Dapper does not use registered TypeHandlers for enum types — it uses Enum.Parse
    // (case-insensitive) for result mapping and stores the underlying int for parameters.
    // We manually convert to the C# enum name so both INSERT and Enum.Parse are consistent.
    private static string NoteTypeToString(NoteType t) => t switch
    {
        NoteType.Plain => nameof(NoteType.Plain),
        NoteType.Checklist => nameof(NoteType.Checklist),
        NoteType.WeeklyReport => nameof(NoteType.WeeklyReport),
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    // ReportFormatKind names "A" / "B" already match the desired DB strings.
    private static string? ReportFormatToString(ReportFormatKind? f) => f switch
    {
        null => null,
        ReportFormatKind.A => nameof(ReportFormatKind.A),
        ReportFormatKind.B => nameof(ReportFormatKind.B),
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    public int Create(Note note)
    {
        var now = DateTimeOffset.UtcNow;
        note.CreatedAt = now;
        note.UpdatedAt = now;
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            conn.Execute(
                "INSERT INTO notes(group_id, type, title, body, log_date, report_format, report_week_start, " +
                "pinned, sort_order, deleted_at, created_at, updated_at) " +
                "VALUES(@GroupId, @Type, @Title, @Body, @LogDate, @ReportFormat, @ReportWeekStart, " +
                "@Pinned, @SortOrder, @DeletedAt, @CreatedAt, @UpdatedAt);",
                new
                {
                    note.GroupId,
                    Type = NoteTypeToString(note.Type),
                    note.Title,
                    note.Body,
                    note.LogDate,
                    ReportFormat = ReportFormatToString(note.ReportFormat),
                    note.ReportWeekStart,
                    note.Pinned,
                    note.SortOrder,
                    note.DeletedAt,
                    note.CreatedAt,
                    note.UpdatedAt,
                });
            note.Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid();");
        }
        return note.Id;
    }

    public void Update(Note note)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute(
                "UPDATE notes SET group_id = @GroupId, type = @Type, title = @Title, body = @Body, " +
                "log_date = @LogDate, report_format = @ReportFormat, report_week_start = @ReportWeekStart, " +
                "pinned = @Pinned, sort_order = @SortOrder, deleted_at = @DeletedAt, " +
                "created_at = @CreatedAt, updated_at = @UpdatedAt WHERE id = @Id;",
                new
                {
                    note.GroupId,
                    Type = NoteTypeToString(note.Type),
                    note.Title,
                    note.Body,
                    note.LogDate,
                    ReportFormat = ReportFormatToString(note.ReportFormat),
                    note.ReportWeekStart,
                    note.Pinned,
                    note.SortOrder,
                    note.DeletedAt,
                    note.CreatedAt,
                    note.UpdatedAt,
                    note.Id,
                });
        }
    }

    public void SoftDelete(int id)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute("UPDATE notes SET deleted_at = @now WHERE id = @id;",
                new { id, now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) });
        }
    }

    public void Restore(int id)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute("UPDATE notes SET deleted_at = NULL WHERE id = @id;", new { id });
        }
    }

    public void Purge(int id)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute("DELETE FROM notes WHERE id = @id;", new { id });
        }
    }

    public void PurgeExpiredTrash(int retentionDays)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToString("O", CultureInfo.InvariantCulture);
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute(
                "DELETE FROM notes WHERE deleted_at IS NOT NULL AND deleted_at < @cutoff;", new { cutoff });
        }
    }

    public Note? Get(int id)
    {
        using var conn = _factory.Open();
        return conn.QuerySingleOrDefault<Note>(
            $"SELECT {SelectColumns} FROM notes WHERE id = @id;", new { id });
    }

    public IReadOnlyList<Note> GetByGroup(int? groupId)
    {
        using var conn = _factory.Open();
        var where = groupId is null ? "group_id IS NULL" : "group_id = @groupId";
        return conn.Query<Note>(
            $"SELECT {SelectColumns} FROM notes WHERE deleted_at IS NULL AND {where} " +
            "ORDER BY pinned DESC, updated_at DESC, id DESC;", new { groupId }).ToList();
    }

    public IReadOnlyList<Note> GetTrash()
    {
        using var conn = _factory.Open();
        return conn.Query<Note>(
            $"SELECT {SelectColumns} FROM notes WHERE deleted_at IS NOT NULL " +
            "ORDER BY deleted_at DESC, id DESC;").ToList();
    }

    public IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday)
    {
        using var conn = _factory.Open();
        return conn.Query<Note>(
            $"SELECT {SelectColumns} FROM notes " +
            "WHERE type = 'Checklist' AND deleted_at IS NULL " +
            "AND log_date BETWEEN @Monday AND @Friday " +
            "ORDER BY log_date, id;",
            new
            {
                Monday = monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Friday = friday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }).ToList();
    }

    public Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format)
    {
        using var conn = _factory.Open();
        return conn.QuerySingleOrDefault<Note>(
            $"SELECT {SelectColumns} FROM notes " +
            "WHERE type = 'WeeklyReport' AND deleted_at IS NULL " +
            "AND report_week_start = @WeekStart AND report_format = @Format LIMIT 1;",
            new
            {
                WeekStart = weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Format = format == ReportFormatKind.A ? nameof(ReportFormatKind.A) : nameof(ReportFormatKind.B),
            });
    }
}

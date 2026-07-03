using Dapper;
using FluentAssertions;
using Memoria.Core.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Memoria.Tests.Data;

public class MigrationV2Tests
{
    [Fact]
    public void FreshDb_MigratesTo_V2_WithBodyFormatColumn()
    {
        using var db = new TestDb();   // EnsureReady() 실행됨
        using var conn = db.Factory.Open();

        conn.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(2);
        var cols = conn.Query<string>("SELECT name FROM pragma_table_info('notes');");
        cols.Should().Contain("body_format");
    }

    [Fact]
    public void ExistingV1Db_Upgrades_AndDefaultsExistingRowsToPlain()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "memoria_v1_" + System.Guid.NewGuid().ToString("N") + ".db");
        try
        {
            // v1 상태를 흉내: body_format 없는 notes 테이블 + user_version=1 + _migrations(1).
            using (var factory0 = new SqliteConnectionFactory(path))
            {
                var c = factory0.Write;
                c.Execute("CREATE TABLE _migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);");
                c.Execute("INSERT INTO _migrations(version, applied_at) VALUES(1, 'x');");
                c.Execute("CREATE TABLE notes (id INTEGER PRIMARY KEY, title TEXT, body TEXT);");
                c.Execute("INSERT INTO notes(id, title, body) VALUES(1, 't', 'b');");
                c.Execute("PRAGMA user_version = 1;");
            }
            SqliteConnection.ClearAllPools();

            using (var factory1 = new SqliteConnectionFactory(path))
            {
                new DatabaseInitializer(factory1).EnsureReady();   // v1 → v2
                var c = factory1.Write;
                c.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(2);
                c.ExecuteScalar<string>("SELECT body_format FROM notes WHERE id = 1;").Should().Be("plain");
            }
            SqliteConnection.ClearAllPools();
        }
        finally
        {
            foreach (var p in new[] { path, path + "-wal", path + "-shm" })
                if (System.IO.File.Exists(p)) { try { System.IO.File.Delete(p); } catch { } }
        }
    }
}

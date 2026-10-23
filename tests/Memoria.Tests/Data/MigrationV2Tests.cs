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

        conn.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(7);
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
                c.Execute("CREATE TABLE notes (id INTEGER PRIMARY KEY, title TEXT, body TEXT, report_format TEXT);");
                c.Execute("INSERT INTO notes(id, title, body) VALUES(1, 't', 'b');");
                // v3 마이그레이션(clients 개명)이 존재하므로 v1 흉내에도 clients 테이블이 있어야 한다.
                c.Execute("CREATE TABLE clients (id INTEGER PRIMARY KEY, name TEXT NOT NULL, sort_order INTEGER NOT NULL, enabled INTEGER NOT NULL DEFAULT 1);");
                c.Execute("CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);");
                c.Execute("CREATE TABLE client_rules (id INTEGER PRIMARY KEY, client_id INTEGER NOT NULL, keyword TEXT NOT NULL, priority INTEGER NOT NULL);");
                c.Execute("PRAGMA user_version = 1;");
            }
            SqliteConnection.ClearAllPools();

            using (var factory1 = new SqliteConnectionFactory(path))
            {
                new DatabaseInitializer(factory1).EnsureReady();   // v1 → 최신
                var c = factory1.Write;
                c.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(7);
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

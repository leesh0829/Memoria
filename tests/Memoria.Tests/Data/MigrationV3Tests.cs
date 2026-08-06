using Dapper;
using FluentAssertions;
using Memoria.Core.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Memoria.Tests.Data;

public class MigrationV3Tests
{
    [Fact]
    public void FreshDb_SeedsAutoFactoryClient_WithSldPrefix()
    {
        using var db = new TestDb();   // EnsureReady() 실행됨
        using var conn = db.Factory.Open();

        conn.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(3);
        conn.ExecuteScalar<long>("SELECT COUNT(*) FROM clients WHERE name = 'SLD 자율형공장';").Should().Be(1);
        conn.ExecuteScalar<long>("SELECT COUNT(*) FROM clients WHERE name = '자율형 공장';").Should().Be(0);
    }

    [Fact]
    public void ExistingV2Db_RenamesAutoFactoryClient_ToSldPrefix_KeepingRulesLinkedById()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "memoria_v2_" + System.Guid.NewGuid().ToString("N") + ".db");
        try
        {
            // v2 상태 흉내: clients 테이블에 '자율형 공장' 행 + user_version=2 + _migrations(1,2).
            using (var factory0 = new SqliteConnectionFactory(path))
            {
                var c = factory0.Write;
                c.Execute("CREATE TABLE _migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);");
                c.Execute("INSERT INTO _migrations(version, applied_at) VALUES(1, 'x'), (2, 'x');");
                c.Execute("CREATE TABLE clients (id INTEGER PRIMARY KEY, name TEXT NOT NULL, sort_order INTEGER NOT NULL, enabled INTEGER NOT NULL DEFAULT 1);");
                c.Execute("INSERT INTO clients(name, sort_order, enabled) VALUES('자율형 공장', 5, 1);");
                c.Execute("PRAGMA user_version = 2;");
            }
            SqliteConnection.ClearAllPools();

            using (var factory1 = new SqliteConnectionFactory(path))
            {
                new DatabaseInitializer(factory1).EnsureReady();   // v2 → v3
                var c = factory1.Write;
                c.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(3);
                c.ExecuteScalar<string>("SELECT name FROM clients WHERE id = 1;").Should().Be("SLD 자율형공장");
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

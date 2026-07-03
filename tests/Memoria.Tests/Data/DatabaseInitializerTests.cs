using Dapper;
using FluentAssertions;
using Memoria.Core;
using Memoria.Core.Data;
using Xunit;

namespace Memoria.Tests.Data;

public class DatabaseInitializerTests
{
    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), "memoria_init_" + Guid.NewGuid().ToString("N") + ".db");

    [Fact]
    public void EnsureReady_CreatesSchema_SetsUserVersion_AndSeeds()
    {
        var path = NewDbPath();
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();

            File.Exists(path).Should().BeTrue();

            using var conn = factory.Open();
            conn.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(2);   // v2: body_format 마이그레이션 후
            conn.ExecuteScalar<string>("PRAGMA journal_mode;").Should().Be("wal");

            conn.ExecuteScalar<long>("SELECT COUNT(*) FROM clients;").Should().Be(6);
            conn.ExecuteScalar<long>("SELECT COUNT(*) FROM groups WHERE is_system = 1;").Should().Be(2);
            conn.ExecuteScalar<string>(
                "SELECT value FROM settings WHERE key = @k;",
                new { k = SettingsKeys.ReporterName }).Should().Be("이승현");
            conn.ExecuteScalar<string>(
                "SELECT value FROM settings WHERE key = @k;",
                new { k = SettingsKeys.ReportIndent }).Should().Be("\t");
            // 분류 규칙: 자율형공장 키워드가 SLD보다 낮은(우선) priority
            var autoPriority = conn.ExecuteScalar<long>(
                "SELECT priority FROM client_rules WHERE keyword = '자율형공장';");
            var sldPriority = conn.ExecuteScalar<long>(
                "SELECT priority FROM client_rules WHERE keyword = 'SLD';");
            autoPriority.Should().BeLessThan(sldPriority);
        }
        finally
        {
            factory.Dispose();  // 영속 쓰기 연결 닫기(파일 잠금 해제)
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var p in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(p)) File.Delete(p);
        }
    }

    [Fact]
    public void EnsureReady_IsIdempotent_DoesNotDuplicateSeed()
    {
        var path = NewDbPath();
        var factory = new SqliteConnectionFactory(path);
        try
        {
            var init = new DatabaseInitializer(factory);
            init.EnsureReady();
            init.EnsureReady(); // 두 번째 호출은 마이그레이션/시드를 다시 적용하지 않음

            using var conn = factory.Open();
            conn.ExecuteScalar<long>("SELECT COUNT(*) FROM clients;").Should().Be(6);
        }
        finally
        {
            factory.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var p in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(p)) File.Delete(p);
        }
    }

    [Fact]
    public void CheckIntegrity_ReturnsTrue_ForFreshDb()
    {
        var path = NewDbPath();
        var factory = new SqliteConnectionFactory(path);
        try
        {
            var init = new DatabaseInitializer(factory);
            init.EnsureReady();
            init.CheckIntegrity().Should().BeTrue();
        }
        finally
        {
            factory.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var p in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(p)) File.Delete(p);
        }
    }
}

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
            conn.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(7);   // v7: SLD 도금 고객사/분류 규칙
            conn.ExecuteScalar<string>("PRAGMA journal_mode;").Should().Be("wal");

            conn.ExecuteScalar<long>("SELECT COUNT(*) FROM clients;").Should().Be(7);
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

            conn.ExecuteScalar<string>("SELECT sql FROM sqlite_master WHERE type='trigger' AND name='notes_au';")
                .Should().Contain("UPDATE OF title, body");
            conn.ExecuteScalar<string>("SELECT sql FROM sqlite_master WHERE type='trigger' AND name='items_au';")
                .Should().Contain("UPDATE OF text");
            conn.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_notes_group_active_order';")
                .Should().Be(1);
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
            conn.ExecuteScalar<long>("SELECT COUNT(*) FROM clients;").Should().Be(7);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void V7_AddsPlatingWithoutDuplicates_AndClassifiesBeforeGenericSld(bool existingClient)
    {
        using var db = new TestDb();
        int? existingId;
        lock (db.Factory.WriteSync)
        {
            var writer = db.Factory.Write;
            writer.Execute("DELETE FROM client_rules WHERE keyword = 'SLD 도금';");
            if (!existingClient) writer.Execute("DELETE FROM clients WHERE name = 'SLD 도금';");
            existingId = writer.ExecuteScalar<int?>("SELECT id FROM clients WHERE name = 'SLD 도금';");
            writer.Execute("DELETE FROM _migrations WHERE version = 7; PRAGMA user_version = 6;");
        }
        var initializer = new DatabaseInitializer(db.Factory);
        initializer.EnsureReady();
        initializer.EnsureReady();
        using var conn = db.Factory.Open();
        var clients = new ClientRepository(db.Factory);
        var plating = clients.GetAll().Single(c => c.Name == "SLD 도금");
        if (existingClient) plating.Id.Should().Be(existingId);
        conn.ExecuteScalar<int>("SELECT COUNT(*) FROM client_rules WHERE keyword = 'SLD 도금';").Should().Be(1);
        new Memoria.Core.Classification.ClientClassifier()
            .Classify("SLD 도금 점검", clients.GetRules(), clients.GetAll(true).Select(c => c.Id).ToHashSet())
            .Should().Be(plating.Id);
    }

}

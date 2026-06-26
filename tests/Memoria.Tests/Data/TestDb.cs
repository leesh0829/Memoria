using Memoria.Core.Data;
using Microsoft.Data.Sqlite;

namespace Memoria.Tests.Data;

internal sealed class TestDb : IDisposable
{
    public string Path { get; }
    public SqliteConnectionFactory Factory { get; }

    public TestDb()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "memoria_test_" + Guid.NewGuid().ToString("N") + ".db");
        Factory = new SqliteConnectionFactory(Path);
        new DatabaseInitializer(Factory).EnsureReady();
    }

    public void Dispose()
    {
        Factory.Dispose();   // 영속 쓰기 연결 닫기(파일 잠금 해제) — 계약 §8
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { Path, Path + "-wal", Path + "-shm" })
            if (File.Exists(p)) { try { File.Delete(p); } catch { /* best-effort cleanup */ } }
    }
}

using Dapper;
using Microsoft.Data.Sqlite;

namespace Memoria.Core.Data;

public sealed class SqliteConnectionFactory : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection _writeConnection;

    /// 모든 쓰기를 직렬화하는 락(스펙 §7.7 단일 직렬 라이터, 계약 §8).
    public object WriteSync { get; } = new();

    /// 원본 DB 파일 경로(백업/복원에서 사용).
    public string DatabasePath { get; }

    public SqliteConnectionFactory(string dbPath)
    {
        DapperConfig.EnsureRegistered();
        DatabasePath = dbPath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        _writeConnection = OpenConfigured(setWal: true);
    }

    /// 단일 영속 쓰기 연결. 반드시 `lock (WriteSync)` 안에서만 사용한다(직렬 라이터).
    public SqliteConnection Write => _writeConnection;

    /// 읽기 전용 연결(WAL 동시 읽기). 호출자가 `using`으로 해제한다.
    public SqliteConnection Open() => OpenConfigured(setWal: false);

    private SqliteConnection OpenConfigured(bool setWal)
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        conn.Execute(
            (setWal ? "PRAGMA journal_mode = WAL; " : "") +
            "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
        return conn;
    }

    /// 복원(IBackupService.TryRestoreFromLatestBackup) 전용: 쓰기 연결을 닫아 파일 잠금을 해제한다.
    /// 호출자가 `lock (WriteSync)`를 보유한 상태에서만 사용한다.
    internal void CloseForRestore() => _writeConnection.Dispose();

    /// 복원 후 쓰기 연결을 다시 연다(WAL 재설정). 호출자가 `lock (WriteSync)`를 보유한 상태에서만 사용한다.
    internal void ReopenAfterRestore() => _writeConnection = OpenConfigured(setWal: true);

    public void Dispose()
    {
        lock (WriteSync)
        {
            try { _writeConnection.Execute("PRAGMA wal_checkpoint(TRUNCATE);"); }
            catch (SqliteException) { /* best-effort checkpoint */ }
            _writeConnection.Dispose();
        }
    }
}

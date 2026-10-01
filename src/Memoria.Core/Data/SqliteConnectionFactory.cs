using Dapper;
using Microsoft.Data.Sqlite;

namespace Memoria.Core.Data;

public sealed class SqliteConnectionFactory : IDisposable
{
    private readonly string _writeConnectionString;
    private readonly string _readConnectionString;
    private SqliteConnection _writeConnection;

    /// 모든 쓰기를 직렬화하는 락(스펙 §7.7 단일 직렬 라이터, 계약 §8).
    public object WriteSync { get; } = new();

    /// 원본 DB 파일 경로(백업/복원에서 사용).
    public string DatabasePath { get; }

    public SqliteConnectionFactory(string dbPath)
    {
        DapperConfig.EnsureRegistered();
        DatabasePath = dbPath;
        _writeConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
        _readConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = true,
        }.ToString();

        _writeConnection = OpenConfigured(_writeConnectionString, setWal: true);
    }

    /// 단일 영속 쓰기 연결. 반드시 `lock (WriteSync)` 안에서만 사용한다(직렬 라이터).
    public SqliteConnection Write => _writeConnection;

    /// 읽기 전용 연결(WAL 동시 읽기). 호출자가 `using`으로 해제한다.
    public SqliteConnection Open() => OpenConfigured(_readConnectionString, setWal: false);

    private static SqliteConnection OpenConfigured(string connectionString, bool setWal)
    {
        var conn = new SqliteConnection(connectionString);
        conn.Open();
        conn.Execute(
            (setWal
                ? "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA wal_autocheckpoint = 256; "
                : "") +
            // 단일 writer 락 + WAL 구조이므로 정상 동작은 즉시 끝난다. 실제 경합에서도
            // UI가 5초씩 멈추지 않도록 대기를 500ms로 제한한다.
            "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 500;");
        return conn;
    }

    /// 복원(IBackupService.TryRestoreFromLatestBackup) 전용: 쓰기 연결을 닫아 파일 잠금을 해제한다.
    /// 호출자가 `lock (WriteSync)`를 보유한 상태에서만 사용한다.
    internal void CloseForRestore() => _writeConnection.Dispose();

    internal void ClearPools()
    {
        using var writeKey = new SqliteConnection(_writeConnectionString);
        using var readKey = new SqliteConnection(_readConnectionString);
        SqliteConnection.ClearPool(writeKey);
        SqliteConnection.ClearPool(readKey);
    }

    /// 복원 후 쓰기 연결을 다시 연다(WAL 재설정). 호출자가 `lock (WriteSync)`를 보유한 상태에서만 사용한다.
    internal void ReopenAfterRestore() => _writeConnection = OpenConfigured(_writeConnectionString, setWal: true);

    public void Dispose()
    {
        using (DatabaseDiagnostics.EnterWrite(WriteSync, "Database.Dispose"))
        {
            // PASSIVE는 활성 reader를 기다리지 않아 종료 UI가 멎지 않는다. 남은 WAL은
            // 다음 시작에 SQLite가 안전하게 재사용/체크포인트한다.
            try { _writeConnection.Execute("PRAGMA wal_checkpoint(PASSIVE);"); }
            catch (SqliteException) { /* best-effort checkpoint */ }
            _writeConnection.Dispose();
            ClearPools();
        }
    }
}

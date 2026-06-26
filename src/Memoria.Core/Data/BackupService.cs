using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Memoria.Core.Data;

public sealed class BackupService : IBackupService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly string _databasePath;
    private readonly string _backupDirectory;

    public BackupService(SqliteConnectionFactory factory)
    {
        _factory = factory;
        // 실제 쓰기 연결과 동일한 DB 경로를 사용해 백업/격리 대상이 어긋나지 않게 한다(계약 §8).
        _databasePath = factory.DatabasePath;
        _backupDirectory = Path.Combine(Path.GetDirectoryName(_databasePath)!, "backups");
    }

    public bool BackupIfDue(int retentionCount)
    {
        Directory.CreateDirectory(_backupDirectory);
        var today = DateTimeOffset.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var target = Path.Combine(_backupDirectory, $"memoria-{today}.db");
        if (File.Exists(target)) return false; // 오늘 이미 백업함

        // VACUUM INTO 는 바인딩 파라미터를 받지 않으므로 경로를 문자열 리터럴로 이스케이프해 주입.
        var literal = target.Replace("'", "''");
        // 스냅샷(VACUUM INTO)과 로테이션을 모두 쓰기 락 안에서 직렬화한다(계약 §8).
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute($"VACUUM INTO '{literal}';");
            RotateBackups(retentionCount);
        }

        return true;
    }

    public bool IsDatabaseHealthy()
    {
        try
        {
            using var conn = _factory.Open();
            var result = conn.ExecuteScalar<string>("PRAGMA integrity_check;");
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public bool TryRestoreFromLatestBackup()
    {
        // 최신→과거 순으로 순회하며 PRAGMA integrity_check 를 통과하는 첫 백업을 고른다.
        // 손상된 백업으로는 복원하지 않는다(정상 백업이 없으면 false).
        var validBackup = EnumerateBackupsNewestFirst().FirstOrDefault(IsBackupHealthy);
        if (validBackup is null) return false;

        lock (_factory.WriteSync)
        {
            _factory.CloseForRestore();       // 영속 쓰기 연결 닫기(파일 잠금 해제)
            SqliteConnection.ClearAllPools(); // 풀링된 읽기 연결도 해제

            var temp = _databasePath + ".restore-tmp";
            try
            {
                // 백업을 임시 파일로 먼저 복사한다. 복사 실패 시 원본 DB 는 손대지 않는다
                // (격리/빈 DB 생성으로 인한 데이터 유실 방지).
                SafeDelete(temp);
                File.Copy(validBackup, temp, overwrite: true);
            }
            catch (IOException)
            {
                SafeDelete(temp);
                _factory.ReopenAfterRestore(); // 원본은 그대로 — 쓰기 연결만 재개
                return false;
            }

            try
            {
                // 복사가 성공한 뒤에만 현재(손상) DB 를 격리하고 임시 파일을 원자적으로 교체한다.
                QuarantineCurrentDatabase();
                File.Move(temp, _databasePath, overwrite: true);
            }
            finally
            {
                _factory.ReopenAfterRestore(); // 성공/실패와 무관하게 쓰기 연결 항상 재개
            }
        }
        return true;
    }

    // 백업 후보를 읽기 전용으로 열어 PRAGMA integrity_check 통과 여부를 확인한다.
    private static bool IsBackupHealthy(string backupPath)
    {
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false, // 검증 후 파일 핸들을 즉시 해제(이후 File.Copy 보장)
            }.ToString();
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            var result = conn.ExecuteScalar<string>("PRAGMA integrity_check;");
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqliteException) { return false; }
        catch (IOException) { return false; }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* best-effort */ }
    }

    private void RotateBackups(int retentionCount)
    {
        foreach (var old in EnumerateBackupsNewestFirst().Skip(Math.Max(retentionCount, 0)))
        {
            try { File.Delete(old); } catch (IOException) { /* best-effort rotation */ }
        }
    }

    // 백업 파일을 파일명(=날짜) 내림차순(최신 먼저)으로 반환.
    private IReadOnlyList<string> EnumerateBackupsNewestFirst()
    {
        if (!Directory.Exists(_backupDirectory)) return [];
        return Directory.GetFiles(_backupDirectory, "memoria-*.db")
            .OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal)
            .ToList();
    }

    private void QuarantineCurrentDatabase()
    {
        var stamp = DateTimeOffset.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var src = _databasePath + suffix;
            // 모든 격리 파일이 .corrupt 로 끝나게 해 `*.corrupt` glob 으로 세 파일을 모두 잡는다(계약 §8).
            if (File.Exists(src))
                File.Move(src, $"{_databasePath}{suffix}.{stamp}.corrupt", overwrite: true);
        }
    }
}

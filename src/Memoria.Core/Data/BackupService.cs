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
        var latest = EnumerateBackupsNewestFirst().FirstOrDefault();
        if (latest is null) return false;

        lock (_factory.WriteSync)
        {
            _factory.CloseForRestore();      // 영속 쓰기 연결 닫기(파일 잠금 해제)
            SqliteConnection.ClearAllPools(); // 풀링된 읽기 연결도 해제
            QuarantineCurrentDatabase();
            File.Copy(latest, _databasePath, overwrite: true);
            _factory.ReopenAfterRestore();   // 새 DB로 쓰기 연결 재개
        }
        return true;
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

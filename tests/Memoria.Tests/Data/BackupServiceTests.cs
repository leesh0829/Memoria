using System.Globalization;
using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Memoria.Tests.Data;

public class BackupServiceTests
{
    // 각 테스트는 전용 임시 디렉터리(=DB 파일의 부모) 안에서 동작한다.
    private static string NewDbPath() =>
        Path.Combine(
            Path.GetTempPath(),
            "memoria_backup_" + Guid.NewGuid().ToString("N"),
            "memoria.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        var dir = Path.GetDirectoryName(dbPath)!;
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private static string BackupsDir(string dbPath) =>
        Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");

    [Fact]
    public void BackupIfDue_CreatesDatedSnapshot_FirstTime_ThenSkipsSameDay()
    {
        var path = NewDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();
            var sut = new BackupService(factory);

            sut.BackupIfDue(7).Should().BeTrue();
            Directory.GetFiles(BackupsDir(path), "memoria-*.db").Should().HaveCount(1);

            sut.BackupIfDue(7).Should().BeFalse(); // 같은 날 두 번째 호출은 skip
            Directory.GetFiles(BackupsDir(path), "memoria-*.db").Should().HaveCount(1);
        }
        finally { factory.Dispose(); Cleanup(path); }
    }

    [Fact]
    public void BackupIfDue_RetainsOnlyRetentionCount_NewestBackups()
    {
        var path = NewDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();
            Directory.CreateDirectory(BackupsDir(path));
            // 오래된 더미 백업 3개(파일명=날짜)
            foreach (var d in new[] { "20200101", "20200102", "20200103" })
                File.WriteAllText(Path.Combine(BackupsDir(path), $"memoria-{d}.db"), "x");

            new BackupService(factory).BackupIfDue(2).Should().BeTrue();

            var remaining = Directory.GetFiles(BackupsDir(path), "memoria-*.db")
                .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            remaining.Should().HaveCount(2);
            var today = DateTimeOffset.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            remaining.Should().Contain($"memoria-{today}.db"); // 오늘 백업은 유지
        }
        finally { factory.Dispose(); Cleanup(path); }
    }

    [Fact]
    public void IsDatabaseHealthy_ReturnsTrue_ForFreshDb()
    {
        var path = NewDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();
            new BackupService(factory).IsDatabaseHealthy().Should().BeTrue();
        }
        finally { factory.Dispose(); Cleanup(path); }
    }

    [Fact]
    public void TryRestoreFromLatestBackup_ReturnsFalse_WhenNoBackupExists()
    {
        var path = NewDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();
            new BackupService(factory).TryRestoreFromLatestBackup().Should().BeFalse();
        }
        finally { factory.Dispose(); Cleanup(path); }
    }

    [Fact]
    public void TryRestoreFromLatestBackup_SkipsCorruptBackup_AndUsesNewestHealthyOne()
    {
        var path = NewDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();
            var notes = new NoteRepository(factory);
            var beforeId = notes.Create(new Note { Type = NoteType.Plain, Title = "before" });

            var sut = new BackupService(factory);
            sut.BackupIfDue(7).Should().BeTrue();              // 오늘 날짜의 정상 백업 생성

            // 더 최신 이름(미래 날짜)의 손상 백업 — 무결성 검증에서 건너뛰어야 한다.
            File.WriteAllText(Path.Combine(BackupsDir(path), "memoria-99991231.db"), "not a database");

            var afterId = notes.Create(new Note { Type = NoteType.Plain, Title = "after" });

            sut.TryRestoreFromLatestBackup().Should().BeTrue();

            notes.Get(beforeId).Should().NotBeNull();          // 정상(오늘) 백업으로 복원됨
            notes.Get(afterId).Should().BeNull();              // 백업 이후 변경은 사라짐
        }
        finally { factory.Dispose(); Cleanup(path); }
    }

    [Fact]
    public void TryRestoreFromLatestBackup_ReturnsFalse_AndKeepsOriginal_WhenOnlyCorruptBackups()
    {
        var path = NewDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();
            var notes = new NoteRepository(factory);
            var id = notes.Create(new Note { Type = NoteType.Plain, Title = "live" });

            Directory.CreateDirectory(BackupsDir(path));
            File.WriteAllText(Path.Combine(BackupsDir(path), "memoria-20200101.db"), "garbage");

            new BackupService(factory).TryRestoreFromLatestBackup().Should().BeFalse();

            // 정상 백업이 없으면 원본을 격리/교체하지 않고 그대로 보존 + 쓰기 연결도 계속 사용 가능.
            notes.Get(id).Should().NotBeNull();
            var dir = Path.GetDirectoryName(path)!;
            Directory.GetFiles(dir, "*.corrupt").Should().BeEmpty();
            notes.Create(new Note { Type = NoteType.Plain, Title = "after-failed-restore" })
                .Should().BePositive();                        // 복원 실패 후에도 쓰기 가능(연결 재개 보장)
        }
        finally { factory.Dispose(); Cleanup(path); }
    }

    [Fact]
    public void TryRestoreFromLatestBackup_RollsBackToBackupState_AndQuarantinesCurrent()
    {
        var path = NewDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new SqliteConnectionFactory(path);
        try
        {
            new DatabaseInitializer(factory).EnsureReady();
            var notes = new NoteRepository(factory);
            var beforeId = notes.Create(new Note { Type = NoteType.Plain, Title = "before" });

            var sut = new BackupService(factory);
            sut.BackupIfDue(7).Should().BeTrue();              // 백업 스냅샷에는 'before'만 존재

            var afterId = notes.Create(new Note { Type = NoteType.Plain, Title = "after" }); // 백업 이후 추가

            sut.TryRestoreFromLatestBackup().Should().BeTrue();

            notes.Get(beforeId).Should().NotBeNull();          // 복원: 백업 시점 데이터 유지
            notes.Get(afterId).Should().BeNull();              // 복원: 백업 이후 변경은 사라짐

            var dir = Path.GetDirectoryName(path)!;
            Directory.GetFiles(dir, "*.corrupt").Should().NotBeEmpty(); // 격리 파일 생성
            // 격리된 모든 파일(db/-wal/-shm)은 .corrupt 로 끝나야 `*.corrupt` glob 이 전부 잡는다(계약 §8c).
            Directory.GetFiles(dir)
                .Where(f => f.Contains(".corrupt", StringComparison.Ordinal))
                .Should().OnlyContain(f => f.EndsWith(".corrupt", StringComparison.Ordinal));
        }
        finally { factory.Dispose(); Cleanup(path); }
    }
}

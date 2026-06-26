namespace Memoria.Core.Data;

public interface IBackupService
{
    /// 마지막 백업이 오늘이 아니면 backups/memoria-yyyyMMdd.db 로 일관 스냅샷(VACUUM INTO) 생성 후
    /// retentionCount 개만 남기고 오래된 것 삭제. 백업했으면 true.
    bool BackupIfDue(int retentionCount);
    /// PRAGMA integrity_check == 'ok' 이면 true.
    bool IsDatabaseHealthy();
    /// 손상 시: 현재 DB를 *.corrupt 로 격리 후 최신 정상 백업을 복원. 복원 성공 시 true(복원본 없으면 false).
    bool TryRestoreFromLatestBackup();
}

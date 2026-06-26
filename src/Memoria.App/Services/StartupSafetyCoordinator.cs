using Memoria.Core.Data;

namespace Memoria.App.Services;

public sealed class StartupSafetyCoordinator : IStartupSafetyCoordinator
{
    private readonly IBackupService _backup;

    public StartupSafetyCoordinator(IBackupService backup) => _backup = backup;

    public StartupSafetyOutcome Run(int retentionCount)
    {
        bool healthy;
        try
        {
            healthy = _backup.IsDatabaseHealthy();
        }
        catch (Exception ex)
        {
            // 무결성 점검 자체가 예외로 실패하면, 파괴적 복원을 피하기 위해 정상으로 간주하고 계속한다.
            AppLog.Error("StartupSafety.IsDatabaseHealthy", ex);
            healthy = true;
        }

        var restoreAttempted = false;
        var restoreSucceeded = false;
        if (!healthy)
        {
            restoreAttempted = true;
            try
            {
                restoreSucceeded = _backup.TryRestoreFromLatestBackup();
            }
            catch (Exception ex)
            {
                AppLog.Error("StartupSafety.TryRestoreFromLatestBackup", ex);
                restoreSucceeded = false;
            }
        }

        var backupCreated = false;
        if (healthy || restoreSucceeded)
        {
            try
            {
                backupCreated = _backup.BackupIfDue(retentionCount);
            }
            catch (Exception ex)
            {
                // 백업 실패는 경고만 — 시작은 계속한다.
                AppLog.Error("StartupSafety.BackupIfDue", ex);
                backupCreated = false;
            }
        }

        return new StartupSafetyOutcome(healthy, restoreAttempted, restoreSucceeded, backupCreated);
    }
}

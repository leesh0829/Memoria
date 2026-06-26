using Memoria.Core.Data;

namespace Memoria.App.Services;

public sealed class StartupSafetyCoordinator : IStartupSafetyCoordinator
{
    private readonly IBackupService _backup;

    public StartupSafetyCoordinator(IBackupService backup) => _backup = backup;

    public StartupSafetyOutcome Run(int retentionCount)
    {
        var healthy = _backup.IsDatabaseHealthy();

        var restoreAttempted = false;
        var restoreSucceeded = false;
        if (!healthy)
        {
            restoreAttempted = true;
            restoreSucceeded = _backup.TryRestoreFromLatestBackup();
        }

        var backupCreated = false;
        if (healthy || restoreSucceeded)
            backupCreated = _backup.BackupIfDue(retentionCount);

        return new StartupSafetyOutcome(healthy, restoreAttempted, restoreSucceeded, backupCreated);
    }
}

using Memoria.Core.Data;

namespace Memoria.Tests.App.Fakes;

internal sealed class FakeBackupService : IBackupService
{
    public bool Healthy { get; set; } = true;
    public bool RestoreSucceeds { get; set; }
    public bool BackupReturns { get; set; } = true;

    public int RestoreCalls { get; private set; }
    public int BackupCalls { get; private set; }
    public int? LastRetentionCount { get; private set; }

    public bool IsDatabaseHealthy() => Healthy;
    public bool TryRestoreFromLatestBackup() { RestoreCalls++; return RestoreSucceeds; }
    public bool BackupIfDue(int retentionCount)
    {
        BackupCalls++;
        LastRetentionCount = retentionCount;
        return BackupReturns;
    }
}

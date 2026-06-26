using FluentAssertions;
using Memoria.App.Services;
using Memoria.Tests.App.Fakes;
using Xunit;

namespace Memoria.Tests.App;

public class StartupSafetyCoordinatorTests
{
    [Fact]
    public void Healthy_db_skips_restore_and_runs_backup()
    {
        var backup = new FakeBackupService { Healthy = true };
        var outcome = new StartupSafetyCoordinator(backup).Run(7);

        backup.RestoreCalls.Should().Be(0);
        backup.BackupCalls.Should().Be(1);
        backup.LastRetentionCount.Should().Be(7);
        outcome.DatabaseWasHealthy.Should().BeTrue();
        outcome.RestoreAttempted.Should().BeFalse();
    }

    [Fact]
    public void Unhealthy_db_restored_then_backs_up()
    {
        var backup = new FakeBackupService { Healthy = false, RestoreSucceeds = true };
        var outcome = new StartupSafetyCoordinator(backup).Run(7);

        backup.RestoreCalls.Should().Be(1);
        backup.BackupCalls.Should().Be(1);
        outcome.RestoreAttempted.Should().BeTrue();
        outcome.RestoreSucceeded.Should().BeTrue();
    }

    [Fact]
    public void Unhealthy_db_with_failed_restore_does_not_back_up()
    {
        var backup = new FakeBackupService { Healthy = false, RestoreSucceeds = false };
        var outcome = new StartupSafetyCoordinator(backup).Run(7);

        backup.RestoreCalls.Should().Be(1);
        backup.BackupCalls.Should().Be(0);          // 복구 실패 시 손상 DB를 백업하지 않음
        outcome.RestoreSucceeded.Should().BeFalse();
        outcome.BackupCreated.Should().BeFalse();
    }
}

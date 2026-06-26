using System;
using FluentAssertions;
using Memoria.App.Services;
using Memoria.Core.Data;
using Memoria.Tests.App.Fakes;
using Xunit;

namespace Memoria.Tests.App;

public class StartupSafetyCoordinatorTests
{
    // 각 단계가 예외를 던져도 Run 은 전파하지 않고 outcome 플래그로 강등해 시작을 계속해야 한다.
    private sealed class ThrowingBackupService : IBackupService
    {
        public bool ThrowOnHealthy { get; init; }
        public bool ThrowOnRestore { get; init; }
        public bool ThrowOnBackup { get; init; }
        public bool Healthy { get; init; } = true;

        public bool IsDatabaseHealthy() =>
            ThrowOnHealthy ? throw new InvalidOperationException("health") : Healthy;
        public bool TryRestoreFromLatestBackup() =>
            ThrowOnRestore ? throw new InvalidOperationException("restore") : false;
        public bool BackupIfDue(int retentionCount) =>
            ThrowOnBackup ? throw new InvalidOperationException("backup") : true;
    }

    [Fact]
    public void Backup_failure_is_demoted_to_warning_and_does_not_throw()
    {
        var backup = new ThrowingBackupService { Healthy = true, ThrowOnBackup = true };
        var act = () => new StartupSafetyCoordinator(backup).Run(7);

        act.Should().NotThrow();
        act().BackupCreated.Should().BeFalse();
    }

    [Fact]
    public void Health_check_exception_is_treated_as_healthy_and_skips_destructive_restore()
    {
        var backup = new ThrowingBackupService { ThrowOnHealthy = true };
        StartupSafetyOutcome outcome = default!;
        var act = () => outcome = new StartupSafetyCoordinator(backup).Run(7);

        act.Should().NotThrow();
        outcome.DatabaseWasHealthy.Should().BeTrue();
        outcome.RestoreAttempted.Should().BeFalse();   // 파괴적 복원을 시도하지 않음
    }

    [Fact]
    public void Restore_exception_is_demoted_and_does_not_throw()
    {
        var backup = new ThrowingBackupService { Healthy = false, ThrowOnRestore = true };
        StartupSafetyOutcome outcome = default!;
        var act = () => outcome = new StartupSafetyCoordinator(backup).Run(7);

        act.Should().NotThrow();
        outcome.RestoreAttempted.Should().BeTrue();
        outcome.RestoreSucceeded.Should().BeFalse();
        outcome.BackupCreated.Should().BeFalse();      // 복원 실패 시 손상 DB 백업 안 함
    }

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

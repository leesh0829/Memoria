namespace Memoria.App.Services;

/// 계약 §9.4 step5/6: 무결성 점검 → (손상 시) 백업 복원 → (정상/복원 성공 시) 일일 백업.
public sealed record StartupSafetyOutcome(
    bool DatabaseWasHealthy,
    bool RestoreAttempted,
    bool RestoreSucceeded,
    bool BackupCreated);

public interface IStartupSafetyCoordinator
{
    StartupSafetyOutcome Run(int retentionCount);
}

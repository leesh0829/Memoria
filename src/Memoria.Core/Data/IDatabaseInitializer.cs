namespace Memoria.Core.Data;

public interface IDatabaseInitializer
{
    /// 파일 없으면 생성, PRAGMA(WAL/foreign_keys/busy_timeout) 설정,
    /// 마이그레이션 적용(user_version), 첫 실행 시드(clients/client_rules/시스템 그룹/settings 기본값).
    void EnsureReady();
    /// PRAGMA integrity_check 결과(true=정상).
    bool CheckIntegrity();
}

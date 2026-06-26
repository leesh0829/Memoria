// 테스트 컬렉션 병렬 실행 비활성화.
// 이유: 일부 테스트가 프로세스 전역 정적 상태(Memoria.App.AppServices provider)와
// SQLite 전역 리소스를 공유하여 병렬 실행 시 비결정적 실패가 발생할 수 있다.
// 전체 스위트가 1초 미만으로 빠르므로 직렬화 비용은 무시할 수 있고, CI(dotnet test 기본 병렬)에서
// 결정적으로 그린을 보장한다. (테스트별 임시 DB는 GUID 경로로 이미 격리되어 있음)
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
